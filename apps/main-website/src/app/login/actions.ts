"use server";

import { redirect } from "next/navigation";
import { requestOtp, verifyOtp } from "@/lib/owner-api";
import { safeNextPath, setSessionCookies } from "@/lib/session";
import type { LoginState } from "./state";

/** One action for the whole sign-in form. The button that was pressed says what to do (the "intent" field). */
export async function loginAction(_previous: LoginState, formData: FormData): Promise<LoginState> {
  const intent = String(formData.get("intent") ?? "request");
  const phone = String(formData.get("phone") ?? "").trim();

  if (intent === "change") return { step: "phone", phone };

  if (intent === "request") {
    if (!phone) return { step: "phone", phone, error: "Enter your 10-digit mobile number." };
    const result = await requestOtp(phone);
    if (!result.ok) return { step: "phone", phone, error: result.message };
    return { step: "code", phone, notice: "We sent a 6-digit code by SMS. It is valid for 5 minutes." };
  }

  // intent === "verify"
  const code = String(formData.get("code") ?? "").replace(/\s/g, "");
  if (!/^\d{6}$/.test(code)) return { step: "code", phone, error: "Enter the 6-digit code." };
  const result = await verifyOtp(phone, code);
  if (!result.ok) return { step: "code", phone, error: result.message };

  await setSessionCookies(result.data);
  redirect(safeNextPath(String(formData.get("next") ?? "")));
}
