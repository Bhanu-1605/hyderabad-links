// Cookie names and options for the super-admin session. No server-only imports here, so proxy.ts can
// use it too. The names differ from the admin site's (hl_admin_*) and the public site's (hl_*) so the
// three sessions cannot be confused even with all of them open on localhost during development.
//
// The access cookie holds the **super-admin audience** token, which is the only thing
// /api/v1/super-admin accepts. The refresh cookie holds the ordinary admin refresh token, because one
// sign-in produces one session: refreshing it returns a new pair of access tokens, and the
// super-admin one only while the account still holds admins.manage.

export const ACCESS_COOKIE = "hl_super_access";
export const REFRESH_COOKIE = "hl_super_refresh";

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
 * It is a credential of its own, so it lives in an httpOnly cookie rather than in form state.
 */
export const MFA_COOKIE = "hl_super_mfa";

export function mfaCookieOptions(expiresInSeconds: number) {
  return { ...base, maxAge: Math.max(expiresInSeconds, 60) };
}
