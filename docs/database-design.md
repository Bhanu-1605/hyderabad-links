# Database design (Phase 1 in progress)

Implemented so far: `V0001` (extensions, cities, localities, categories, slug_history) `V0002` (users, businesses, business_members, listings, listing_categories, listing_service_areas) and `V0003` (otp_challenges, user_sessions, admin_users, admin_recovery_codes, admin_sessions, roles, permissions, role_permissions, admin_user_roles, audit_logs). The rest of the groups below follow in V0004+.
`audit_logs` is append-only via rules that turn UPDATE/DELETE into no-ops. Admin roles and permissions are seeded by `R003_roles_permissions.sql`.
Listings attach to sub-categories only (composite foreign key). At most one primary category per listing is enforced by a partial unique index; "at least one" is enforced by the application before review.
Businesses with no `business_members` rows are unclaimed (pre-loaded listings waiting to be claimed).
The two-level category rule is enforced by a composite foreign key plus a CHECK constraint (no trigger).

PostgreSQL + PostGIS. Raw SQL migrations in `database/migrations`. Integer minor units for money. Slugs are never primary keys.
Extensions needed: `postgis`, `pg_trgm`, `unaccent` (must be allow-listed on Azure Flexible Server).

## Key modelling rules
- `businesses` = the brand/entity. `listings` = one public presence at one location (own locality, map point, URL, media, plan,
  verification). `listing_service_areas` = extra localities served, with no extra URL.
- `categories` = two-level tree; slugs globally unique; `listing_categories` with exactly one primary per listing.
- Listing slug unique per locality; the URL's category segment must match the primary category, otherwise 301 to the canonical URL.
  `slug_history` keeps old slugs for redirects.
- Effective plan = active paid subscription, else FREE. At most one active subscription per listing (partial unique index).
- Entitlements live in `plan_entitlements`, separate from subscription history. Prices in `plan_prices` (not hardcoded).
- `media_assets.status`: pending_upload → active; hidden_over_limit; rejected; deleted. Counting happens in a transaction that
  locks the listing row, so concurrent uploads cannot exceed limits.
- `audit_logs` and `verification_events` are append-only; the application database role has no UPDATE/DELETE on them.
- `listing_events` partitioned by month; `listing_daily_stats` holds de-duplicated rollups (anti-manipulation).

## Table groups
Geography: cities, localities · Taxonomy: categories, slug_history · Business: businesses, business_members, listings,
listing_categories, listing_service_areas · Accounts: users, user_sessions, otp_challenges, admin_users, roles, permissions,
role_permissions, admin_user_roles · Plans: plans, plan_entitlements, plan_prices · Billing: subscriptions, payments, refunds,
payment_webhook_events · Media: media_assets · Trust: verification_records, verification_events, reviews, abuse_reports ·
Engagement: enquiries, listing_events, listing_daily_stats · Services: campaigns, sponsored_placements · Governance: audit_logs.

## Search
Weighted `tsvector` (name > category/services > description), `pg_trgm` GIN index on name, GiST on location, composite index on
(locality, status).

## Migrations
`V0001__description.sql`, applied in order by DbUp (Phase 1), recorded in a history table. Production is forward-only; each
reversible migration gets a matching script in `database/rollback/`. Risky releases take a point-in-time-restore marker first.
Use expand/contract (add first, remove in a later release). CI rejects destructive statements without an explicit approval comment.
