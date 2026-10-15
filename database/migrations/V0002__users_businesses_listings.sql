-- V0002: minimal users, businesses, listings and their category / service-area links.
-- Additive only. OTP challenges, sessions and admin accounts arrive in V0003 (they extend `users`, they do not replace it).

-- ------------------------------------------------------------------ users
-- Business owners and (later) reviewers. Identified by a verified phone number in E.164 form.
CREATE TABLE users (
    id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    phone_e164         text NOT NULL,
    phone_verified_at  timestamptz,
    name               text,
    email              text,
    email_verified_at  timestamptz,
    status             text NOT NULL DEFAULT 'active',
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT users_phone_key UNIQUE (phone_e164),
    CONSTRAINT users_phone_format CHECK (phone_e164 ~ '^\+[1-9][0-9]{7,14}$'),
    CONSTRAINT users_status_check CHECK (status IN ('active', 'suspended', 'deleted'))
);

-- -------------------------------------------------------------- businesses
-- The brand / legal entity. A business with several branches has several listings (decision D3).
CREATE TABLE businesses (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        text NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT businesses_name_not_blank CHECK (btrim(name) <> '')
);

-- Who may manage a business. A business with no members is unclaimed (pre-loaded listing waiting to be claimed).
CREATE TABLE business_members (
    business_id  bigint NOT NULL REFERENCES businesses (id) ON DELETE CASCADE,
    user_id      bigint NOT NULL REFERENCES users (id),
    role         text NOT NULL DEFAULT 'owner',
    created_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (business_id, user_id),
    CONSTRAINT business_members_role_check CHECK (role IN ('owner', 'manager'))
);
CREATE INDEX business_members_user_idx ON business_members (user_id);

-- ---------------------------------------------------------------- listings
-- One public presence of a business at one location. Own locality, map point, URL and (later) plan.
CREATE TABLE listings (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    business_id         bigint NOT NULL REFERENCES businesses (id),
    locality_id         integer NOT NULL REFERENCES localities (id),
    slug                text NOT NULL,
    name                text NOT NULL,
    description         text,
    address_line        text,
    pincode             text,
    location            geography(Point, 4326) NOT NULL,
    phone_primary       text NOT NULL,
    phone_whatsapp      text,
    email               text,
    website_url         text,
    status              text NOT NULL DEFAULT 'draft',
    source              text NOT NULL DEFAULT 'owner',
    created_by_user_id  bigint REFERENCES users (id),
    published_at        timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    -- Weighted full-text vector: name (A) outranks description (C). Category/service text is added in a later migration.
    search_vector       tsvector GENERATED ALWAYS AS (
                            setweight(to_tsvector('simple', coalesce(name, '')), 'A')
                            || setweight(to_tsvector('simple', coalesce(description, '')), 'C')
                        ) STORED,
    CONSTRAINT listings_locality_slug_key UNIQUE (locality_id, slug),
    CONSTRAINT listings_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
    CONSTRAINT listings_name_not_blank CHECK (btrim(name) <> ''),
    CONSTRAINT listings_phone_primary_format CHECK (phone_primary ~ '^\+[1-9][0-9]{7,14}$'),
    CONSTRAINT listings_phone_whatsapp_format CHECK (phone_whatsapp IS NULL OR phone_whatsapp ~ '^\+[1-9][0-9]{7,14}$'),
    CONSTRAINT listings_status_check CHECK (status IN ('draft', 'pending_review', 'active', 'rejected', 'suspended', 'archived')),
    CONSTRAINT listings_source_check CHECK (source IN ('owner', 'admin', 'import'))
);
CREATE INDEX listings_location_gix ON listings USING gist (location);
CREATE INDEX listings_locality_status_idx ON listings (locality_id, status);
CREATE INDEX listings_business_idx ON listings (business_id);
CREATE INDEX listings_phone_primary_idx ON listings (phone_primary);        -- duplicate detection
CREATE INDEX listings_name_trgm_idx ON listings USING gin (name gin_trgm_ops);  -- typo-tolerant search
CREATE INDEX listings_search_vector_idx ON listings USING gin (search_vector);

-- ------------------------------------------------------ listing categories
-- Listings attach to level-2 (sub) categories only; the composite foreign key enforces that.
-- At most one primary category per listing is enforced here; "at least one" is enforced by the application
-- before a listing can be submitted for review. The primary category drives the canonical URL (decision D4).
CREATE TABLE listing_categories (
    listing_id      bigint NOT NULL REFERENCES listings (id) ON DELETE CASCADE,
    category_id     integer NOT NULL,
    category_level  smallint NOT NULL DEFAULT 2,
    is_primary      boolean NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (listing_id, category_id),
    CONSTRAINT listing_categories_level_check CHECK (category_level = 2),
    CONSTRAINT listing_categories_category_fk FOREIGN KEY (category_id, category_level) REFERENCES categories (id, level)
);
CREATE UNIQUE INDEX listing_categories_one_primary_idx ON listing_categories (listing_id) WHERE is_primary;
CREATE INDEX listing_categories_category_idx ON listing_categories (category_id);

-- --------------------------------------------------- listing service areas
-- Extra localities a listing serves. They add search reach but no extra public URL (decision D3).
CREATE TABLE listing_service_areas (
    listing_id   bigint NOT NULL REFERENCES listings (id) ON DELETE CASCADE,
    locality_id  integer NOT NULL REFERENCES localities (id),
    created_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (listing_id, locality_id)
);
CREATE INDEX listing_service_areas_locality_idx ON listing_service_areas (locality_id);
