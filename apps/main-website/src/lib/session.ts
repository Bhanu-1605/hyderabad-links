import { cookies } from "next/headers";
import { ACCESS_COOKIE, REFRESH_COOKIE, accessCookieOptions, refreshCookieOptions, type SessionTokens } from "./session-cookies";

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

/** Only allow sending the owner back to a page inside /account after login (no open redirects). */
export function safeNextPath(value: string | null | undefined): string {
  const fallback = "/account/listings";
  if (!value || !value.startsWith("/account") || value.startsWith("//") || value.includes("\\")) return fallback;
  return value;
}
