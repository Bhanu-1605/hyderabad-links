# Project status and handoff

**Updated:** 2026-10-17. Read this first, then `CLAUDE.md`. It replaces re-auditing the repository.

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

## Verified on 2026-10-20

All run locally and observed, not assumed:

| Check | Result |
|---|---|
| `npm run verify` (migration check, script tests, typecheck, 3 production builds, .NET build + tests) | **passed** |
| .NET unit tests | **333 passed**, 0 failed |
| .NET integration tests | **94 passed**, 0 failed |
| `database/checks/review_smoke.sql` | **passed** (9 assertions: pending on arrival, one per person, every CHECK constraint, average over approved rows only, a decided review cannot be re-decided, cascade) |
| Reviews against the running API | **works** — anonymous refused; validation; **an owner cannot review their own business (403)**; a duplicate is refused by the constraint (409); nothing public until approved (`average: null`, not 0); the admin queue, approve, and a second decision answering 404; an audit row written |
| Reviews on the public page | **works** — average 4.0, the reviewer shown by first name only, and a sign-in prompt for an anonymous visitor |
| `database/checks/enquiry_smoke.sql` | **passed** (isolation, the ownership-scoped UPDATE, all four CHECK constraints, cascade on listing delete) |
| Enquiries against the running API | **works** — validation and normalisation, 404 for a listing that is not live, two real owners proving one never sees the other's enquiries (and a cross-owner status change answering 404, not 403), status filters, and the per-IP-per-listing daily cap |
| Enquiries in a real browser | **works** — sent from a listing page with the form replaced by a confirmation; the rate-limit message rendered when the cap was hit; the owner's inbox showed it, and "Mark replied" persisted and cleared the unread badge |
| `database/checks/admin_mfa_smoke.sql` | **passed** (7 assertions, rolls back) |
| Admin MFA against the running API | **works** — a password alone returns only a stage token and **401s on `/api/v1/admin/listings`**; enrollment, TOTP verification, the drift window, replay refusal, recovery-code single-use and lockout after five wrong codes were each exercised |
| Admin MFA in a real browser | **works** — first sign-in walked password → authenticator setup → recovery codes shown once → moderation queue; signing out and back in asked for a code and returned to the queue, with moderation unaffected |
| TOTP agreement with an independent implementation | **passes** — the RFC 6238 published test vectors in unit tests, and a separately written Node generator produced codes the API accepted |
| Owner dashboard in a real browser | **works** — signed in by OTP, every sidebar item reaches a working page with the correct active state, Overview's figures and 3-of-6 checklist match the API exactly, and editing the name saved and was reflected everywhere |
| `PUT /api/v1/me/profile` against the running API | **works** — 401 anonymous, 400 with field errors on bad input, 200 on a valid change, and an `owner.profile_updated` audit row written |
| Owner routes refuse anonymous callers | **passes** — `/account`, `/account/listings`, `/account/listings/new`, `/account/profile` and `/account/settings` all answer 307 to `/login?next=…`; `/api/v1/me`, `/me/overview` and `/me/listings` all answer 401 |
| Dashboard responsive check | **passes** — no horizontal overflow at 320, 375, 768, 1024 or 1440; sidebar from 1024, drawer below it with all six items; **no console errors** |
| Category dropdown interactions in a real browser | **works** — hover opens; clicking a hover-opened panel keeps it open (it pins) instead of closing; a second click closes; the pointer travels trigger → panel without closing; leaving closes an unpinned panel; Escape closes and returns focus; Tab from a trigger lands on the first link inside its panel |
| Chevron rotation and panel animation | **works** — `rotate: 180deg` when open over 150ms, panel animates in over 140ms, and the `prefers-reduced-motion` rule is present in the stylesheet |
| Nav fits its own box, measured at four widths | **passes** — `nav.scrollWidth <= nav.clientWidth` and no collision with the sign-in button at 1024, 1280, 1536 and 1850; page overflow is −15px (scrollbar slack) throughout, and 0 at 375 |
| Popular-category icons | **render** — seven inline SVG icons on the homepage, no new dependency, no horizontal overflow at 375px |
| Mobile category drawer | **works** — tap opens the drawer, tap expands a category, `aria-controls` resolves, no horizontal overflow at 375px |
| `database/checks/owner_overview_smoke.sql` | **passed** (5 assertions, rolls back) — includes that one owner's dashboard figures never count another owner's listings |
| `GET /api/v1/me/overview` against the running API | **works** — 401 anonymous; a verified-but-unregistered owner sees all zeros and 0/6 steps; immediately after registering, 1 draft and 3/6 steps, matching the unit tests |
| `npm run build -w apps/main-website` with the API stopped | **passes** — this is what CI does, and it was **failing** until the fix noted under "Known limitations" |
| `database/checks/search_smoke.sql` | **passed** (10 assertions, rolls back) |
| `database/checks/locality_search_smoke.sql` | **passed** (9 assertions, rolls back) |
| Locality lookup against the running API | **works** — `gachi`→Gachibowli, `patan`→Patancheru (a misspelling reached through trigram matching), `kphb`→KPHB Colony; a typed `%` matched nothing instead of everything; one letter and a point outside the world were both refused with 400; `nearest` resolved HITEC City's own coordinates to HITEC City |
| Location autocomplete in a real browser | **works** — typing six characters sent exactly **one** request (debounced), arrow keys and Enter chose a suggestion, Escape closed the list, and `aria-activedescendant` tracked the highlighted option. Unconfirmed typed text submitted `locality=` **empty**, so it never became a filter; a chosen suggestion submitted its slug and came back named in the field. With geolocation stubbed to a point 53 km out, "use my current location" named Medchal and said how far away it was. Every failure state was exercised and handled in place, with no console errors: permission denied, geolocation missing from the browser, the naming lookup returning 502, the suggestion lookup failing (the list says so), no match at all, and a **stale answer** — a deliberately slow first response arriving 1.5 s after a newer one did not replace it. Reloading a full `/search` URL restored the same filters, the same area name from its slug, and the same result count |
| `database/checks/admin_moderation_smoke.sql` | **passed** (21 assertions, rolls back) |
| `database/checks/onboarding_smoke.sql` | **passed** (8 assertions, rolls back) |
| Owner registration against the running API | **works** — a new number verified an OTP, registered, and got one account with one draft listing built from the chosen locality's centre point and the verified phone. Repeating the request returned the same listing and did not overwrite the profile; **five simultaneous requests produced one 201 and four 200s, and exactly one business and one listing** |
| Owner registration in a real browser | **works** — a new number was sent to `/onboarding` after verifying, registered, and landed on the dashboard with its draft listing. A registered owner signing in again went straight to the dashboard, and `/onboarding` redirects away once registration is done |
| Search in a real browser against the seeded database | **works** — see below |
| The whole workflow in a real browser against the seeded database | **works** — owner signed in by OTP and submitted a listing, it appeared in the pending queue, an admin approved it and the public page went from "not found" to live within seconds; a second listing was rejected with a reason, stayed unpublished, and the owner saw the reason on their listings screen |
| Admin-added and admin-edited listings against the running API | **works** — a hand-added listing is stored as `source = admin` with no owner and goes live at once; editing changed the details and left the status alone; both wrote audit rows |
| Permission separation against the running API | **works** — an admin holding only `listings.view` + `listings.create` got `403` on approve, reject and edit, and their new listing went to the review queue instead of going live |
| Redesigned home page in a real browser | **works** — every section renders from real API data (17 category parents, their real sub-categories, published listings). The mega-menu opens on click, lists a parent's real sub-categories and navigates: "Dentists" landed on `/search?category=dentists` with results. Keyboard path checked: Enter opened the panel, Tab moved to "All Home Services" then "Plumbers", Escape closed it and returned focus to the button. The ☰ menu at 375 px listed all 17 parents and expanded to the real links. The hero search box still submits (`dentists` → `/search?q=dentists&category=dentists`) and the locality combobox still suggests (`gachi` → Gachibowli). No console errors on a clean load |
| Redesigned search results in a real browser | **works** — all against the seeded database. Filters combine: `q=sample` + `category=plumbers` + `locality=kukatpally` + `sort=name` returned **1 business**, and every control still showed its value after a refresh, including the area name resolved back from its slug. Removing a filter chip went from `/search?category=dentists&q=sample` to `/search?category=dentists` and the count changed with it; **Back** and **Forward** both restored the right results. Paging: 31 listings over 2 pages, page 2 held the remaining 11. Empty search, past-the-end page (`page=99`) and **a real API failure** (the API was stopped, then restarted) each showed their own panel, the failure one as `role="alert"` with the filters still usable. The locality field starts on "Choose a location", `kukat` suggested Kukatpally, choosing it submitted `locality=kukatpally` while **typed-but-unchosen text submitted `locality=` empty**, and the clear button emptied both the field and the hidden slug. No console errors |
| Redesigned login and onboarding in a real browser | **works** — the whole OTP journey was walked twice against the running API. A number that had never signed in requested a code, the code step showed the number masked (`90****0123`), the code verified and the new owner landed on `/onboarding`; the form listed the **103 real localities and 134 real sub-categories** from the API, and submitting it created the account and went to `/account/listings?welcome=1` with the draft listing shown. Visiting `/onboarding` again redirected to the dashboard, and **signing out and back in with the same number went straight to the dashboard**, not to onboarding. Both failure paths were exercised: a 3-digit number was refused with "Enter a valid 10-digit Indian mobile number", and a wrong code with "The code is invalid or has expired", each staying on the right step. No console errors |
| Redesigned login and onboarding at four widths | **works** — no horizontal overflow at 320, 375, 768 or 1440 px; the marketing panel is hidden below 1024 px so the form has the screen to itself |
| Redesigned listing detail in a real browser | **works** — a real listing rendered with its breadcrumb, monogram, description, actions and facts panel. Every crumb navigates: the area crumb went to `/search?locality=beeramguda`, the category crumb and the "More …" link both went to `/hyderabad/madhapur/plumbers`. The **D4 redirect still holds**: `/hyderabad/madhapur/electricians/ravi-kumar-plumbing` landed on `/hyderabad/madhapur/plumbers/ravi-kumar-plumbing`, and an unknown slug gave the "Not found" page. The canonical link and the `LocalBusiness` structured data are both still in the markup. A listing with no WhatsApp number and no website showed four facts instead of six, with nothing empty. No console errors; no overflow at 320, 375 or 1440 px |
| The optional WhatsApp and website fields, end to end | **works** — after one dev sample listing was given both (`D001_sample_listings.sql`), `Sample Brew and Bites Cafe Madhapur` showed **six** facts instead of four, and its WhatsApp link came out as `https://wa.me/919999010025` (the `+` and spacing stripped) with the website carrying `rel="noopener noreferrer nofollow"`. The same branch was checked in the other two places that render it: the `/search` result card and the locality + category page card, where **only that one listing of the two shows a WhatsApp button**, so the condition is genuinely conditional |
| Redesigned owner screens in a real browser | **works** — the whole owner journey was walked on the redesigned screens against the running API. A second new number registered through `/onboarding`, landed on the dashboard, opened the listing, saved an address, pincode and description ("Changes saved."), then submitted for review: the page returned to `/account/listings?submitted=1`, the badge changed to **In review** and the counts moved from `Drafts 1` to `In review 1`. The counts are derived from the listings the API returns, so they follow the real data. Validation still reports properly through the shared `Field`: a 2-digit pincode produced "Enter a 6-digit pincode." with `aria-invalid="true"` and `aria-describedby="pincode-error"`, under the banner "Please fix the fields marked below." The area is shown locked on the edit form and selectable only when adding. No console errors; no overflow at 320, 375 or 1440 px |
| OTP rate limiting, hit by accident | **works** — after five code requests for one number within the hour the API refused with "Too many code requests. Please wait a little and try again." (`MaxOtpPerPhonePerHour = 5`). The per-IP allowance is 20/hour, so testing continued on a second number |
| Redesigned search results at five widths | **works** — no horizontal overflow at 320, 375, 768 or 1440 px; the results grid is one column on a phone, two at 768 px and three at 1440 px |
| Home page layout at seven widths | **works** — no horizontal overflow at 320, 375, 768, 1024, 1280, 1366, 1440 or 1920 px. The category bar appears at 1280 px and above; below that the same tree is behind the ☰ toggle |

The moderation walk-through was done against a temporary listing that was deleted afterwards, so the
dev data is unchanged. A local admin account `dev.admin@hyderabadlinks.local` (role `admin`) exists in
the local database only; reset its password with the `create-admin` command whenever you need it.

## What works today

**Public site (`apps/main-website`)**
- **Design system (redesign pass 1).** The palette, type and spacing now come from tokens in
  `src/app/globals.css` (`@theme`), taken from the owner's reference build: teal `brand-700`, a cyan
  `brand-400` accent, `ink` / `muted` / `line` neutrals and `night` for the dark band. The four token
  names the pre-redesign components already used (`brand-50/100/700/900`) were kept, so the search
  filters, listing cards and owner screens picked up the new palette without being edited.
  Reusable pieces live in `src/components/ui/`: `Button`/`ButtonLink` (primary, ghost, cta),
  `SectionHeading` (light and dark), `CategoryCard` and `BusinessCard` (the circular monogram card
  with the name ribbon). `SiteChrome.tsx` holds the teal utility bar, the main header and the
  footer; `CategoryNav.tsx` holds the mega-menu. All of it is page-agnostic, so the search results
  page can adopt it next.
- **Home page, redesigned to the reference.** Announcement strip, hero with the search box and
  popular-category shortcuts, "Most Searched Businesses & Services" built from the real category
  tree, a dark band of circular business cards built from real published listings, and the
  business-owner call to action. No mock data and no hard-coded categories anywhere on it.
- **Category mega-menu (`src/components/CategoryNav.tsx`).** The header navigation is the real
  two-level tree from the API. Five parents sit on the bar and the remaining twelve are behind
  "More"; each opens a panel of that parent's real sub-categories plus an "All <parent>" link, and
  every entry navigates to `/search?category={slug}`. The triggers are disclosure buttons rather
  than hover targets, so the panels are reachable by keyboard and usable on touch: Enter or Space
  opens, Tab walks into the panel, Escape closes and returns focus, and a click outside closes.
  Below 1280 px the same tree stacks behind the ☰ toggle. The tree is fetched on the server in
  `SiteHeader` and passed in, so the browser still never calls the API.
- **Search (`/search`) — database-backed.** Matches business names and descriptions via PostgreSQL
  full-text search, tolerates misspellings via pg_trgm, and matches service words through the
  category `search_terms` array (so "tap repair" finds plumbers). Filters by category (a parent
  slug also matches its sub-categories), by locality (including listings that merely *serve* that
  area), and by proximity. Sorts by relevance, distance or name. Paginated, with the total of all
  matches. Every filter lives in the URL, so results are shareable. Empty, error and
  past-the-end-page states are all handled.
- **Search results, redesigned to the reference (pass 2).** A page heading that names what was
  searched and where, the filter panel, then the results as `ListingCard`s in a one / two / three
  column grid. New on this pass: **active filter chips**, one per filter in force (keyword,
  category, area, "near my location", sort), each an ordinary link back to the same search minus
  that one parameter — so removing a filter needs no JavaScript, keeps the other filters, returns
  to page 1 and leaves a normal history entry. Removing "near my location" also drops the distance
  sort, which cannot outlive the coordinates it sorts by. The empty, error and past-the-end states
  are now the shared `EmptyState` panel, and the empty one offers "List your business".
  `ListingCard` was restyled rather than replaced, so the locality + category SEO pages picked up
  the same card. `src/app/search/loading.tsx` shows a skeleton of the real layout while the server
  fetches, announced to screen readers as "Searching for businesses…".
- **Location autocomplete (`src/components/LocalityCombobox.tsx`).** The home page and the search
  filters share one accessible combobox: type part of an area name and choose from suggestions
  (debounced ~250ms, with stale answers dropped so a slow earlier request cannot overwrite a newer
  one), or press "use my current location" to have the coordinates named. Full keyboard support and
  loading, empty and error states. **Only a chosen suggestion becomes a filter, and only as its
  slug** — text that was merely typed is never sent as a locality, so results never quietly come
  from an area the visitor did not pick. Geolocation is asked for on click and never on page load.
  It goes through two route handlers under `src/app/api/localities/`, because this lookup happens
  while the visitor types and so cannot be done during server rendering; the browser still never
  touches the API directly.
- Locality + category SEO pages (`/hyderabad/{locality}/{category}`): real listings within 8 km,
  closest first, paginated, with `ItemList` structured data and a canonical URL. Areas with no
  listings are marked `noindex` so the site does not publish thin pages.
- Listing detail pages with the correct record, structured data and canonical URLs.
- **Listing detail, redesigned to the reference (pass 5) — with today's fields only.** A breadcrumb
  that follows the URL's own segments (Home → area → category → this business, every crumb a page
  that exists), then the business card: monogram, name, category and area, description, the Call /
  WhatsApp / Directions actions, and a facts panel. The facts are built by `factsFor()`, which adds
  a row only when the API returned something for it, so a listing without a WhatsApp number or a
  website simply has fewer rows rather than empty ones. The reference's rating, opening hours,
  services list, languages spoken, contact person, social links and logo are **left out**, because
  none of them have tables — `factsFor()` is the one place to extend when they do. The owner's
  disclaimer from the reference is kept. The D4 canonical-category redirect, the canonical URL and
  the `LocalBusiness` structured data are unchanged.
- Owner screens: phone-OTP sign-in (`/login`), then `/account/listings` to create, edit and submit
  listings for review.
- **Owner screens, redesigned to the reference (pass 4).** `/account/listings` now opens with four
  counts — Live, In review, Drafts, Needs changes — worked out from the listings the API returns,
  then the listings themselves as cards with their status badge, the reviewer's reason when there
  is one, and the actions. The prototype's dashboard also shows Active Blogs, Inactive Blogs and
  Total Views; those are **not** copied, because nothing in this database can ever make them
  non-zero. The add and edit pages share one page header, and `ListingForm` now uses the shared
  `Field`, so the owner form, the onboarding form and the search filters label and describe their
  controls the same way. **No owner logic changed**: the server actions, ownership checks and the
  draft → review → published flow are untouched.
- **Owner registration (D24).** A phone that has never signed in before is sent to `/onboarding`,
  which collects name, business name, email, locality and primary category, then creates the
  profile, business, ownership row and first draft listing in one transaction
  (`POST /api/v1/me/onboarding`). It is idempotent: the user row is locked for the transaction,
  the profile update is guarded by `name IS NULL`, and a repeat returns the existing listing
  rather than making a second one. `GET /api/v1/me` reports `needsOnboarding`, so the rule lives
  in the API rather than in each website. A rejected listing shows the reviewer's reason, so the owner knows what to
  change; submitting again clears it.
- **Login and onboarding, redesigned to the reference (pass 3).** Both now use the shared
  `AuthLayout`: the form on the left, a teal panel on the right, from the prototype's
  `_AuthLayout.cshtml`. The sign-in card keeps its two steps — number, then code — with the number
  masked on the code step (`90****0123`) and the code box centred and letter-spaced as in the
  prototype. The onboarding form is the same five fields, now built from the shared `Field`
  component so the label, help text and error are wired to the control consistently. **No
  authentication logic changed**: the server actions, the OTP flow, the session cookies and
  `needsOnboarding` are untouched; this pass is presentation only.
- `/api/revalidate`: lets the admin site refresh a public page the moment a listing is approved,
  instead of waiting out the 60-second cache window. Off unless `REVALIDATE_TOKEN` is configured.

**API (`backend/BusinessDirectory.Api`)**
- `/health/live`, `/health/ready`, `/api/v1/public/meta`.
- Public: `/cities`, `/cities/{city}/localities`, `/categories`,
  `/listings/search`, `/listings/nearby`, `/listings/{city}/{locality}/{slug}`.
- Public locality lookup, for the autocomplete: `/localities/search?q=&city=&limit=` ranks names
  starting with what was typed above names containing it, and trigram similarity below both so a
  misspelling still lands; `/localities/nearest?lat=&lng=` resolves coordinates to the closest
  known locality and says how many metres away it is. Both use indexes the schema already had
  (`localities_name_trgm_idx`, `localities_center_gix`), so neither needed a migration — and
  nearest stands in for a paid reverse geocoder while D14 is open.
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
- `/listings/new` and `/listings/{id}/edit`: adding a business that phoned in, and correcting a
  listing on an owner's behalf. Both use the same form as the owner's screens, and both only appear
  for an admin who holds the matching permission — which the API checks again regardless.
- The session lives in httpOnly cookies on the admin site's own origin, and `proxy.ts` swaps an
  expired access token for a fresh pair — the same scheme as the public site.

**API — admin surface**
- Admin auth: `POST /api/v1/auth/admin/login|refresh|logout`. PBKDF2-SHA256 passwords, lockout after
  5 wrong attempts for 15 minutes, one generic failure message, and a rotating refresh token whose
  reuse revokes the whole family.
- Moderation: `GET /api/v1/admin/me`, `GET /listings`, `POST /listings`, `GET /listings/{id}`,
  `PUT /listings/{id}`, `POST /listings/{id}/approve`, `POST /listings/{id}/reject`.
  See `docs/api-specification.md`.
- Adding and editing are separate permissions from approving (`listings.create`, `listings.edit`,
  `listings.review`). A hand-added listing goes live at once only when that admin may also approve
  listings; otherwise it joins the queue, so adding is never a way around reviewing. Editing never
  changes a listing's status.
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
hold 30 clearly labelled sample listings. One of them, `sample-brew-and-bites-cafe-madhapur`, also
carries a WhatsApp number and a website (`https://example.com/...`, a reserved documentation
domain), added on 2026-10-18 so the optional WhatsApp and website branches on the detail page and
the result cards can actually be seen while developing. It is a re-runnable `UPDATE` at the end of
`D001_sample_listings.sql`, so `npm run db:seed-dev` stays safe to repeat.

### Business owner dashboard

`GET /api/v1/me/overview` returns the owner's listing counts by state (total, active, pending
review, draft, rejected, suspended; archived excluded) and a six-step profile-completion checklist.
Every figure is counted from that owner's own rows through `business_members`, so one owner's
dashboard can never show another's.

**Listing views and leads are deliberately absent** — there are no tables for either, and the brief
forbids inventing figures. They appear when those tables do.

The dashboard *shell* (sidebar, top bar, account menu, breadcrumbs) is **not built yet**: it waits on
the reference UI redesign so it can use the real design system rather than being restyled twice.
Agreed sidebar for the first version: Overview, My Listings, Add New Listing, Business Profile,
Account Settings, Logout. Leads & Enquiries and Subscription & Plans are **left out until their
tables exist**, so that every item leads to a working page.

There is no separate Business Owner application and none is needed. The owner's live site serves it
from a path (`/BO/Dashboard`) on one domain; this repository does the same at `/account/*` inside
`apps/main-website`.

### Header category navigation

Seven real top-level categories sit on the bar, with an **All categories** panel listing all
seventeen. Nothing is hardcoded and the reference's wording ("Food", "Stays") was deliberately not
adopted — see D22's sibling decision below.

- **Hover and click coexist.** A panel opened by hover is *unpinned* and closes when the pointer
  leaves; clicking *pins* it so it stays. Without that distinction a click on an already-hovered
  trigger reads as "toggle" and shuts the panel the hover just opened, which makes clicking look
  broken — that bug was hit once already and the `pinned` flag is what prevents it.
- **The pointer can travel from trigger into panel** because each panel is a DOM child of the same
  wrapper as its button, so `onMouseLeave` does not fire on the way. A 120ms close delay absorbs
  the hairline gap. **That DOM order is load-bearing** for the keyboard too: Tab from a trigger
  lands on the first link inside its own panel.
- SVG chevron rotating 180° in 150ms; panel fades and rises 6px over 140ms; both disabled under
  `prefers-reduced-motion`, which now also turns off the page's smooth scrolling.
- `aria-expanded` and `aria-controls` on every trigger, desktop and mobile.

**The nav now appears from 1024px**, not 1280px. It was previously hidden entirely on 1024–1279px
laptops, which is a very common width. Narrower screens show fewer shortcuts — four up to 1535, six from 1536, seven past about 1800 — and
"All categories" always lists every parent, so nothing is ever unreachable.

**The measurement that matters here is not page overflow.** The nav is a flex item between the brand
and the sign-in button, and its content can overrun that box while `document.scrollWidth` stays
clean, which shows up as the nav colliding with the sign-in button rather than as a scrollbar.
Checking page overflow alone passed at every width while the nav was in fact 59px too wide at 1024
and colliding at 1280 and 1536. The real test is `nav.scrollWidth > nav.clientWidth`, plus comparing
the last trigger's right edge with the sign-in button's left edge. Re-run that at 1024, 1280, 1536
and 1850 after touching the slot counts, the type scale or the gaps.

### Business owner dashboard

The owner area at `/account/*` now has a real shell: a sidebar from 1024px, a drawer with the same
six items below that, active-navigation states, and a bar showing who is signed in. It stays inside
`apps/main-website` — there is no separate Business Owner application and none is needed, exactly as
the owner's own live site serves theirs from `/BO/` on one domain.

Sidebar: Overview, My Listings, Add New Listing, Business Profile, Account Settings, Logout. **Leads
& Enquiries and Subscription & Plans are deliberately absent** until their tables exist, so every
item reaches a working page.

- **Overview** (`/account`) renders `GET /api/v1/me/overview`: total, live, in review, drafts and
  needs-changes counts, a suspended card only when there is one, and the six-step completion
  checklist with a progress meter. An owner with no listings gets an empty state with a working
  "Create your first listing" action. **No views, leads, ratings or growth figures** — no table
  records them, and the reference dashboard's "Active Blogs", "InActive Blogs", "Total Views" and
  notification badge were left out for that reason.
- **Business Profile** (`/account/profile`) edits the owner's name and email, and lists their
  businesses with status and links into the listing editor. Business-level details live on a
  *listing*, so this page links to that editor rather than duplicating it.
- **Account Settings** (`/account/settings`) shows the verified number, name and email, explains the
  no-password model, and signs out through the existing server action that revokes the refresh
  token. No password, email-login or delete-account controls, because none exists.

**`PUT /api/v1/me/profile` is new, and needed.** Onboarding sets the name and email once and is
guarded by `name IS NULL` so a repeated registration cannot overwrite a profile — which also meant
an owner could never correct their own name. No migration was required: `users.name` and
`users.email` already existed. The phone number is not editable there; it is the identity the OTP
verified, and changing it would mean re-verifying.

**A Next.js detail worth remembering:** saving the profile needs *both* `revalidatePath("/account",
"layout")` and a redirect. The owner's name also shows in the dashboard bar, which lives in the
layout above the page; without the revalidate that bar keeps the old name even after a redirect,
because the layout stays in the client router cache. This was observed, not assumed.

### Admin MFA (D28–D31)

TOTP MFA is **mandatory** and enforced structurally rather than by a check in a handler. A correct
password returns only a short-lived token on a *different audience* (`admin-mfa`, or
`admin-enroll` for an admin with no authenticator yet), and the admin bearer scheme validates the
audience, so that token fails on every real admin route. The `admin_sessions` row, the access token
and the refresh token are created **only** by the MFA step, which stamps `last_mfa_at`; refresh
carries that forward and refuses a session that lacks it.

Both existing admin accounts are unenrolled, so each will be taken through authenticator setup on
its next sign-in. That is the intended path — there is no way to skip it.

- Hand-written RFC 6238 TOTP and RFC 4648 Base32, no new dependency, both checked against the
  specifications' own published test vectors.
- The secret is sealed with AES-256-GCM under `Auth:TotpEncryptionKey`; a tampered row fails to
  open rather than decrypting to rubbish.
- Ten recovery codes, shown once, stored only as hashes, spent by an `UPDATE` that matches only an
  unused row so a race cannot spend one twice.
- Wrong codes count against the same lockout allowance as wrong passwords.
- The admin website now has the matching stages: password → code (with a recovery-code toggle), or
  password → authenticator setup → recovery codes → in.

**Two bugs found and fixed on the way, one of them pre-existing:**

1. **Admin lockout never worked.** `make_interval(mins => …)` takes an integer, and the existing
   code passed a double, so Postgres could not resolve the function: *every wrong admin password
   answered 500 instead of 401, and no account was ever locked.* It had no test because the
   integration tests run without a database. Now fixed and verified: five wrong passwords lock the
   account.
2. **Replay protection was not exact.** Deriving the spent TOTP step from `last_mfa_at` lets a code
   accepted from the *next* step — which the drift window allows — be replayed, because that code
   belongs to a later step than the completion time. Migration **V0005** adds
   `admin_users.totp_last_step`, recording the step the verification itself matched.

**Still not built:** MFA re-entry before individually sensitive actions (MFA gets you a session; a
live session is not challenged again), and any endpoint to change or reset an authenticator. An
admin who loses both authenticator and recovery codes currently needs a database intervention.

**Local development caveat:** in Development a missing `Auth:TotpEncryptionKey` is generated
randomly at startup, so **restarting the API makes every enrolled secret undecryptable** and admins
must re-enroll. Set the variable to a fixed base64 value locally if you want enrollment to survive
restarts.

### Customer enquiries (V0006)

The directory's core function now leaves a record. Call and WhatsApp reach the owner but log nothing,
so a missed call was a lost customer. An enquiry is a row the owner can work through.

- `POST /api/v1/public/listings/{city}/{locality}/{slug}/enquiries` — public, no account. Accepts
  only **live** listings, normalises the phone to E.164 and lowercases the email to match the CHECK
  constraints, and is capped at 10 per IP per hour and 3 per IP per listing per day.
- `GET /api/v1/me/enquiries` — the owner's inbox, filterable by status, paginated, with an unread
  count across every listing. Every row is scoped through `business_members`, so an owner can only
  ever see enquiries for their own listings.
- `PUT /api/v1/me/enquiries/{id}/status` — read, replied, closed or spam. The ownership join is
  part of the `UPDATE`, so another owner's id matches no row and answers **404 rather than 403**,
  which tells a prober nothing.
- Public form on the listing page; `/account/enquiries` in the dashboard, which fills in the
  **Leads & Enquiries** sidebar item deliberately left out before the table existed.
- The dashboard overview now reports a real unread count instead of omitting the figure.

**No notification is sent when an enquiry arrives.** There is no email or SMS provider configured,
and telling the owner we had messaged them would be worse than them finding it in the dashboard.
That is the next thing this feature needs.

### Customer reviews and ratings (V0007, decision D11)

D11 required phone-verified reviewers plus moderation, and both are enforced rather than assumed.

- `POST /api/v1/me/reviews/{city}/{locality}/{slug}` — signed in only. Checks `phone_verified_at`,
  refuses a reviewer who **manages that business** (the most obvious way to manufacture a rating),
  and relies on a unique constraint for one-review-per-person so a double submit or a race cannot
  produce two. Returns **202, not 201**: the review exists but nothing is public yet.
- `GET /api/v1/public/listings/{city}/{locality}/{slug}/reviews` — approved reviews and the
  average, computed over exactly the set the visitor can read, so the number and the list cannot
  disagree. The average is `null` when nothing is approved, never 0, which would read as "rated
  zero". Reviewers appear by **first name only**.
- `GET /api/v1/me/reviews` — a reviewer sees their own, including a rejection note.
- `GET /api/v1/admin/reviews`, `POST .../approve`, `POST .../reject` — gated on the existing
  `reports.handle` permission, whose seeded description already covers reviews, so **no new
  permission or migration was needed**. Only a *pending* review can be decided, so two moderators
  cannot both record an outcome, and each decision writes an audit row.
- Public reviews section and a submission form on the listing page.

**Not built:** no admin *UI* for review moderation yet — the endpoints exist and are tested, but a
moderator currently needs to call them directly. Abuse reporting (flagging a published review) is
also still absent.

## Not built yet

Ordered by what unblocks the most.

1. **Admin MFA and account management.** Admin sign-in is email + password today. `docs/security.md`
   requires mandatory TOTP MFA with recovery codes, and the tables for it (`admin_users.totp_*`,
   `admin_recovery_codes`, `admin_sessions.last_mfa_at`) are already there but unused. **The admin
   site must not be exposed outside the office network until this is built.** Inviting admins,
   assigning roles and changing passwords also have no screens yet; today an account is created or
   reset with the `create-admin` command.
2. **The rest of admin moderation.** Approve, reject, add and edit exist. Suspending a live listing,
   verifying a business, and handling abuse reports are not built, although the permissions for them
   are seeded.
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

- **The site header fetches the category tree, so `getCategories()` must never throw.** `SiteHeader`
  is an async server component rendered in the root layout, which means every statically prerendered
  page — `/_not-found` included — needs the categories endpoint at build time. This broke
  `next build` with `ECONNREFUSED` whenever the API was not running, and would have broken CI,
  which builds with no API and no database. `getCategories()` now catches a failed fetch and
  returns an empty list. **Anything else added to the root layout needs the same treatment**, and
  `npm run build -w apps/main-website` should be run with the API stopped before trusting a change
  to the site chrome. The empty list is a reachable state, so the pieces that read it degrade
  rather than render empty frames: `CategoryNav` renders nothing at all (so there is no empty
  dropdown and no ☰ button with nothing behind it), and the home page skips both its
  popular-category shortcuts and its whole "Most Searched" section instead of showing a heading
  over an empty grid. Checked by building with the API unreachable: the build succeeds and
  `/_not-found` prerenders as static.

- **The listing detail page shows only what the database holds.** The reference shows a Google
  rating, opening hours, a services list, languages spoken, a contact person, social links and a
  logo; none of those have tables, so on the owner's instruction (2026-10-18) they were left out
  rather than faked or stubbed. `factsFor()` in
  `src/app/[city]/[locality]/[category]/[slug]/page.tsx` is the single place to add them.
- ~~The WhatsApp and Website rows on the detail page have not been seen rendered.~~ **Closed on
  2026-10-18**: one dev sample listing now carries both (see the dev seed note below), and all
  three places that render them were checked.
- **The sign-in and onboarding pages keep the site header and footer**, where the prototype hides
  them and fills the screen. That is deliberate: this is a public directory rather than a
  standalone portal, so someone who lands on sign-in by accident can still get back to the
  listings. It is a one-line change in `AuthLayout` if the owner would rather match the prototype.
- **The prototype's sign-in page rotates three marketing slides in a carousel.** The same three
  messages are shown as a static list instead, because a carousel would mean a JavaScript library
  for decoration alone.
- **Two test owners exist in the local dev database** from walking the OTP journey: "Redesign Test
  Owner" / "Redesign Test Plumbing" on `+919000000123` (one **draft** in Gachibowli), and
  "Redesign Test Owner Two" / "Redesign Test Bakery" on `+919000000124` (one listing in Madhapur,
  now **pending review**). The second exists because the first number had used up its five codes
  for the hour. Neither is public — a draft is not published, and the pending one is waiting in the
  admin queue, where it will show up for whoever is testing moderation. Both are left in place
  rather than deleted, because deleting data needs the owner's say-so; the dev seed data itself is
  untouched.
- **The header still shows a "LogIn" button to an owner who is already signed in.** That markup is
  in `SiteChrome.tsx`, which another session is working on, so it was left alone deliberately
  rather than edited concurrently. It wants an account link instead once that work lands.
- **Submitting the search form leaves empty parameters in the URL** (`/search?page=1&q=&category=plumbers&locality=&sort=`).
  That is how a plain GET form behaves, and the page ignores empty values, so the results and the
  filter chips are correct. Tidying it would mean handling the submit in JavaScript, which would
  cost the form its "works with scripts off" property. The chip links, which are the usual way to
  change a filter, produce clean URLs.
- **The category filter on `/search` is still one `<select>` of about 150 options.** It works, it is
  accessible, and it is grouped by parent category, but the header's mega-menu is now the better
  pattern for it.
- **The home page has no "trending" or "featured" section, because nothing in the database can
  rank listings.** There is no view count, rating or featured flag, so the dark band is headed
  "Businesses on Hyderabad Links" and shows published listings by name. Inventing a ranking would
  have meant inventing data. A real "featured" section needs either a flag on the listing or a
  popularity signal; both are small schema additions and neither is decided.
- **Every business card shows a monogram rather than a logo**, because there is no media table
  (see "Not built yet" item 4). `initialsOf()` in `src/components/ui/BusinessCard.tsx` is the one
  place to change when logos exist.
- **The header and footer only link to pages that exist.** The reference carries About, Membership,
  Contact, Terms of Use and Privacy Policy, and the footer carries social icons; none of those
  routes or accounts exist here, so they are left out rather than shipped as dead links. They are
  easy to add once there is something to point at.
- **The brand is set in type, not a logo.** The reference loads a logo from the live site; this
  repository has no logo file, so `SiteChrome.tsx` draws "Hyderabad **Links**" with a round HL mark.
- **The number of categories on the navigation bar is a fixed five.** Category names come from the
  database, so a much longer name could still crowd the bar at 1280 px. It is checked at seven
  widths with today's names.

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
  accessible, but a searchable combobox would be better once there are more categories — the
  locality one in `LocalityCombobox.tsx` is the pattern to copy.
- **Location autocomplete only knows the 103 seeded localities.** Typing an area that is not in the
  dataset finds nothing, and "use my current location" names the nearest one we do have rather than
  where the visitor actually is (it says how far away when that is over 5 km). Extending the
  dataset — see "Decisions needed" item 6 — fixes both, and a real geocoder is only needed for
  addresses rather than areas.
- **The autocomplete needs JavaScript.** The rest of the search page works without it, but the area
  field is a client component, so with scripts off a visitor can still search by keyword, category
  and sort, just not filter by area from this form (a `locality=` already in the URL still applies).
- **Page size on `/search` is fixed** at the API default of 20; `pageSize` in the URL is ignored by
  the page on purpose, so visitors cannot ask for huge pages.
- **Admin sign-in has no MFA yet.** See "Not built yet" item 1. Password strength is judged by length
  only (12 characters minimum), and nothing forces a password change.
- **An owner is not told when their listing is decided on.** The status and the rejection reason are
  on their listings screen the next time they look; there is no SMS or email, because no notification
  channel exists yet.
- **A hand-added listing has no owner account attached.** `created_by_user_id` is null, so the
  business cannot manage it until someone links it to an owner — there is no screen for that yet.
- **Public pages refresh on approval only when `REVALIDATE_TOKEN` and `MAIN_SITE_URL` are set** on
  both apps. Without them the public site still catches up on its own within 60 seconds.
- **The rejection reason is free text.** There is no list of standard reasons, so wording is up to the
  reviewer.
- **Admin session lifetime** is 15 minutes for the access token and 7 days for the refresh token
  (`Auth:AdminAccessTokenMinutes`, `Auth:AdminRefreshTokenDays`). The lockout is 5 wrong passwords for
  15 minutes, counted per account, not per IP.

## Recently decided (2026-10-17)

- **D22 — public URL scheme.** The locality-first `/{city}/{locality}/{category}/{slug}` scheme stays.
  The live site's category-first URLs are served by 301 redirects rather than adopted, so no
  three-level taxonomy migration is needed and the city segment survives for a second city.
- **D23 — subscription tiers.** Free ₹0 / Basic ₹699 / Advanced ₹6999 / Premium ₹36999, from the
  owner's prototype. This supersedes D9's Basic/Gold/Premium media-count model.
- **D24 — onboarding shape.** Registration collects name, business name, email, locality and primary
  category, and creates the profile, business, ownership row and first draft listing in one
  transaction.

### Where the design references actually are (found 2026-10-17)

They are not in this repository, and the four screenshots the owner refers to were not on this
machine either. What *is* on the machine, and is a better reference than a screenshot, is the
owner's own reference implementation of the public site:

| Reference | Path | What it covers |
|---|---|---|
| **Reference Build V2** | `C:\Users\gadip\Downloads\HyderabadLinks_Reference_Build_V2\HyderabadLinks_Reference_Build` | The public site. `Views/Home/Index.cshtml`, `Views/Search/Index.cshtml`, `Views/Search/Details.cshtml`, `Views/Shared/_PublicLayout.cshtml` and **`wwwroot/css/public-home.css`**, which is the whole visual system in one file: the teal palette, the header, the hero, the category grid, the circular logo cards, the ribbons and the footer |
| **Owner prototype** | `C:\Users\gadip\Downloads\HyderabadLinksPrototype_fixed\HyderabadLinksPrototype` | The owner portal only — `Views/Auth/Login.cshtml`, `Verify`, `Onboarding`, `Dashboard`, `Listings`, `Plans`. This is the login and onboarding workflow reference. It has **no** public homepage or search views |

The redesign now follows Reference Build V2 rather than a guess. Its homepage markup was the source
for the section order, the copy, the kickers and the card shapes; `public-home.css` was the source
for the palette now in `globals.css`.

Two things in the reference were deliberately **not** copied, and both are noted under "Known
limitations": its hard-coded "Trending" and "Featured" business lists (this site uses real listings
instead), and its About / Membership / Contact / Terms / Privacy links and social icons (those
routes do not exist here, and the reference's own social links are `href="#"`).

The reference's own search and detail URLs (`/Business/SearchResults?searchTerm=food` and
`/food/restaurants/barbeque-and-grill/kphb/{business}`) are **not** adopted: **D22** already settled
that, and the redesign changes no URL.

## Decisions needed from the owner

1. **One API or two?** See the architecture note above. Nothing is blocked while this is open.
2. **Per-tier media allowance, and the verified badge.** D23 settled the tiers and prices but not how
   many photos and videos each one allows. Separately, the prototype grants a verified badge with
   paid Basic, while the earlier requirement was that a paid plan must never auto-grant one. Both
   block media and billing work.
3. **OTP provider.** The prototype uses Twilio Verify; this API's code comments name MSG91. Only one
   should be registered for DLT, so pick before starting that paperwork.
4. **Maps provider** (D14) — still open, and now demonstrably avoidable for the moment:
   `/public/localities/nearest` resolves coordinates against the 103 rows with PostGIS, with no
   provider or key, and that is what the website uses. A real geocoder is only needed for places
   outside that dataset — street addresses rather than areas.
5. **Edge protection and private networking** (D13) — before production launch.
6. **Locality coverage.** 103 localities is partial for Hyderabad, and this is now the limiting
   factor on the location autocomplete: an area that is missing cannot be typed or resolved to.
   Extending the dataset is a reference-data job, not a code one.

## Missing credentials and configuration

None of these are in the repository, and none should be.

| What | Configuration key | Status |
|---|---|---|
| OTP / SMS delivery | an `ISmsSender` implementation + its provider keys | **Not configured.** Development prints the code to the API console (`ConsoleSmsSender`). Outside Development the API uses `UnconfiguredSmsSender`, which refuses to pretend. MSG91 is the intended provider. |
| JWT signing key | `Auth:JwtSigningKey` | Development generates one. Production must supply it. |
| OTP HMAC key | `Auth:OtpHmacKey` | As above. |
| TOTP encryption key | `Auth:TotpEncryptionKey` | **Required in production** (base64, ≥32 bytes; only the first 32 are used). Development generates a random one per start, which invalidates enrolled authenticators on every restart. Rotating it in production forces every admin to re-enroll. |
| Database | `ConnectionStrings:Default` | Local value comes from `.env` / Docker Compose. |
| Email (Resend, D14) | not wired up | Not configured. |
| Razorpay | not wired up | Not configured. |
| Azure Blob Storage | not wired up | Azurite runs locally in Docker Compose, unused so far. |

## Suggested next step

**The redesign is finished.** Every public page and every owner screen now uses the shared design
system, and each one was verified in a browser before the next was started.

What that leaves, in the order that unblocks the most:

1. **The decisions still open**, several of which now block design rather than only code — above
   all the per-tier media allowance and the verified badge (item 2 below). Media is the biggest:
   it is what turns the monograms on the cards and the detail page into real logos, and it gates
   the listing detail sections that were left out.
2. **Admin MFA** (D18), which gates exposing the admin site at all.
3. **A sitemap**, now that every public page has its final shape.
4. **The listing detail page's missing sections** — rating, opening hours, services, languages
   spoken, contact person, social links. `factsFor()` in the detail page is where they go once
   there are tables behind them.

The redesign was done one page at a time, each one verified in a browser before the next was
started. All seven steps are complete:

1. ~~**Design system and chrome**~~ — **done**. Tokens, buttons, section headings, cards, header,
   mega-menu and footer; see "What works today".
2. ~~**Location autocomplete**~~ — **done**. Both endpoints and the combobox are built and verified;
   see "What works today". The redesign restyled the combobox through the shared tokens rather than
   editing it.
3. ~~**Homepage**~~ — **done**, and approved by the owner on 2026-10-18.
4. ~~**Search results**~~ — **done**. It added `ActiveFilters`, `EmptyState` and a loading skeleton
   to the design system, and restyled `ListingCard`, which the locality + category pages share.
5. ~~**Login and onboarding screens**~~ — **done**. Added `AuthLayout` and `Field` to the design
   system; no authentication logic was touched.
6. ~~**Owner account and listing screens**~~ — **done**. `/account/listings`, the add and edit
   pages, `ListingForm`, `StatusBadge` and `SubmitForReview`; no owner logic was touched.
7. ~~**Listing detail**~~ — **done**, built from today's fields only, on the owner's instruction
   of 2026-10-18. The missing data (rating, hours, services, languages, contact person, social
   links, logo) is still missing; it was left out rather than faked.

The two smaller things the owner's brief asked for alongside the search page are now **done**: the
locality field starts on "Choose a location" and has a visible clear button. The category `<select>`
is still a long list — see "Known limitations".

Blocked until decided or built: the listing detail page in the references shows a Google rating,
opening hours, a services list, languages spoken, a contact person, social links and a logo. **None
of these have tables.** Media is the biggest piece and also gates the circular logo cards on the
search results page, so a placeholder is needed until it exists.

Still command-line only on the admin side: admin MFA (D18 — the admin site must not be exposed
publicly before it exists), inviting admins and assigning roles.
