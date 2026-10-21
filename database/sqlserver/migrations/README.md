# SQL Server migrations

Forward-only, applied by DbUp, newest last. This is the live migration path.

`database/migrations/` holds the original **PostgreSQL** migrations. They are kept as history and
are never run again: nothing in the application connects to PostgreSQL, and a PostgreSQL migration
must never be executed against SQL Server.

The files here are a baseline rather than a replay of that history. `S0001`-`S0007` describe the
schema as it stands after the PostgreSQL `V0001`-`V0010`, grouped by subject the same way those
files were, so each one can be read beside its counterpart. `S0008` adds the search machinery, which
has no PostgreSQL equivalent to compare with. Changes from here on are new numbered files.

## Conventions, and why

| PostgreSQL | SQL Server here | Why |
|---|---|---|
| `bigint GENERATED ALWAYS AS IDENTITY` | `bigint IDENTITY(1,1)` | Same effect. SQL Server has no "generated always" form, so the application simply never inserts the column. |
| `text` | `nvarchar(n)`, or `nvarchar(max)` for free text | SQL Server cannot index `nvarchar(max)`, so anything searched, sorted or made unique gets a real length. `n` is generous but bounded. |
| `timestamptz` | `datetimeoffset(3)` | Keeps the offset, which `datetime2` would lose. Millisecond precision is more than enough and makes the row smaller. |
| `now()` | `SYSDATETIMEOFFSET()` | The same "now, with offset". |
| `boolean` | `bit` | `0`/`1`. Every comparison in the SQL is written `= 1` / `= 0`, because SQL Server has no boolean expression type. |
| `bytea` | `varbinary(n)` | Hashes and encrypted secrets, all of known length. |
| `inet` | `varchar(45)` | SQL Server has no address type. 45 characters is the longest IPv6 form. |
| `uuid` / `gen_random_uuid()` | `uniqueidentifier` / `NEWID()` | Only used for session family ids, which are never ordered by, so sequential ids buy nothing. |
| `jsonb` | `nvarchar(max)` + `CHECK (ISJSON(...) = 1)` | SQL Server has no JSON type. The check keeps the column honest, and `JSON_VALUE` reads it. |
| `text[]` | a child table | `categories.search_terms` became `category_search_terms`. SQL Server has no array type, and a table indexes and joins properly. |
| regex `CHECK (x ~ '...')` | `LIKE` patterns over `Latin1_General_BIN2` | SQL Server has no regular expressions in a check constraint. Each one is written out as `LIKE`/`NOT LIKE`, and the character ranges are compared under a **binary** collation: a range like `[a-z]` is read through the collation, and even a case-**sensitive** one orders `aAbBcC...`, so `ABIDS` would still fall inside `[a-z]` and pass. Only a binary collation compares code points, which is what "lower case only" means here. A plain equality test such as `email = LOWER(email)` needs no binary collation, only a case-sensitive one. |
| partial index `WHERE x` | filtered index `WHERE x = 1` | The same thing. SQL Server needs an explicit comparison and cannot filter on an expression. |
| `CREATE RULE ... DO INSTEAD NOTHING` | `INSTEAD OF` trigger that does nothing | How `audit_logs` stays append-only. |
| generated `tsvector` column + GIN | full-text index over the base columns | See `S0008`. |
| `pg_trgm` similarity | a maintained trigram table | See `S0008`. |
| PostGIS `geography(Point,4326)` | `geography` | SQL Server's own spatial type, SRID 4326, with a spatial index. |
