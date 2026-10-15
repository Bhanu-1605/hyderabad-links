# Security design (summary; implemented progressively)

## Identity realms
Separate token audiences: `owner`, `admin`, `superadmin`. A token from one realm is never accepted by another.

## Business-owner OTP (Phase 3)
Normalise to E.164; verify bot-protection token; resend cooldown + per-phone and per-IP limits stored in PostgreSQL (works across
instances); 6-digit code from a cryptographic RNG; store only an HMAC hash (key in Key Vault); 5-minute expiry; max 5 attempts;
identical response whether or not the number has an account; constant-time comparison. SMS provider behind `ISmsSender`.
Dev-only test OTP requires Development environment AND an explicit flag; the app refuses to start if enabled elsewhere.

## Sessions
15-minute JWT access token + rotating opaque refresh token stored hashed, with reuse detection and revocation. httpOnly, Secure,
SameSite cookies on each Next.js app's own origin.

## Admin and super-admin
Invite-only. Email + password + mandatory TOTP MFA + recovery codes + lockout. Permissions loaded server-side per request so revocation
is immediate. Nobody can change their own role or permissions. The first super-admin is created by a one-time command-line
bootstrap, never by an HTTP endpoint. Sensitive actions require MFA re-entry. The last super-admin cannot be removed.

## Baseline already in place (Phase 0)
Security headers on all apps and the API; admin sites blocked from indexing; deny-by-default CORS; startup refusal of wildcard CORS
outside Development; per-IP rate limiting; generic error responses; `.gitignore` excludes env files and key material.

## Later
Content-Security-Policy (needs per-request nonces in Next.js), Key Vault + managed identity, least-privilege database roles,
upload scanning, payment webhook signature verification, DPDP Act consent/deletion flows (legal review), DLT SMS registration.
