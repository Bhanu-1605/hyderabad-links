"use server";

import { redirect } from "next/navigation";
import { adminLogin } from "@/lib/admin-api";
import { safeNextPath, setSessionCookies } from "@/lib/session";
import type { LoginState } from "./state";

/** Signs an admin in. The API answers with one message for every kind of failure, and so do we. */
export async function loginAction(_previous: LoginState, formData: FormData): Promise<LoginState> {
  const email = String(formData.get("email") ?? "").trim();
  const password = String(formData.get("password") ?? "");

  if (!email || !password) {
    return { email, error: "Enter your email address and password." };
  }

  const result = await adminLogin(email, password);
  if (!result.ok) {
    return { email, error: result.message };
  }

  await setSessionCookies(result.data);
  redirect(safeNextPath(String(formData.get("next") ?? "")));
}
