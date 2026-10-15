# Migrations

Versioned SQL files named `V0001__short_description.sql`. Applied in order; never edit a migration that has been applied to a
shared environment: add a new one. Run `npm run check:migrations` before committing. The first migrations arrive in Phase 1.
