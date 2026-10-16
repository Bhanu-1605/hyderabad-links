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
| .NET unit tests | **97 passed**, 0 failed |
| .NET integration tests | **31 passed**, 0 failed |
| `database/checks/search_smoke.sql` | **passed** (10 assertions, rolls back) |
| Search in a real browser against the seeded database | **works** — see below |

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

**Database** — migrations `V0001`–`V0003`: cities, localities (PostGIS), categories, slug history,
users, businesses, business members, listings (PostGIS + full-text + trigram indexes), listing
categories and service areas, OTP challenges, user sessions, admin users and sessions, roles,
permissions, audit logs. Reference seeds hold real Hyderabad localities and categories; dev seeds
hold 30 clearly labelled sample listings.

## Not built yet

Ordered by what unblocks the most.

1. **Admin website** — placeholder only. No `/api/v1/admin` endpoints exist. Listing moderation
   (approve/reject) is the gap that matters most: owners can submit listings but nobody can approve
   them, so `pending_review` is a dead end today.
2. **Super-admin website** — placeholder only. No `/api/v1/super-admin` endpoints.
3. **Media** — no tables, no upload endpoints, no Blob Storage code. Subscription media limits
   (Basic 4/0, Gold 4/2, Premium 16/4) are therefore not enforced anywhere.
4. **Subscriptions and payments** — no tables, no Razorpay integration, no entitlement checks.
5. **Enquiries and leads, reviews, favourites, notifications** — no tables, no endpoints.
6. **OpenAPI documentation** — the API publishes none. The brief requires it, and
   `packages/api-client` is an empty placeholder waiting for a generated client.
7. **Sitemap** — `robots.ts` exists in all three apps; there is no `sitemap.ts`.
8. **Branches** — decision D3 models a branch as a second listing. No branch-specific UI exists.
9. **Real OTP delivery** — see "Missing credentials".
10. **End-to-end tests** — `tests/e2e/` is empty; Playwright is not set up.
11. **Azure infrastructure as code** — `infrastructure/azure/` holds only a README.

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

**Admin listing moderation, end to end.** It is the only thing standing between an owner submitting
a listing and that listing going live, so it completes the first real business workflow.

Roughly: admin authentication against the existing `admin_users` / `admin_sessions` /
`roles` / `permissions` tables → `/api/v1/admin/listings` with a pending queue, approve and reject,
each action writing to `audit_logs` → the matching screens in `apps/admin-website`. Enforce every
permission in the API, and keep ordinary admin permissions separate from super-admin ones.

Add validation-level integration tests (no database, as CI has none) plus a
`database/checks/admin_moderation_smoke.sql` for the SQL behaviour.
