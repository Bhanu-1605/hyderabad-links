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
