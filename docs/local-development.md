# Local development

1. Install prerequisites (see README). After installing Docker Desktop, start it before `docker compose up -d`.
2. `npm install` at the repo root.
3. `docker compose up -d`, then `docker compose ps`: `hl-postgres` should show *healthy*.
4. `npm run db:migrate` creates the tables and loads the reference data (re-run any time; it only applies what is new).
5. Start the API and the website(s) you need (commands in README).
6. `npm run verify` before every commit.

Troubleshooting
- Port already in use: change the port in the app's `dev` script or stop the other process.
- `dotnet` not found after install: open a new terminal.
- Docker errors on Windows: confirm WSL2 is enabled and Docker Desktop is running.
- Local database credentials are throwaway values from `.env.example`. Never reuse them anywhere real.

## Approving a listing by hand (until the admin site exists, Phase 5)

New owner listings start as `draft`, and become `pending_review` when the owner submits them. Only `active` listings appear on the public site.
To approve one locally, run (replace `12` with the listing id):

```powershell
docker exec -it hl-postgres psql -U hl_dev -d hyderabad_links -c "UPDATE listings SET status='active', published_at=now() WHERE id=12;"
```
