# Requirements summary

Source of truth for scope. Full original brief is held by the project owner; this is the condensed version.

## Product
- HYDERABAD LINKS: location-first business directory, Hyderabad first, designed to expand to other cities.
- Public domain: hyderabadlinks.com. Admin and super-admin domains **not decided**: must stay configurable.

## Hard constraints
- Next.js (App Router) + React + TypeScript + Tailwind; ASP.NET Core Web API (C#); PostgreSQL + Npgsql + raw SQL + PostGIS; **no ORM**.
- Azure: App Service, PostgreSQL Flexible Server, Blob Storage, Key Vault, Application Insights. GitHub + GitHub Actions.
- Modular monolith. No Kubernetes / microservices without demonstrated need.
- Three separate Next.js apps (main, admin, super-admin), independently deployable, one API, one database.
- Browsers never connect to the database; all changes go through authorised API endpoints; permissions enforced on the server.
- OpenAPI is the API contract; TypeScript client is generated.
- Migrations: version-controlled SQL; no destructive migration without approval.
- No paid Azure resource without a cost explanation and approval.

## Plans (stored in the database, editable by authorised staff; prices NOT finalised)
| Plan | Photos | Videos |
|---|---|---|
| Free | 0 (assumption) | 0 |
| Basic | 4 | 0 |
| Gold | 4 | 2 |
| Premium | 16 | 4 |

- Limits enforced on the backend, never only in the UI.
- A paid plan never grants verification automatically; verification is a separate process with its own audit trail.
- Downgrades never silently delete media.
- Paid features activate only after the backend confirms payment (signed, idempotent Razorpay webhooks).

## Public URL structure
`/`, `/{city}`, `/{city}/{locality}`, `/{city}/{locality}/{category}`, `/{city}/{locality}/{category}/{business-slug}`
(lowercase, hyphenated; immutable database IDs internally; canonical URLs and 301 redirects for changes).
