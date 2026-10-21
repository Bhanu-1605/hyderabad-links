-- S0004: customer enquiries and customer reviews.
-- The SQL Server counterpart of PostgreSQL V0006 and V0007.

-- Spatial and filtered indexes require these to be ON, and a client that leaves either OFF makes
-- the index creation fail rather than silently differ. Microsoft.Data.SqlClient has them ON already;
-- sqlcmd does not, so they are set here and the file is correct whichever applies it.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- -------------------------------------------------------------- enquiries
-- Messages sent to a business through its public listing page. The sender has no account: they are
-- a member of the public who filled in a form, so there is no users row, just the details they gave.
CREATE TABLE enquiries (
    id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT enquiries_pkey PRIMARY KEY,
    listing_id   bigint NOT NULL CONSTRAINT enquiries_listing_fk REFERENCES listings (id) ON DELETE CASCADE,
    name         nvarchar(100) NOT NULL,
    phone_e164   varchar(16) NOT NULL,
    email        nvarchar(254) NULL,
    message      nvarchar(2000) NOT NULL,
    status       varchar(20) NOT NULL CONSTRAINT enquiries_status_default DEFAULT 'new',
    created_at   datetimeoffset(3) NOT NULL CONSTRAINT enquiries_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at   datetimeoffset(3) NOT NULL CONSTRAINT enquiries_updated_default DEFAULT SYSDATETIMEOFFSET(),
    -- Where it came from, for abuse control only. Never shown to the business owner.
    ip           varchar(45) NULL,
    CONSTRAINT enquiries_status_check CHECK (status IN ('new', 'read', 'replied', 'closed', 'spam')),
    CONSTRAINT enquiries_name_not_blank CHECK (LEN(LTRIM(RTRIM(name))) > 0),
    CONSTRAINT enquiries_name_length CHECK (LEN(LTRIM(RTRIM(name))) <= 100),
    CONSTRAINT enquiries_message_length CHECK (LEN(LTRIM(RTRIM(message))) BETWEEN 10 AND 2000),
    CONSTRAINT enquiries_phone_format CHECK (
        phone_e164 LIKE '+[1-9]%'
        AND LEN(phone_e164) BETWEEN 9 AND 16
        AND SUBSTRING(phone_e164, 2, LEN(phone_e164)) NOT LIKE '%[^0-9]%'
    ),
    CONSTRAINT enquiries_email_format CHECK (
        email IS NULL
        OR (CHARINDEX('@', email) > 1
            AND email COLLATE Latin1_General_CS_AS = LOWER(email) COLLATE Latin1_General_CS_AS)
    )
);

-- The owner's inbox: newest first, for one listing or for all the listings they own.
CREATE INDEX enquiries_listing_created_idx ON enquiries (listing_id, created_at DESC);

-- Counting unread enquiries for the dashboard, without scanning the whole table.
CREATE INDEX enquiries_new_idx ON enquiries (listing_id) WHERE status = 'new';

-- Per-sender rate limiting, the same approach otp_challenges uses.
CREATE INDEX enquiries_ip_created_idx ON enquiries (ip, created_at);

-- ---------------------------------------------------------------- reviews
-- Decision D11: a reviewer must be a phone-verified user, and every review is moderated before it
-- appears. One review per person per listing, enforced by a constraint rather than by the API, so a
-- double submit or a race cannot produce two.
CREATE TABLE reviews (
    id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT reviews_pkey PRIMARY KEY,
    listing_id            bigint NOT NULL CONSTRAINT reviews_listing_fk
                              REFERENCES listings (id) ON DELETE CASCADE,
    /* No cascade from users, unlike the PostgreSQL original: SQL Server refuses a second cascade
       path into this table. Deleting a person's account therefore means deleting their reviews
       first, which ReviewEndpoints does not do because nothing deletes an account -- it is marked
       'deleted' instead, and their reviews stop being public with it. */
    user_id               bigint NOT NULL CONSTRAINT reviews_user_fk REFERENCES users (id),
    rating                smallint NOT NULL,
    body                  nvarchar(2000) NOT NULL,
    status                varchar(20) NOT NULL CONSTRAINT reviews_status_default DEFAULT 'pending',
    -- Why a moderator rejected it. Shown to the reviewer, never to the public.
    moderation_note       nvarchar(1000) NULL,
    moderated_at          datetimeoffset(3) NULL,
    moderated_by_admin_id bigint NULL CONSTRAINT reviews_moderated_by_fk REFERENCES admin_users (id),
    created_at            datetimeoffset(3) NOT NULL CONSTRAINT reviews_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at            datetimeoffset(3) NOT NULL CONSTRAINT reviews_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT reviews_one_per_person UNIQUE (listing_id, user_id),
    CONSTRAINT reviews_rating_range CHECK (rating BETWEEN 1 AND 5),
    CONSTRAINT reviews_status_check CHECK (status IN ('pending', 'approved', 'rejected')),
    CONSTRAINT reviews_body_length CHECK (LEN(LTRIM(RTRIM(body))) BETWEEN 20 AND 2000),
    CONSTRAINT reviews_note_length CHECK (moderation_note IS NULL OR LEN(moderation_note) <= 1000)
);

-- The public view of a listing: approved reviews, newest first.
CREATE INDEX reviews_listing_approved_idx ON reviews (listing_id, created_at DESC) WHERE status = 'approved';

-- The moderation queue: oldest first, one status at a time.
CREATE INDEX reviews_status_created_idx ON reviews (status, created_at);

-- A reviewer's own reviews, so they can see what happened to them.
CREATE INDEX reviews_user_idx ON reviews (user_id);
