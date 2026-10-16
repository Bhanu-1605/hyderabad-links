# HYDERABAD LINKS

Location-first business directory for Hyderabad, Telangana. Public site: **https://hyderabadlinks.com**.

Three independent Next.js websites share **one ASP.NET Core API** and **one PostgreSQL (PostGIS) database**.

| App | Folder | Local URL | Production domain |
|---|---|---|---|
| Main website | `apps/main-website` | http://localhost:3000 | hyderabadlinks.com |
| Admin website | `apps/admin-website` | http://localhost:3001 | *to be decided* (env-configured) |
| Super-admin website | `apps/super-admin-website` | http://localhost:3002 | *to be decided* (env-configured) |
| API | `backend/BusinessDirectory.Api` | http://localhost:5080 | *to be decided* |

**Status:** Schema `V0001`-`V0003` and the Hyderabad reference seeds exist. The main website has the home page,
database-backed search (keyword, category, locality, proximity, sorting and paging), locality/category pages and
listing pages (English only). Owner phone-OTP login and listing create/edit/submit work end to end. Admin and
super-admin are still placeholders, so submitted listings cannot be approved yet.
See `docs/PROJECT_STATUS.md` for the current state and the next step, and `docs/` for architecture and decisions.

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

To sign in to the admin site you need an admin account. There is no sign-up page; create one from the
command line (the password comes from an environment variable so it stays out of the shell history):

```powershell
$env:ADMIN_BOOTSTRAP_PASSWORD = "a long passphrase"
dotnet run --project backend/BusinessDirectory.Api -- create-admin --email you@example.com --name "Your Name" --role super_admin
```

Run it again with the same email to reset that account's password.

Quick checks once running:
- http://localhost:5080/health/live, http://localhost:5080/health/ready (checks the database) and http://localhost:5080/api/v1/public/meta
- http://localhost:3000/health, http://localhost:3001/health, http://localhost:3002/health

## Verify everything (build + tests)

```powershell
npm run verify
```

Runs: migration safety check, its self-tests, TypeScript checks, production builds of all three apps, the .NET build
and the .NET tests.

Database behaviour has its own checks. They insert, assert and roll back, so they leave no data and are safe to
re-run against a running local database:

```powershell
Get-Content database/checks/search_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
```

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
