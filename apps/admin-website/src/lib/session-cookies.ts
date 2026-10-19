// Cookie names and options for the admin session. No server-only imports here, so proxy.ts can use it too.
// Admin tokens live only in httpOnly cookies on this site's own origin: browser scripts can never read them.
// The names differ from the public site's (hl_access / hl_refresh) so the two sessions cannot be confused
// even when both sites are open on localhost during development.

export const ACCESS_COOKIE = "hl_admin_access";
export const REFRESH_COOKIE = "hl_admin_refresh";

const base = {
  httpOnly: true,
  sameSite: "lax" as const,
  secure: process.env.NODE_ENV === "production",
  path: "/",
};

/** The access cookie expires slightly before the token does, so we refresh instead of sending a dead token. */
export function accessCookieOptions(expiresInSeconds: number) {
  return { ...base, maxAge: Math.max(expiresInSeconds - 30, 60) };
}

/** Matches the API's default admin refresh-token lifetime (7 days). The API remains the authority on expiry. */
export const refreshCookieOptions = { ...base, maxAge: 60 * 60 * 24 * 7 };

export type SessionTokens = { accessToken: string; refreshToken: string; expiresInSeconds: number };

/**
 * The half-finished sign-in: the password was accepted, the authenticator code is still to come.
 * It is a credential of its own, so it lives in an httpOnly cookie like the others rather than in
 * form state where a browser script could read it. Short-lived on purpose; the API's own token
 * expires at the same time and is the real authority.
 */
export const MFA_COOKIE = "hl_admin_mfa";

export function mfaCookieOptions(expiresInSeconds: number) {
  return { ...base, maxAge: Math.max(expiresInSeconds, 60) };
}
