# Deployment plan (nothing is provisioned yet)

- Four independent Azure App Services: main, admin, super-admin, API. They may share one App Service *plan* (the paid compute)
  at first to save cost; each still deploys independently.
- Separate staging and production resource groups and databases.
- GitHub Actions: one CI workflow per deployable with path filters (exists). Deploy workflows arrive in Phase 9: Azure sign-in via
  OIDC (no stored passwords), automatic staging deploy, manual approval for production, migrations as a gated step before the API.
- App Service settings: HTTPS Only on; `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the API; Key Vault references for secrets;
  health check path `/health/live` (API) and `/health` (websites); `APP_ENV` = STAGING or PRODUCTION on each website.
- No paid resource is created without an itemised cost estimate and explicit approval.
