# The original PostgreSQL checks — history only, never run

These twelve files are the PL/pgSQL smoke checks the project used before it moved to SQL Server
(decision D41). They are kept so the behaviour each one asserted can be compared against its
replacement, and for no other reason.

**Do not run them.** They are written for `psql` against PostgreSQL with PostGIS and `pg_trgm`, and
there is no PostgreSQL in the project any more: `docker-compose.yml` starts SQL Server and Azurite
only.

The live checks are in [`database/sqlserver/checks/`](../sqlserver/checks/). Every file here has a
counterpart there under the same name, plus one extra (`constraints_smoke.sql`) covering the
constraints that had to be rewritten by hand for SQL Server:

| this folder (history)         | the live check                                 |
| ----------------------------- | ---------------------------------------------- |
| `v0002_smoke.sql`             | `../sqlserver/checks/v0002_smoke.sql`          |
| `v0003_smoke.sql`             | `../sqlserver/checks/v0003_smoke.sql`          |
| `onboarding_smoke.sql`        | `../sqlserver/checks/onboarding_smoke.sql`     |
| `owner_overview_smoke.sql`    | `../sqlserver/checks/owner_overview_smoke.sql` |
| `admin_mfa_smoke.sql`         | `../sqlserver/checks/admin_mfa_smoke.sql`      |
| `admin_moderation_smoke.sql`  | `../sqlserver/checks/admin_moderation_smoke.sql` |
| `enquiry_smoke.sql`           | `../sqlserver/checks/enquiry_smoke.sql`        |
| `review_smoke.sql`            | `../sqlserver/checks/review_smoke.sql`         |
| `media_smoke.sql`             | `../sqlserver/checks/media_smoke.sql`          |
| `notification_smoke.sql`      | `../sqlserver/checks/notification_smoke.sql`   |
| `search_smoke.sql`            | `../sqlserver/checks/search_smoke.sql`         |
| `locality_search_smoke.sql`   | `../sqlserver/checks/locality_search_smoke.sql` |
| —                             | `../sqlserver/checks/constraints_smoke.sql`    |

Each converted file opens with a note saying what had to change and why. Two of those notes are
worth knowing about before writing a new check:

- **A full-text index cannot see uncommitted rows.** These checks all run inside a transaction that
  is rolled back, so a `CONTAINSTABLE` assertion against a row the check just inserted will match
  nothing — or, worse, match a leftover committed row and pass for the wrong reason. Full-text
  behaviour is asserted against committed rows in `v0002_smoke.sql`.
- **The trigram table is ordinary rows**, written by a trigger, so typo tolerance *can* be asserted
  inside the transaction. `hl_similarity` replaces `pg_trgm`'s `%` operator and `similarity()`;
  compare against 0.3, which was `pg_trgm`'s default threshold.
