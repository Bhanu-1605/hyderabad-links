-- V0008: subscription plans, the plan a listing is on, and listing media. Additive only.
--
-- Plans live in the database because requirements.md says they are "stored in the database,
-- editable by authorised staff". Putting the allowances in rows rather than in C# constants is also
-- what lets the owner settle the outstanding policy (see below) with an UPDATE instead of a deploy.
--
-- OUTSTANDING OWNER DECISIONS, deliberately not invented here:
--   * The photo/video allowance per tier. requirements.md documents Free 0/0, Basic 4/0, Gold 4/2,
--     Premium 16/4, but decision D23 replaced "Gold" with "Advanced", so the middle tier's numbers
--     are an inference rather than a decision. The values seeded below carry the documented numbers
--     with Gold's figures placed on Advanced; `media_limits_confirmed` marks them as provisional so
--     the application can say so rather than implying they are settled.
--   * The billing period for the paid tiers is documented nowhere, so no renewal date is computed.
--   * Whether a paid plan grants a verified badge. requirements.md says a paid plan must NEVER grant
--     verification automatically, so `grants_verified_badge` is false everywhere and verification
--     stays a separate process.

CREATE TABLE plans (
    slug                    text PRIMARY KEY,
    name                    text NOT NULL,
    -- Paise, not rupees: integer money avoids the rounding problems of a float.
    price_paise             bigint NOT NULL,
    photo_limit             integer NOT NULL,
    video_limit             integer NOT NULL,
    -- A logo is identity rather than promotion, so it is counted separately from photos.
    logo_limit              integer NOT NULL,
    grants_verified_badge   boolean NOT NULL DEFAULT false,
    /* False until the owner confirms the per-tier allowances. The UI reads this so it can avoid
       presenting a provisional number as a promise. */
    media_limits_confirmed  boolean NOT NULL DEFAULT false,
    sort_order              integer NOT NULL DEFAULT 0,
    is_active               boolean NOT NULL DEFAULT true,
    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT plans_price_not_negative CHECK (price_paise >= 0),
    CONSTRAINT plans_limits_not_negative CHECK (photo_limit >= 0 AND video_limit >= 0 AND logo_limit >= 0),
    CONSTRAINT plans_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
);

-- The four plans have to exist before the column that references them, because every existing
-- listing takes the 'free' default the moment the column is added. R004_plans.sql is the
-- idempotent reference seed that keeps these rows current afterwards.
INSERT INTO plans (slug, name, price_paise, photo_limit, video_limit, logo_limit, sort_order) VALUES
  ('free',     'Free',     0,        0,  0, 0, 0),
  ('basic',    'Basic',    69900,    4,  0, 1, 1),
  ('advanced', 'Advanced', 699900,   4,  2, 1, 2),
  ('premium',  'Premium',  3699900, 16,  4, 1, 3);

-- Decision D5: subscriptions are per listing, so the plan hangs off the listing.
-- Every existing listing becomes Free, which is the no-cost default and changes nothing about them.
ALTER TABLE listings
    ADD COLUMN plan_slug text NOT NULL DEFAULT 'free' REFERENCES plans (slug);

CREATE INDEX listings_plan_idx ON listings (plan_slug);

-- Media belongs to a listing. The binary never goes in PostgreSQL: `storage_key` names the blob and
-- the bytes live in Azure Blob Storage (Azurite locally).
CREATE TABLE listing_media (
    id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    listing_id         bigint NOT NULL REFERENCES listings (id) ON DELETE CASCADE,
    kind               text NOT NULL,
    -- Random, not derived from the uploaded filename: a caller must never be able to choose a path.
    storage_key        text NOT NULL,
    content_type       text NOT NULL,
    byte_size          bigint NOT NULL,
    -- Null for video, and for an image whose dimensions could not be read.
    width              integer,
    height             integer,
    sort_order         integer NOT NULL DEFAULT 0,
    /* D10: when a plan is downgraded, media over the new allowance is hidden, never deleted. */
    is_hidden          boolean NOT NULL DEFAULT false,
    created_at         timestamptz NOT NULL DEFAULT now(),
    created_by_user_id bigint REFERENCES users (id),
    CONSTRAINT listing_media_storage_key_key UNIQUE (storage_key),
    CONSTRAINT listing_media_kind_check CHECK (kind IN ('logo', 'photo', 'video')),
    CONSTRAINT listing_media_size_positive CHECK (byte_size > 0),
    /* Either both dimensions are known or neither is. The IS NOT NULL tests are not redundant:
       with only a width set, "width > 0 AND height > 0" is NULL rather than false, and a CHECK
       constraint accepts NULL, so the half-filled row would have been stored. */
    CONSTRAINT listing_media_dimensions CHECK (
        (width IS NULL AND height IS NULL)
        OR (width IS NOT NULL AND height IS NOT NULL AND width > 0 AND height > 0)
    )
);

-- The gallery for one listing, in display order, visible items first.
CREATE INDEX listing_media_listing_idx ON listing_media (listing_id, kind, sort_order);

-- Counting what a listing already has, to compare against its allowance.
CREATE INDEX listing_media_quota_idx ON listing_media (listing_id, kind) WHERE NOT is_hidden;

COMMENT ON TABLE plans IS
    'Subscription plans. Prices from decision D23; media allowances provisional until the owner confirms them.';
COMMENT ON COLUMN listing_media.storage_key IS
    'Blob name in Azure Blob Storage. Randomly generated; never derived from the uploaded filename.';
