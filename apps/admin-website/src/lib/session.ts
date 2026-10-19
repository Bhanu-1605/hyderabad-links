import { cookies } from "next/headers";
import {
  ACCESS_COOKIE,
  MFA_COOKIE,
  REFRESH_COOKIE,
  accessCookieOptions,
  mfaCookieOptions,
  refreshCookieOptions,
  type SessionTokens,
} from "./session-cookies";

// Server-side only (uses next/headers).

export async function setSessionCookies(tokens: SessionTokens): Promise<void> {
  const jar = await cookies();
  jar.set(ACCESS_COOKIE, tokens.accessToken, accessCookieOptions(tokens.expiresInSeconds));
  jar.set(REFRESH_COOKIE, tokens.refreshToken, refreshCookieOptions);
}

export async function clearSessionCookies(): Promise<void> {
  const jar = await cookies();
  jar.delete(ACCESS_COOKIE);
  jar.delete(REFRESH_COOKIE);
}

export async function readTokens(): Promise<{ access: string | null; refresh: string | null }> {
  const jar = await cookies();
  return { access: jar.get(ACCESS_COOKIE)?.value ?? null, refresh: jar.get(REFRESH_COOKIE)?.value ?? null };
}

/** Only allow sending an admin back to a page inside /listings after signing in (no open redirects). */
export function safeNextPath(value: string | null | undefined): string {
  const fallback = "/listings";
  if (!value || !value.startsWith("/listings") || value.startsWith("//") || value.includes("\\")) return fallback;
  return value;
}

export async function setMfaCookie(token: string, expiresInSeconds: number): Promise<void> {
  const jar = await cookies();
  jar.set(MFA_COOKIE, token, mfaCookieOptions(expiresInSeconds));
}

export async function readMfaToken(): Promise<string | null> {
  const jar = await cookies();
  return jar.get(MFA_COOKIE)?.value ?? null;
}

export async function clearMfaCookie(): Promise<void> {
  const jar = await cookies();
  jar.delete(MFA_COOKIE);
}
