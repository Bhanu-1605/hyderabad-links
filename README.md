# HYDERABAD LINKS

Location-first business directory for Hyderabad, Telangana. Public site: **https://hyderabadlinks.com**.

Three independent Next.js websites share **one ASP.NET Core API** and **one PostgreSQL (PostGIS) database**.

| App | Folder | Local URL | Production domain |
|---|---|---|---|
| Main website | `apps/main-website` | http://localhost:3000 | hyderabadlinks.com |
| Admin website | `apps/admin-website` | http://localhost:3001 | *to be decided* (env-configured) |
| Super-admin website | `apps/super-admin-website` | http://localhost:3002 | *to be decided* (env-configured) |
| API | `backend/BusinessDirectory.Api` | http://localhost:5080 | *to be decided* |

**Status:** Phase 1 in progress. Schema `V0001`-`V0003` (geography, categories, users, businesses, listings, auth, admin, audit) and the Hyderabad reference seeds exist. The main website has a home page, search, area/category results and listing pages (English only); admin and super-admin are still placeholders. Owner login by phone OTP and owner listing create/edit/submit exist in the API (Phase 3b and 3c); the main website now has the owner screens too (Phase 3d): sign in with phone OTP at `/login`, then `/account/listings` to add, edit and submit listings for review. Admin review comes in Phase 5.
See `docs/` for the architecture, decisions and roadmap.

## Prerequisites (Windows; see docs/local-development.md for details)

```powershell
winget install Git.Git
winget install OpenJS.NodeJS.LTS
winget install Microsoft.DotNet.SDK.10
winget install Docker.DockerDesktop
winget install Microsoft.VisualStudioCode
```

## Run locally

```powershell
npm install                      # installs all three websites (npm workspaces)
docker compose up -d             # PostgreSQL+PostGIS and the Azure Blob emulator (Azurite)

npm run db:migrate                                     # apply migrations + reference seeds (safe to re-run)
npm run db:seed-dev                                    # optional: sample listings for local testing (Development only)
dotnet run --project backend/BusinessDirectory.Api     # API on :5080   (terminal 1)
npm run dev:main                                       # main site :3000 (terminal 2)
npm run dev:admin                                      # admin :3001     (terminal 3, optional)
npm run dev:super-admin                                # super-admin :3002 (terminal 4, optional)
```

Optional: copy `.env.example` to `.env` to change local database credentials, and `apps/<app>/.env.example`
to `apps/<app>/.env.local` to change an app's settings. **Never commit `.env` files.**

Quick checks once running:
- http://localhost:5080/health/live, http://localhost:5080/health/ready (checks the database) and http://localhost:5080/api/v1/public/meta
- http://localhost:3000/health, http://localhost:3001/health, http://localhost:3002/health

## Verify everything (build + tests)

```powershell
npm run verify
```

Runs: migration safety check, its self-tests, TypeScript checks, production builds of all three apps, the .NET build
and the .NET tests.

## Repository layout

```
apps/        three Next.js websites (independent builds and deployments)
backend/     ASP.NET Core API + unit and integration test projects
packages/    shared TypeScript config; generated API client (later)
database/    migrations/ (versioned SQL), rollback/, seeds/, queries/
tests/e2e/   Playwright end-to-end tests (later)
infrastructure/azure/   infrastructure as code (later)
docs/        requirements, architecture, decisions, security, deployment
scripts/     repository tooling (migration safety check)
```
