-- S0002: users, businesses, listings and their category / service-area links.
-- The SQL Server counterpart of PostgreSQL V0002, with the review columns V0004 added and the
-- plan column V0008 added folded in where they belong (see S0005 for the plans table itself).
--
-- An E.164 phone number is '+' then 8 to 15 digits. PostgreSQL said that in one regex; here it is
-- three conditions, because a check constraint has no regular expressions: it starts with a plus and
-- a non-zero digit, the rest is digits only, and the length is in range.

-- Spatial and filtered indexes require these to be ON, and a client that leaves either OFF makes
-- the index creation fail rather than silently differ. Microsoft.Data.SqlClient has them ON already;
-- sqlcmd does not, so they are set here and the file is correct whichever applies it.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ------------------------------------------------------------------ users
-- Business owners and reviewers. Identified by a verified phone number in E.164 form.
CREATE TABLE users (
    id                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT users_pkey PRIMARY KEY,
    phone_e164         varchar(16) NOT NULL,
    phone_verified_at  datetimeoffset(3) NULL,
    name               nvarchar(200) NULL,
    email              nvarchar(254) NULL,
    email_verified_at  datetimeoffset(3) NULL,
    status             varchar(20) NOT NULL CONSTRAINT users_status_default DEFAULT 'active',
    created_at         datetimeoffset(3) NOT NULL CONSTRAINT users_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at         datetimeoffset(3) NOT NULL CONSTRAINT users_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT users_phone_key UNIQUE (phone_e164),
    CONSTRAINT users_phone_format CHECK (
        phone_e164 LIKE '+[1-9]%'
        AND LEN(phone_e164) BETWEEN 9 AND 16
        AND SUBSTRING(phone_e164, 2, LEN(phone_e164)) NOT LIKE '%[^0-9]%'
    ),
    CONSTRAINT users_status_check CHECK (status IN ('active', 'suspended', 'deleted'))
);

-- -------------------------------------------------------------- businesses
-- The brand / legal entity. A business with several branches has several listings (decision D3).
CREATE TABLE businesses (
    id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT businesses_pkey PRIMARY KEY,
    name        nvarchar(300) NOT NULL,
    created_at  datetimeoffset(3) NOT NULL CONSTRAINT businesses_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at  datetimeoffset(3) NOT NULL CONSTRAINT businesses_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT businesses_name_not_blank CHECK (LEN(LTRIM(RTRIM(name))) > 0)
);

-- Who may manage a business. A business with no members is unclaimed (pre-loaded listing waiting to be claimed).
CREATE TABLE business_members (
    business_id  bigint NOT NULL CONSTRAINT business_members_business_fk
                     REFERENCES businesses (id) ON DELETE CASCADE,
    user_id      bigint NOT NULL CONSTRAINT business_members_user_fk REFERENCES users (id),
    role         varchar(20) NOT NULL CONSTRAINT business_members_role_default DEFAULT 'owner',
    created_at   datetimeoffset(3) NOT NULL CONSTRAINT business_members_created_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT business_members_pkey PRIMARY KEY (business_id, user_id),
    CONSTRAINT business_members_role_check CHECK (role IN ('owner', 'manager'))
);
CREATE INDEX business_members_user_idx ON business_members (user_id);

-- ---------------------------------------------------------------- listings
-- One public presence of a business at one location. Own locality, map point, URL and plan.
CREATE TABLE listings (
    id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT listings_pkey PRIMARY KEY,
    business_id         bigint NOT NULL CONSTRAINT listings_business_fk REFERENCES businesses (id),
    locality_id         int NOT NULL CONSTRAINT listings_locality_fk REFERENCES localities (id),
    slug                nvarchar(120) NOT NULL,
    name                nvarchar(300) NOT NULL,
    description         nvarchar(max) NULL,
    address_line        nvarchar(400) NULL,
    pincode             nvarchar(10) NULL,
    location            geography NOT NULL,
    phone_primary       varchar(16) NOT NULL,
    phone_whatsapp      varchar(16) NULL,
    email               nvarchar(254) NULL,
    website_url         nvarchar(500) NULL,
    status              varchar(20) NOT NULL CONSTRAINT listings_status_default DEFAULT 'draft',
    source              varchar(20) NOT NULL CONSTRAINT listings_source_default DEFAULT 'owner',
    created_by_user_id  bigint NULL CONSTRAINT listings_created_by_fk REFERENCES users (id),
    published_at        datetimeoffset(3) NULL,
    -- The latest review decision, which the owner's screens read without touching the audit log (V0004).
    review_note         nvarchar(1000) NULL,
    reviewed_at         datetimeoffset(3) NULL,
    reviewed_by_admin_id bigint NULL,   -- foreign key added in S0003, once admin_users exists
    created_at          datetimeoffset(3) NOT NULL CONSTRAINT listings_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at          datetimeoffset(3) NOT NULL CONSTRAINT listings_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT listings_locality_slug_key UNIQUE (locality_id, slug),
    CONSTRAINT listings_slug_format CHECK (
        LEN(slug) > 0
        AND slug COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9-]%'
        AND slug NOT LIKE '-%' AND slug NOT LIKE '%-' AND slug NOT LIKE '%--%'
    ),
    CONSTRAINT listings_name_not_blank CHECK (LEN(LTRIM(RTRIM(name))) > 0),
    CONSTRAINT listings_phone_primary_format CHECK (
        phone_primary LIKE '+[1-9]%'
        AND LEN(phone_primary) BETWEEN 9 AND 16
        AND SUBSTRING(phone_primary, 2, LEN(phone_primary)) NOT LIKE '%[^0-9]%'
    ),
    CONSTRAINT listings_phone_whatsapp_format CHECK (
        phone_whatsapp IS NULL
        OR (phone_whatsapp LIKE '+[1-9]%'
            AND LEN(phone_whatsapp) BETWEEN 9 AND 16
            AND SUBSTRING(phone_whatsapp, 2, LEN(phone_whatsapp)) NOT LIKE '%[^0-9]%')
    ),
    CONSTRAINT listings_status_check CHECK (status IN ('draft', 'pending_review', 'active', 'rejected', 'suspended', 'archived')),
    CONSTRAINT listings_source_check CHECK (source IN ('owner', 'admin', 'import')),
    CONSTRAINT listings_review_note_length CHECK (review_note IS NULL OR LEN(review_note) <= 1000)
);

CREATE SPATIAL INDEX listings_location_gix ON listings (location)
    USING GEOGRAPHY_GRID WITH (GRIDS = (MEDIUM, MEDIUM, MEDIUM, MEDIUM), CELLS_PER_OBJECT = 16);
CREATE INDEX listings_locality_status_idx ON listings (locality_id, status);
CREATE INDEX listings_business_idx ON listings (business_id);
CREATE INDEX listings_phone_primary_idx ON listings (phone_primary);   -- duplicate detection
CREATE INDEX listings_status_created_idx ON listings (status, created_at);   -- the moderation queue

-- ------------------------------------------------------ listing categories
-- Listings attach to level-2 (sub) categories only; the composite foreign key enforces that.
-- At most one primary category per listing is enforced here; "at least one" is enforced by the
-- application before a listing can be submitted. The primary category drives the canonical URL (D4).
CREATE TABLE listing_categories (
    listing_id      bigint NOT NULL CONSTRAINT listing_categories_listing_fk
                        REFERENCES listings (id) ON DELETE CASCADE,
    category_id     int NOT NULL,
    category_level  smallint NOT NULL CONSTRAINT listing_categories_level_default DEFAULT 2,
    is_primary      bit NOT NULL CONSTRAINT listing_categories_primary_default DEFAULT 0,
    created_at      datetimeoffset(3) NOT NULL CONSTRAINT listing_categories_created_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT listing_categories_pkey PRIMARY KEY (listing_id, category_id),
    CONSTRAINT listing_categories_level_check CHECK (category_level = 2),
    CONSTRAINT listing_categories_category_fk FOREIGN KEY (category_id, category_level) REFERENCES categories (id, level)
);
-- One primary per listing, as a filtered unique index: SQL Server needs the explicit `= 1`.
CREATE UNIQUE INDEX listing_categories_one_primary_idx ON listing_categories (listing_id) WHERE is_primary = 1;
CREATE INDEX listing_categories_category_idx ON listing_categories (category_id);

-- --------------------------------------------------- listing service areas
-- Extra localities a listing serves. They add search reach but no extra public URL (decision D3).
CREATE TABLE listing_service_areas (
    listing_id   bigint NOT NULL CONSTRAINT listing_service_areas_listing_fk
                     REFERENCES listings (id) ON DELETE CASCADE,
    locality_id  int NOT NULL CONSTRAINT listing_service_areas_locality_fk REFERENCES localities (id),
    created_at   datetimeoffset(3) NOT NULL CONSTRAINT listing_service_areas_created_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT listing_service_areas_pkey PRIMARY KEY (listing_id, locality_id)
);
CREATE INDEX listing_service_areas_locality_idx ON listing_service_areas (locality_id);
