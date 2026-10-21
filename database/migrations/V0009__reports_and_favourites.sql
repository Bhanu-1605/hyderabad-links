-- V0009: abuse reports and customer favourites. Additive only.
--
-- Two small features that share a migration because both hang off the same pair of tables
-- (`users` and `listings`) and neither needs anything else.
--
-- Abuse reports
--   Anyone can report a listing or a review: a customer who is signed in, and a visitor who is not.
--   The seeded `reports.handle` permission already exists for moderating them, so no new permission
--   and no seed change is needed.
--
-- Favourites
--   A signed-in person saving a listing to come back to. There is deliberately no "favourite count"
--   on the public page: one person with a script could invent any number, and nothing in the product
--   needs it.

CREATE TABLE reports (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    -- What is being reported. Kept as a column pair rather than two nullable foreign keys so adding
    -- a third reportable thing later does not mean another column.
    subject_type     text NOT NULL,
    subject_id       bigint NOT NULL,
    reason           text NOT NULL,
    details          text,
    /* Null for a visitor who is not signed in. Reporting must not require an account: the people
       most likely to spot a fake listing are the customers it tried to catch. */
    reported_by_user_id bigint REFERENCES users (id) ON DELETE SET NULL,
    -- Kept to rate-limit reports from one address, the same way enquiries are.
    reporter_ip      inet,
    status           text NOT NULL DEFAULT 'open',
    resolution_note  text,
    handled_at       timestamptz,
    handled_by_admin_id bigint REFERENCES admin_users (id),
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reports_subject_type_check CHECK (subject_type IN ('listing', 'review')),
    CONSTRAINT reports_reason_check CHECK (reason IN (
        'not_a_real_business',
        'wrong_details',
        'closed_down',
        'offensive_content',
        'spam',
        'other'
    )),
    CONSTRAINT reports_status_check CHECK (status IN ('open', 'actioned', 'dismissed')),
    CONSTRAINT reports_details_length CHECK (details IS NULL OR char_length(btrim(details)) BETWEEN 1 AND 2000),
    CONSTRAINT reports_note_length CHECK (resolution_note IS NULL OR char_length(resolution_note) <= 1000),
    /* "other" without a word of explanation tells a moderator nothing, so the schema asks for one. */
    CONSTRAINT reports_other_needs_details CHECK (reason <> 'other' OR details IS NOT NULL)
);

-- The moderation queue: open reports first, oldest first within a status.
CREATE INDEX reports_status_created_idx ON reports (status, created_at);

-- Every report about one thing, for the moderator looking at it.
CREATE INDEX reports_subject_idx ON reports (subject_type, subject_id);

-- Counting a reporter's recent reports, for the per-person and per-address limits.
CREATE INDEX reports_reporter_idx ON reports (reported_by_user_id, created_at DESC);
CREATE INDEX reports_reporter_ip_idx ON reports (reporter_ip, created_at DESC);

CREATE TABLE favourites (
    user_id    bigint NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    listing_id bigint NOT NULL REFERENCES listings (id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    -- The pair is the identity: saving the same listing twice is the same favourite, and the
    -- constraint says so rather than the API checking first.
    PRIMARY KEY (user_id, listing_id)
);

-- A person's saved list, newest first.
CREATE INDEX favourites_user_idx ON favourites (user_id, created_at DESC);

COMMENT ON TABLE reports IS
    'Abuse reports about a listing or a review. Handled under the seeded reports.handle permission.';
COMMENT ON COLUMN reports.reported_by_user_id IS
    'Null when the reporter was not signed in; reporting deliberately does not require an account.';
COMMENT ON TABLE favourites IS
    'Listings a signed-in person saved. Private to that person; no public count is derived from it.';
