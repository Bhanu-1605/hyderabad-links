# CLAUDE.md — instructions for every session

Read `docs/PROJECT_STATUS.md` first. It holds the current state and the exact next step.
This file holds the rules that do not change.

## What this is

HYDERABAD LINKS: a location-first business directory for Hyderabad (https://hyderabadlinks.com).
Three independent Next.js websites, one ASP.NET Core API, one SQL Server database
(Azure SQL Database in production).

## Stack rules (do not change without asking)

- **Frontend:** Next.js App Router, React, TypeScript, Tailwind. No other UI framework.
- **Backend:** ASP.NET Core minimal APIs, C#. **No ORM** — raw parameterised SQL through
  Microsoft.Data.SqlClient (decisions D6 and D41). Not EF Core, Dapper, Prisma or anything similar.
- **Database:** Microsoft SQL Server, and Azure SQL Database in production (decision D41). Spatial
  (`geography`), full-text search and a stored trigram table for typo tolerance (D42, D43).
  Migrations are forward-only SQL files under `database/sqlserver/migrations/`, applied by DbUp.
  `database/migrations/` holds the original **PostgreSQL** migrations as history: never run them.
  The conversion from PostgreSQL is **done**: schema, seeds, driver, every query and every check.
  `database/migrations`, `database/seeds` and `database/checks` are history only; each has a README
  saying so. There is no PostgreSQL left in development, runtime, testing, CI or deployment.
- **Infrastructure:** Azure App Service, Azure SQL Database, Blob Storage, Key Vault,
  Application Insights, GitHub Actions. No Kubernetes, Redis or message brokers without approval.

## Architecture rules

- The browser never calls the API directly. It calls its own Next.js server, which calls the API
  server-to-server. Frontends never connect to the database.
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

Database behaviour is checked by the transactional scripts in `database/sqlserver/checks/` — they
insert, assert and roll back, so they are safe to re-run. All thirteen at once:

```powershell
npm run db:check
```

One of them, by any part of its name:

```powershell
npm run db:check -- review
```

They need the container and the schema, so this is a local command, not a CI one. To run a file by
hand instead:

```powershell
Get-Content database/sqlserver/checks/constraints_smoke.sql | docker exec -i hl-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<the MSSQL_SA_PASSWORD from .env>" -C -d hyderabad_links -b -I
```

Note the `-i` on `docker exec`: without it sqlcmd gets no stdin and the script silently does
nothing, which looks exactly like a pass. `-I` turns on QUOTED_IDENTIFIER, which sqlcmd otherwise
leaves off.

Two things to know before adding a check. A **full-text index cannot see uncommitted rows**, so a
`CONTAINSTABLE` assertion inside one of these transactions matches nothing — or matches a leftover
committed row and passes for the wrong reason; full-text is asserted against committed rows in
`v0002_smoke`. The **trigram table is ordinary rows** written by a trigger, so typo tolerance can be
asserted inside the transaction, through `hl_similarity` against 0.3 (`pg_trgm`'s old threshold).
Give each check a slug prefix of its own, so it does not depend on the dev database being pristine.

The .NET integration tests deliberately need **no** database, because CI runs them without one.
Anything that needs real rows belongs in `database/sqlserver/checks/`.

## Running locally

See `README.md`. In short: `docker compose up -d`, `npm run db:migrate`, `npm run db:seed-dev`,
then `npm run dev:api` and `npm run dev:main`.

## Where things are

```
apps/main-website         public site (home, search, locality pages, listings, owner screens)
apps/admin-website        listing, review and report moderation
apps/super-admin-website  staff accounts, roles and the audit log
backend/BusinessDirectory.Api   the API: Endpoints/, Listings/, Auth/, Data/
database/sqlserver/migrations   versioned T-SQL, forward-only (the live path)
database/sqlserver/seeds        reference and dev seed data
database/sqlserver/checks       transactional smoke checks
database/migrations             the original PostgreSQL migrations, kept as history; never run
database/seeds, database/checks the original PostgreSQL seeds and checks, likewise
docs/                     requirements, architecture, decisions, security, deployment
```

Decisions already made (and which were assumed rather than approved) are in `docs/decisions.md`.
Check it before changing behaviour it covers.
