"use server";

import { redirect } from "next/navigation";
import { adminLogin, confirmEnrollment, startEnrollment, verifyMfa } from "@/lib/admin-api";
import {
  clearMfaCookie,
  readMfaToken,
  safeNextPath,
  setMfaCookie,
  setSessionCookies,
} from "@/lib/session";
import type { LoginState } from "./state";

/**
 * The whole admin sign-in, in stages. Which stage runs is decided by the "intent" field, the same
 * way the public site's OTP form works.
 *
 * The password alone never produces a session: the API returns a token for the second stage, which
 * is kept in an httpOnly cookie and spent on one of the MFA endpoints. Only those issue the real
 * access and refresh tokens.
 */
export async function loginAction(previous: LoginState, formData: FormData): Promise<LoginState> {
  const intent = String(formData.get("intent") ?? "password");
  const next = String(formData.get("next") ?? "");

  if (intent === "password") {
    return await submitPassword(formData, next);
  }

  // Every later stage needs the token the password stage put in the cookie.
  const mfaToken = await readMfaToken();
  if (!mfaToken) {
    return { stage: "password", email: previous.email, error: "That took too long. Please sign in again." };
  }

  if (intent === "enroll-confirm") {
    return await confirmEnrollmentStage(previous, formData, mfaToken, next);
  }

  return await verifyStage(previous, formData, mfaToken, next);
}

async function submitPassword(formData: FormData, next: string): Promise<LoginState> {
  const email = String(formData.get("email") ?? "").trim();
  const password = String(formData.get("password") ?? "");

  if (!email || !password) {
    return { stage: "password", email, error: "Enter your email address and password." };
  }

  const result = await adminLogin(email, password);
  if (!result.ok) {
    return { stage: "password", email, error: result.message };
  }

  await setMfaCookie(result.data.mfaToken, result.data.expiresInSeconds);

  if (result.data.stage === "mfa") {
    return { stage: "mfa", email };
  }

  // No authenticator yet: fetch the secret so the admin can add it, then ask for the first code.
  const enrollment = await startEnrollment(result.data.mfaToken);
  if (!enrollment.ok) {
    await clearMfaCookie();
    return { stage: "password", email, error: enrollment.message };
  }

  return { stage: "enroll", email, enrollment: enrollment.data };
}

async function confirmEnrollmentStage(
  previous: LoginState,
  formData: FormData,
  mfaToken: string,
  next: string,
): Promise<LoginState> {
  const code = String(formData.get("code") ?? "").replace(/\s/g, "");
  if (!/^\d{6}$/.test(code)) {
    return { ...previous, stage: "enroll", error: "Enter the 6-digit code from your authenticator." };
  }

  const result = await confirmEnrollment(mfaToken, code);
  if (!result.ok) {
    return { ...previous, stage: "enroll", error: result.message };
  }

  // Enrolling signs the admin in as well, so the session starts here.
  await setSessionCookies(result.data);
  await clearMfaCookie();

  // The codes are shown once, so the redirect waits until they have been written down.
  return {
    stage: "codes",
    email: previous.email,
    recoveryCodes: result.data.recoveryCodes,
    next: safeNextPath(next),
  };
}

async function verifyStage(
  previous: LoginState,
  formData: FormData,
  mfaToken: string,
  next: string,
): Promise<LoginState> {
  const recoveryCode = String(formData.get("recoveryCode") ?? "").trim();
  const code = String(formData.get("code") ?? "").replace(/\s/g, "");

  if (!recoveryCode && !/^\d{6}$/.test(code)) {
    return { stage: "mfa", email: previous.email, error: "Enter the 6-digit code from your authenticator." };
  }

  const result = await verifyMfa(mfaToken, recoveryCode ? { recoveryCode } : { code });
  if (!result.ok) {
    return { stage: "mfa", email: previous.email, error: result.message };
  }

  await setSessionCookies(result.data);
  await clearMfaCookie();
  redirect(safeNextPath(next));
}
