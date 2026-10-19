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
Invite-only. Email + password + **mandatory TOTP MFA** + recovery codes + lockout. Permissions loaded server-side per request so
revocation is immediate. Nobody can change their own role or permissions. The first super-admin is created by a one-time
command-line bootstrap, never by an HTTP endpoint. The last super-admin cannot be removed.

### How MFA is enforced

A correct password does **not** produce a session. It returns a short-lived token on a *different audience*
(`admin-mfa`, or `admin-enroll` when no authenticator is set up yet), and the admin bearer scheme validates the
audience, so that token fails on every real admin route. The `admin_sessions` row, the access token and the refresh
token are created only by `POST /api/v1/auth/admin/mfa/verify` or `/mfa/enroll/confirm`, which stamp
`admin_sessions.last_mfa_at`. A password-only caller therefore cannot reach a protected function by construction,
not by a check that could be forgotten. Refresh carries `last_mfa_at` forward and refuses a session that lacks it.

- **TOTP** is RFC 6238, HMAC-SHA1, 6 digits, 30-second steps, hand-written (no package) to match the hand-written
  password hashing of D17. Codes one step either side of now are accepted for clock drift.
- **Replay** is refused using `admin_users.totp_last_step` (migration V0005), which records the step the accepted
  code belonged to. The step cannot be inferred from a timestamp: a code accepted from the *next* step belongs to a
  later step than "now", so a clock-derived value is too low to refuse that same code again. That was a real defect
  before the column existed.
- **The TOTP secret** is encrypted with AES-256-GCM under `Auth:TotpEncryptionKey`, which lives outside the database,
  so a database-only leak does not defeat MFA. Hashing is impossible here because the server must reproduce the code.
  A tampered row fails to open rather than decrypting to rubbish.
- **Recovery codes**: ten, generated at enrollment, shown once, stored only as SHA-256 hashes. Spending one is a
  single `UPDATE` that matches only an unused row, so two requests racing with the same code cannot both succeed.
- **Rate limiting**: wrong codes count against the same `failed_login_count` and `locked_until` allowance as wrong
  passwords, so guessing codes locks the account exactly as guessing passwords does.
- **Enumeration**: one message for an unknown address, a wrong password, a locked account and a disabled one; and one
  message for a wrong code, a replayed code and a spent recovery code. The enrollment stage is never revealed before
  the password is correct.
- Secrets, codes and tokens never appear in audit `details` or logs. Audit actions recorded:
  `admin.password_verified`, `admin.mfa_enrolled`, `admin.mfa_enroll_failed`, `admin.mfa_failed`,
  `admin.signed_in` (with the method), `admin.sign_in_failed`.

**Not built yet:** MFA re-entry before individually sensitive actions. MFA is required to obtain a session at all,
but a live session is not challenged again. Changing or resetting an authenticator also has no endpoint — an admin
who loses both their authenticator and their recovery codes needs a database intervention.

## Baseline already in place (Phase 0)
Security headers on all apps and the API; admin sites blocked from indexing; deny-by-default CORS; startup refusal of wildcard CORS
outside Development; per-IP rate limiting; generic error responses; `.gitignore` excludes env files and key material.

## Later
Content-Security-Policy (needs per-request nonces in Next.js), Key Vault + managed identity, least-privilege database roles,
upload scanning, payment webhook signature verification, DPDP Act consent/deletion flows (legal review), DLT SMS registration.
