# Project status and handoff

**Updated:** 2026-10-16. Read this first, then `CLAUDE.md`. It replaces re-auditing the repository.

## Architecture as built

Three Next.js apps (`apps/main-website`, `apps/admin-website`, `apps/super-admin-website`), **one**
ASP.NET Core API (`backend/BusinessDirectory.Api`), one PostgreSQL + PostGIS database.
Browser → its own Next.js server → API → PostgreSQL. No ORM; raw parameterised SQL via Npgsql.

> **Open architecture question.** The master brief asks for *two* APIs — `BusinessListings.Api`
> (businesses, listings, branches, categories, localities, search) and `Platform.Api` (auth, users,
> subscriptions, payments, enquiries, reviews, admin, audit). The repository has one API, and
> decision D1 in `docs/decisions.md` records the single modular API as **approved**. Splitting it is
> a real migration with real cost. **It needs the owner's decision before anyone starts it** — see
> "Decisions needed" below. Nothing currently blocks feature work either way: the API is already
> organised by audience, so the split would mostly move folders.

## Verified on 2026-10-16

All run locally and observed, not assumed:

| Check | Result |
|---|---|
| `npm run verify` (migration check, script tests, typecheck, 3 production builds, .NET build + tests) | **passed** |
| .NET unit tests | **166 passed**, 0 failed |
| .NET integration tests | **54 passed**, 0 failed |
| `database/checks/search_smoke.sql` | **passed** (10 assertions, rolls back) |
| `database/checks/admin_moderation_smoke.sql` | **passed** (17 assertions, rolls back) |
| Search in a real browser against the seeded database | **works** — see below |
| Admin moderation in a real browser against the seeded database | **works** — signed in, rejected a listing with a reason, approved it, and watched it appear in public search |

The moderation walk-through was done against a temporary listing that was deleted afterwards, so the
dev data is unchanged. A local admin account `dev.admin@hyderabadlinks.local` (role `admin`) exists in
the local database only; reset its password with the `create-admin` command whenever you need it.

## What works today

**Public site (`apps/main-website`)**
- Home page: brand, search box, category browse. Real categories from the database.
- **Search (`/search`) — database-backed.** Matches business names and descriptions via PostgreSQL
  full-text search, tolerates misspellings via pg_trgm, and matches service words through the
  category `search_terms` array (so "tap repair" finds plumbers). Filters by category (a parent
  slug also matches its sub-categories), by locality (including listings that merely *serve* that
  area), and by proximity. Sorts by relevance, distance or name. Paginated, with the total of all
  matches. Every filter lives in the URL, so results are shareable. Empty, error and
  past-the-end-page states are all handled.
- Locality + category SEO pages (`/hyderabad/{locality}/{category}`): real listings within 8 km,
  closest first, paginated, with `ItemList` structured data and a canonical URL. Areas with no
  listings are marked `noindex` so the site does not publish thin pages.
- Listing detail pages with the correct record, structured data and canonical URLs.
- Owner screens: phone-OTP sign-in (`/login`), then `/account/listings` to create, edit and submit
  listings for review.

**API (`backend/BusinessDirectory.Api`)**
- `/health/live`, `/health/ready`, `/api/v1/public/meta`.
- Public: `/cities`, `/cities/{city}/localities`, `/categories`,
  `/listings/search`, `/listings/nearby`, `/listings/{city}/{locality}/{slug}`.
- Owner auth: OTP request/verify, refresh, logout. Short-lived JWT plus a rotating refresh token.
- Owner listings: list, create, read, update, submit for review — all ownership-checked server-side.
- Security baseline: security headers, deny-by-default CORS, per-IP rate limiting, stricter
  database-backed OTP limits, generic error responses, startup refusal of wildcard CORS outside
  Development.

**Admin site (`apps/admin-website`) — listing moderation, end to end**
- Staff sign-in at `/login` (email + password). No sign-up: accounts are created by the CLI bootstrap.
- `/listings`: the moderation queue, filtered by status, `pending_review` first and oldest-first,
  paginated, with the total of all matches.
- `/listings/{id}`: everything a reviewer needs (contact details, map point, who submitted it, the
  last decision) plus Approve and Reject with a reason the owner will read. Approving publishes the
  listing immediately; rejecting sends it back to the owner, who can edit and resubmit.
- The session lives in httpOnly cookies on the admin site's own origin, and `proxy.ts` swaps an
  expired access token for a fresh pair — the same scheme as the public site.

**API — admin surface**
- Admin auth: `POST /api/v1/auth/admin/login|refresh|logout`. PBKDF2-SHA256 passwords, lockout after
  5 wrong attempts for 15 minutes, one generic failure message, and a rotating refresh token whose
  reuse revokes the whole family.
- Moderation: `GET /api/v1/admin/me`, `GET /listings`, `GET /listings/{id}`,
  `POST /listings/{id}/approve`, `POST /listings/{id}/reject`. See `docs/api-specification.md`.
- Admin tokens carry the audience `admin` and are validated by their own authentication scheme, so an
  owner token never reaches an admin route (and vice versa). Permissions are read from the database on
  every request — removing a role or disabling an account takes effect on the next call, which was
  checked against the running API.
- Ordinary admin permissions are kept apart from super-admin ones in code
  (`Auth/AdminPermissions.cs`): a permission reserved for the super-admin surface is refused on
  `/api/v1/admin` even to someone who holds it.
- Every decision writes to `audit_logs` in the same transaction as the change.

**Database** — migrations `V0001`–`V0004`: cities, localities (PostGIS), categories, slug history,
users, businesses, business members, listings (PostGIS + full-text + trigram indexes), listing
categories and service areas, OTP challenges, user sessions, admin users and sessions, roles,
permissions, audit logs. Reference seeds hold real Hyderabad localities and categories; dev seeds
hold 30 clearly labelled sample listings.

## Not built yet

Ordered by what unblocks the most.

1. **Admin MFA and account management.** Admin sign-in is email + password today. `docs/security.md`
   requires mandatory TOTP MFA with recovery codes, and the tables for it (`admin_users.totp_*`,
   `admin_recovery_codes`, `admin_sessions.last_mfa_at`) are already there but unused. **The admin
   site must not be exposed outside the office network until this is built.** Inviting admins,
   assigning roles and changing passwords also have no screens yet; today an account is created or
   reset with the `create-admin` command.
2. **The rest of admin moderation.** Only approve and reject exist. Suspending a live listing,
   editing one on the owner's behalf, verifying a business, and handling abuse reports are not built,
   although the permissions for them are seeded.
3. **Super-admin website** — placeholder only. No `/api/v1/super-admin` endpoints.
4. **Media** — no tables, no upload endpoints, no Blob Storage code. Subscription media limits
   (Basic 4/0, Gold 4/2, Premium 16/4) are therefore not enforced anywhere.
5. **Subscriptions and payments** — no tables, no Razorpay integration, no entitlement checks.
6. **Enquiries and leads, reviews, favourites, notifications** — no tables, no endpoints.
7. **OpenAPI documentation** — the API publishes none. The brief requires it, and
   `packages/api-client` is an empty placeholder waiting for a generated client.
8. **Sitemap** — `robots.ts` exists in all three apps; there is no `sitemap.ts`.
9. **Branches** — decision D3 models a branch as a second listing. No branch-specific UI exists.
10. **Real OTP delivery** — see "Missing credentials".
11. **End-to-end tests** — `tests/e2e/` is empty; Playwright is not set up.
12. **Azure infrastructure as code** — `infrastructure/azure/` holds only a README.

## Known limitations in what *is* built

- **Full-text search does not stem.** `listings.search_vector` is built with the `simple`
  PostgreSQL text configuration, so "plumbers" does not match "plumber" through full-text search.
  Trigram matching and the category `search_terms` cover most of the gap in practice. Moving to the
  `english` configuration means a migration that drops and recreates the generated column, and the
  migration safety checker treats that as destructive — so it is a deliberate decision, not a
  drive-by change.
- **`/api/v1/public/listings/nearby` now has no frontend caller.** `listings/search` with
  `sort=distance` does everything it does. The endpoint and its tests remain because the brief wants
  the listings API reusable by other applications. Decide whether to keep it when the API surface is
  reviewed.
- **The search page's category `<select>` lists every category** (~170 options). It works and is
  accessible, but a searchable combobox would be better once there are more categories.
- **Page size on `/search` is fixed** at the API default of 20; `pageSize` in the URL is ignored by
  the page on purpose, so visitors cannot ask for huge pages.
- **Admin sign-in has no MFA yet.** See "Not built yet" item 1. Password strength is judged by length
  only (12 characters minimum), and nothing forces a password change.
- **An owner is not told when their listing is decided on.** The reason is on their listings screen
  the next time they look; there is no SMS or email, because no notification channel exists yet.
- **The rejection reason is free text.** There is no list of standard reasons, so wording is up to the
  reviewer.
- **Admin session lifetime** is 15 minutes for the access token and 7 days for the refresh token
  (`Auth:AdminAccessTokenMinutes`, `Auth:AdminRefreshTokenDays`). The lockout is 5 wrong passwords for
  15 minutes, counted per account, not per IP.

## Decisions needed from the owner

1. **One API or two?** See the architecture note above. Nothing is blocked while this is open.
2. **Free plan media allowance.** `docs/decisions.md` D9 records 0 photos / 0 videos as *assumed*.
   The brief says this is unconfirmed. It must be settled before media upload is built, and it
   stays configurable either way.
3. **Maps provider** (D14) — still open.
4. **Edge protection and private networking** (D13) — before production launch.

## Missing credentials and configuration

None of these are in the repository, and none should be.

| What | Configuration key | Status |
|---|---|---|
| OTP / SMS delivery | an `ISmsSender` implementation + its provider keys | **Not configured.** Development prints the code to the API console (`ConsoleSmsSender`). Outside Development the API uses `UnconfiguredSmsSender`, which refuses to pretend. MSG91 is the intended provider. |
| JWT signing key | `Auth:JwtSigningKey` | Development generates one. Production must supply it. |
| OTP HMAC key | `Auth:OtpHmacKey` | As above. |
| Database | `ConnectionStrings:Default` | Local value comes from `.env` / Docker Compose. |
| Email (Resend, D14) | not wired up | Not configured. |
| Razorpay | not wired up | Not configured. |
| Azure Blob Storage | not wired up | Azurite runs locally in Docker Compose, unused so far. |

## Suggested next step

**Admin MFA, then the rest of the admin surface.** The first business workflow is now complete: an
owner submits a listing, an admin approves it, and it is live. What is missing before anyone outside
the office uses the admin site is the MFA the security notes require (the tables are already there),
followed by inviting admins and assigning roles — today both are command-line only.

If moderation itself is the priority instead, the next pieces are suspending a live listing, editing
a listing on the owner's behalf, and telling the owner a decision was made (which needs a
notification channel, so it depends on OTP/email delivery being configured).
