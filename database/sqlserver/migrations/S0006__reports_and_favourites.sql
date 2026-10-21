-- S0006: abuse reports and customer favourites.
-- The SQL Server counterpart of PostgreSQL V0009.

-- Spatial and filtered indexes require these to be ON, and a client that leaves either OFF makes
-- the index creation fail rather than silently differ. Microsoft.Data.SqlClient has them ON already;
-- sqlcmd does not, so they are set here and the file is correct whichever applies it.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ---------------------------------------------------------------- reports
-- Anyone can report a listing or a review: a customer who is signed in, and a visitor who is not.
-- The seeded `reports.handle` permission already covers moderating them.
CREATE TABLE reports (
    id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT reports_pkey PRIMARY KEY,
    -- What is being reported. Kept as a column pair rather than two nullable foreign keys so adding
    -- a third reportable thing later does not mean another column.
    subject_type        varchar(20) NOT NULL,
    subject_id          bigint NOT NULL,
    reason              varchar(40) NOT NULL,
    details             nvarchar(2000) NULL,
    /* Null for a visitor who is not signed in. Reporting must not require an account: the people
       most likely to spot a fake listing are the customers it tried to catch. */
    reported_by_user_id bigint NULL CONSTRAINT reports_reporter_fk
                            REFERENCES users (id) ON DELETE SET NULL,
    -- Kept to rate-limit reports from one address, the same way enquiries are.
    reporter_ip         varchar(45) NULL,
    status              varchar(20) NOT NULL CONSTRAINT reports_status_default DEFAULT 'open',
    resolution_note     nvarchar(1000) NULL,
    handled_at          datetimeoffset(3) NULL,
    handled_by_admin_id bigint NULL CONSTRAINT reports_handled_by_fk REFERENCES admin_users (id),
    created_at          datetimeoffset(3) NOT NULL CONSTRAINT reports_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at          datetimeoffset(3) NOT NULL CONSTRAINT reports_updated_default DEFAULT SYSDATETIMEOFFSET(),
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
    CONSTRAINT reports_details_length CHECK (details IS NULL OR LEN(LTRIM(RTRIM(details))) BETWEEN 1 AND 2000),
    CONSTRAINT reports_note_length CHECK (resolution_note IS NULL OR LEN(resolution_note) <= 1000),
    /* "other" without a word of explanation tells a moderator nothing, so the schema asks for one. */
    CONSTRAINT reports_other_needs_details CHECK (reason <> 'other' OR details IS NOT NULL)
);

-- The moderation queue: one status at a time, oldest first.
CREATE INDEX reports_status_created_idx ON reports (status, created_at);

-- Every report about one thing, for the moderator looking at it.
CREATE INDEX reports_subject_idx ON reports (subject_type, subject_id);

-- Counting a reporter's recent reports, for the per-person and per-address limits.
CREATE INDEX reports_reporter_idx ON reports (reported_by_user_id, created_at DESC);
CREATE INDEX reports_reporter_ip_idx ON reports (reporter_ip, created_at DESC);

-- ------------------------------------------------------------- favourites
CREATE TABLE favourites (
    /* No cascade from users, unlike the PostgreSQL original, for the same reason as `reviews`:
       SQL Server refuses a second cascade path into one table. Nothing deletes an account anyway. */
    user_id    bigint NOT NULL CONSTRAINT favourites_user_fk REFERENCES users (id),
    listing_id bigint NOT NULL CONSTRAINT favourites_listing_fk REFERENCES listings (id) ON DELETE CASCADE,
    created_at datetimeoffset(3) NOT NULL CONSTRAINT favourites_created_default DEFAULT SYSDATETIMEOFFSET(),
    -- The pair is the identity: saving the same listing twice is the same favourite, and the
    -- constraint says so rather than the API checking first.
    CONSTRAINT favourites_pkey PRIMARY KEY (user_id, listing_id)
);

-- A person's saved list, newest first.
CREATE INDEX favourites_user_idx ON favourites (user_id, created_at DESC);
