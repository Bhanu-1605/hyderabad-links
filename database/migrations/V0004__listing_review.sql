-- V0004: what a reviewer decided about a listing. Additive only.
-- The audit log records *that* a decision happened and by whom; these columns hold the latest
-- decision on the listing itself, so the owner's screens can show the reason without reading audit rows.

ALTER TABLE listings
    ADD COLUMN review_note           text,
    ADD COLUMN reviewed_at           timestamptz,
    ADD COLUMN reviewed_by_admin_id  bigint REFERENCES admin_users (id);

ALTER TABLE listings
    ADD CONSTRAINT listings_review_note_length CHECK (review_note IS NULL OR char_length(review_note) <= 1000);

-- The moderation queue reads "oldest submission first" for one status at a time.
CREATE INDEX listings_status_created_idx ON listings (status, created_at);
