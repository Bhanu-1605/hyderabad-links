"use server";

import { redirect } from "next/navigation";
import {
  confirmEnrollment,
  staffLogin,
  startEnrollment,
  verifyMfa,
  type StaffSession,
} from "@/lib/super-admin-api";
import {
  clearMfaCookie,
  readMfaToken,
  safeNextPath,
  setMfaCookie,
  setSessionCookies,
} from "@/lib/session";
import type { LoginState } from "./state";

/**
 * The staff sign-in, in stages, exactly as on the admin site: the password alone never produces a
 * session, and MFA is mandatory.
 *
 * The difference is the last step. This site stores the **super-admin** token, and an account
 * without `admins.manage` is never issued one — so it is told it has no platform access instead of
 * being let into a site where every request would be refused.
 */
export async function loginAction(previous: LoginState, formData: FormData): Promise<LoginState> {
  const intent = String(formData.get("intent") ?? "password");
  const next = String(formData.get("next") ?? "");

  if (intent === "password") {
    return await submitPassword(formData, next);
  }

  const mfaToken = await readMfaToken();
  if (!mfaToken) {
    return { stage: "password", email: previous.email, error: "That took too long. Please sign in again." };
  }

  if (intent === "enroll-confirm") {
    return await confirmEnrollmentStage(previous, formData, mfaToken, next);
  }

  return await verifyStage(previous, formData, mfaToken, next);
}

const NO_ACCESS =
  "That account does not have platform administration access. Use the admin site instead.";

/**
 * Keeps the session only when the sign-in produced a super-admin token. Returns the error to show
 * when it did not, so an ordinary administrator is turned away here rather than at every page.
 */
async function keepSession(session: StaffSession): Promise<string | null> {
  if (!session.superAdminAccessToken) {
    await clearMfaCookie();
    return NO_ACCESS;
  }

  await setSessionCookies({
    accessToken: session.superAdminAccessToken,
    refreshToken: session.refreshToken,
    expiresInSeconds: session.expiresInSeconds,
  });
  await clearMfaCookie();
  return null;
}

async function submitPassword(formData: FormData, next: string): Promise<LoginState> {
  const email = String(formData.get("email") ?? "").trim();
  const password = String(formData.get("password") ?? "");

  if (!email || !password) {
    return { stage: "password", email, error: "Enter your email address and password." };
  }

  const result = await staffLogin(email, password);
  if (!result.ok) {
    return { stage: "password", email, error: result.message };
  }

  await setMfaCookie(result.data.mfaToken, result.data.expiresInSeconds);

  if (result.data.stage === "mfa") {
    return { stage: "mfa", email };
  }

  // No authenticator yet: fetch the secret so it can be added, then ask for the first code.
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

  const refusal = await keepSession(result.data);
  if (refusal) {
    return { stage: "password", email: previous.email, error: refusal };
  }

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

  const refusal = await keepSession(result.data);
  if (refusal) {
    return { stage: "password", email: previous.email, error: refusal };
  }

  redirect(safeNextPath(next));
}
