# Decision log

Status: **Approved** = owner approved. **Assumed** = default chosen by the architect; the owner was unsure and delegated.
Assumed decisions can be revisited, but changing them after data exists costs more.

| # | Decision | Status | Notes |
|---|---|---|---|
| D1 | Modular monolith; 3 Next.js apps + 1 API; browser → own Next.js server → API | Approved | |
| D2 | `packages/` added to the monorepo (shared config, generated API client) | Approved | One generated client, not three copies |
| D3 | A *listing* is one public presence of a business at one location; a business with many branches has many listings. Service areas add extra localities without a second URL | Assumed | Merges "business locations" into `listings` |
| D4 | Category slugs globally unique across parent and sub levels; one primary category per listing drives the canonical URL | Assumed | |
| D5 | Subscriptions are per listing | Assumed | Revisit before billing (Phase 6) |
| D6 | No ORM. Plain Npgsql with small hand-written mappers. Dapper only with explicit approval | Assumed | Dapper is commonly called a micro-ORM |
| D7 | ASP.NET Core security building blocks (password hashing, JWT bearer, policies, Data Protection) + own OTP/session tables; not full ASP.NET Core Identity | Assumed | Identity expects an EF-style store |
| D8 | DbUp for migrations; forward-only in production + rollback scripts where safe; CI blocks destructive SQL | Assumed | `scripts/check-migrations.mjs` |
| D9 | Free plan: 0 photos, 0 videos | Assumed | |
| D10 | Downgrade: over-limit media becomes hidden, never deleted; 30-day grace period | Assumed | |
| D11 | Reviews require phone-OTP-verified reviewers plus moderation | Assumed | |
| D12 | Video: MP4/H.264 only, size/duration caps, no transcoding at first | Assumed | |
| D13 | Edge protection (WAF) and private networking decided before production launch | Open | Cost and security trade-off; not needed locally or in early staging |
| D14 | Email provider: Resend. Maps provider: undecided (before Phase 2) | Assumed / Open | |
| D15 | Admin and super-admin domains: env configuration only | Approved | |
| D16 | Website is English only for now; no language routing in URLs | Approved | Telugu/Hindi would need a locale segment in every URL: decide before launch if ever wanted |
| D17 | Admin passwords hashed with PBKDF2-HMAC-SHA256 (210,000 iterations, per-password salt) from the .NET base library, stored as `pbkdf2-sha256$iterations$salt$hash` | Assumed | Follows D7 (no ASP.NET Core Identity store). The iteration count travels with each hash, so it can be raised later without invalidating existing passwords |
| D18 | Admin sign-in is password-only for now; the TOTP MFA the security notes require is not built yet | Assumed | The tables exist and are unused. The admin site must not be exposed publicly until MFA is added — see `docs/PROJECT_STATUS.md` |
| D19 | The latest review decision is stored on the listing (`review_note`, `reviewed_at`, `reviewed_by_admin_id`, migration V0004); the audit log keeps the full history | Assumed | The owner's screens need the current reason without reading audit rows |
| D20 | A listing an admin adds by hand goes live immediately only if that admin also holds `listings.review`; otherwise it joins the review queue | Assumed | Otherwise "add listings manually" would quietly include the power to publish |
| D21 | After a decision, the admin site asks the public site to refresh the affected pages (`POST /api/revalidate`, shared secret). Without the secret the public site falls back to its 60-second revalidation | Assumed | Keeps approvals near-instant without making every public page uncached |
