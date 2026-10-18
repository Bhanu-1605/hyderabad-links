# CLAUDE.md — instructions for every session

Read `docs/PROJECT_STATUS.md` first. It holds the current state and the exact next step.
This file holds the rules that do not change.

## What this is

HYDERABAD LINKS: a location-first business directory for Hyderabad (https://hyderabadlinks.com).
Three independent Next.js websites, one ASP.NET Core API, one PostgreSQL + PostGIS database.

## Stack rules (do not change without asking)

- **Frontend:** Next.js App Router, React, TypeScript, Tailwind. No other UI framework.
- **Backend:** ASP.NET Core minimal APIs, C#. **No ORM** — raw parameterised SQL through Npgsql
  (decision D6). Not EF Core, Dapper, Prisma or anything similar.
- **Database:** PostgreSQL with PostGIS, pg_trgm and full-text search. Migrations are forward-only
  SQL files under `database/migrations/`, applied by DbUp (decision D8).
- **Infrastructure:** Azure App Service, Azure Database for PostgreSQL, Blob Storage, Key Vault,
  Application Insights, GitHub Actions. No Kubernetes, Redis or message brokers without approval.

## Architecture rules

- The browser never calls the API directly. It calls its own Next.js server, which calls the API
  server-to-server. Frontends never connect to PostgreSQL.
- Route groups follow the audience: `/api/v1/public`, `/api/v1/me` (owners), `/api/v1/admin`,
  `/api/v1/super-admin`. Separate token audiences keep them apart.
- Every permission is enforced in the API. Hiding a button in the UI is never the control.
- Owners may only touch businesses they are authorised for. A matching phone number is not proof
  of ownership.

## How to work here

- Keep the code readable: the owner is learning. Plain code with clear names beats clever
  abstraction. Comment the *why*, not the *what*.
- Do not add an interface, service, package or folder without a real use for it today.
- Never claim a feature works because the code exists. Run it.
- Report test results honestly: passed, failed, not run, or blocked.
- Never print OTPs, tokens, passwords or keys into logs, reports or commits.
- Ask before: destructive migrations, deleting data, production operations, paid cloud resources,
  and any major architectural migration.

## Verifying changes

```powershell
npm run verify     # migration check, script tests, typecheck, 3 production builds, .NET build + tests
```

Targeted checks while working:

```powershell
npm run typecheck -w apps/main-website
dotnet test backend/BusinessDirectory.sln
```

Database behaviour is checked by the transactional scripts in `database/checks/` — they insert,
assert and roll back, so they are safe to re-run:

```powershell
Get-Content database/checks/search_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
```

`database/checks/` currently holds `v0002_smoke`, `v0003_smoke`, `search_smoke`,
`locality_search_smoke`, `onboarding_smoke` and `admin_moderation_smoke`. Note the `-i` on `docker exec`: without it psql gets no stdin and the
script silently does nothing.

The .NET integration tests deliberately need **no** database, because CI runs them without one.
Anything that needs real rows belongs in `database/checks/`.

## Running locally

See `README.md`. In short: `docker compose up -d`, `npm run db:migrate`, `npm run db:seed-dev`,
then `npm run dev:api` and `npm run dev:main`.

## Where things are

```
apps/main-website         public site (home, search, locality pages, listings, owner screens)
apps/admin-website        moderation and management (placeholder)
apps/super-admin-website  platform administration (placeholder)
backend/BusinessDirectory.Api   the API: Endpoints/, Listings/, Auth/, Data/
database/migrations       versioned SQL, forward-only
database/seeds/reference  real Hyderabad localities, categories, roles
database/seeds/dev        clearly labelled sample listings, Development only
database/checks           transactional smoke checks
docs/                     requirements, architecture, decisions, security, deployment
```

Decisions already made (and which were assumed rather than approved) are in `docs/decisions.md`.
Check it before changing behaviour it covers.
