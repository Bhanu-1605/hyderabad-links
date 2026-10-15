# Seeds

`reference/` holds real reference data loaded in every environment by `npm run db:migrate` (Hyderabad localities, category tree).
Files are named `R001_...sql`, run in name order, and are idempotent (`ON CONFLICT DO NOTHING`): existing rows are never overwritten.
The category tree and locality list are drafts for owner review: see `docs/hyderabad-taxonomy-draft.md`. Locality coordinates are
approximate until verified with the maps provider.

`dev/` will hold clearly labelled **sample** data for local development only (never loaded in production).
