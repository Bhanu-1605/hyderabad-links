# Deployment plan (nothing is provisioned yet)

- Four independent Azure App Services: main, admin, super-admin, API. They may share one App Service *plan* (the paid compute)
  at first to save cost; each still deploys independently.
- Separate staging and production resource groups and databases.
- **Azure SQL Database** (decision D41). Two things are decided when the database is created and
  cannot be changed afterwards, so they have to be right first time:
  - **Collation** must be case-insensitive — `SQL_Latin1_General_CP1_CI_AS`, which is what the local
    container uses. Every query assumes it, with explicit `COLLATE` clauses where a check needs case
    or binary comparison.
  - **Full-text search** must be available on the chosen tier. It is supported on Azure SQL Database;
    semantic search is not, and is not used.
  The API's `ConnectionStrings__Default` is a Key Vault reference and keeps `Encrypt=True`. It must
  **not** carry `TrustServerCertificate=True`, which exists locally only for the container's
  self-signed certificate. Migrations run as a gated deployment step
  (`dotnet run --project backend/BusinessDirectory.Api -- migrate`), never from the running API, and
  the runner never creates the database. Nothing here has yet been pointed at a real Azure SQL
  database: that is untested.
- GitHub Actions: one CI workflow per deployable with path filters (exists). Deploy workflows arrive in Phase 9: Azure sign-in via
  OIDC (no stored passwords), automatic staging deploy, manual approval for production, migrations as a gated step before the API.
- App Service settings: HTTPS Only on; `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the API; Key Vault references for secrets;
  health check path `/health/live` (API) and `/health` (websites); `APP_ENV` = STAGING or PRODUCTION on each website.
- **Storage for listing media** (needed the moment photos go live). One Storage Account per environment with a blob
  container (default name `listing-media`). The API creates the container on startup, but it can only make it public-read if
  the account has **"Allow Blob public access" enabled** — the files are public listing photos, so they are served directly to
  the browser rather than through the API (decision D33). Settings on the API: `Media__BlobConnectionString` (a secret, so a
  Key Vault reference), `Media__ContainerName`, and optionally `Media__PublicBaseUrl` when a CDN or custom domain fronts it.
  With no connection string the API refuses uploads with 503 and never records a row for a file it did not store.
- No paid resource is created without an itemised cost estimate and explicit approval.
