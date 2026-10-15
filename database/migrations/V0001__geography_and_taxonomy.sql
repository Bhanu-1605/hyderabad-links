-- V0001: extensions, geography (cities, localities), taxonomy (categories), slug history.
-- Additive only. Extensions must be allow-listed on Azure PostgreSQL Flexible Server (azure.extensions).

CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

-- ---------------------------------------------------------------- cities
CREATE TABLE cities (
    id            integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    slug          text NOT NULL,
    name          text NOT NULL,
    state         text NOT NULL,
    country_code  char(2) NOT NULL DEFAULT 'IN',
    center        geography(Point, 4326),
    is_active     boolean NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT cities_slug_key UNIQUE (slug),
    CONSTRAINT cities_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
);

-- ------------------------------------------------------------- localities
CREATE TABLE localities (
    id          integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    city_id     integer NOT NULL REFERENCES cities (id),
    slug        text NOT NULL,
    name        text NOT NULL,
    center      geography(Point, 4326) NOT NULL,
    pincode     text,
    is_active   boolean NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT localities_city_slug_key UNIQUE (city_id, slug),
    CONSTRAINT localities_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
);
CREATE INDEX localities_center_gix ON localities USING gist (center);
CREATE INDEX localities_name_trgm_idx ON localities USING gin (name gin_trgm_ops);

-- ------------------------------------------------------------- categories
-- Two-level tree, enforced by the database itself (no trigger):
--   level 1 = parent (no parent), level 2 = child whose parent must be a level-1 row.
-- Slugs are globally unique across both levels (decision D4).
CREATE TABLE categories (
    id            integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    level         smallint NOT NULL,
    parent_id     integer,
    parent_level  smallint,
    slug          text NOT NULL,
    name          text NOT NULL,
    search_terms  text[] NOT NULL DEFAULT ARRAY[]::text[],
    sort_order    integer NOT NULL DEFAULT 0,
    is_active     boolean NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT categories_slug_key UNIQUE (slug),
    CONSTRAINT categories_slug_format CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
    CONSTRAINT categories_id_level_key UNIQUE (id, level),
    CONSTRAINT categories_parent_fk FOREIGN KEY (parent_id, parent_level) REFERENCES categories (id, level),
    CONSTRAINT categories_two_levels CHECK (
        (level = 1 AND parent_id IS NULL AND parent_level IS NULL)
        OR (level = 2 AND parent_id IS NOT NULL AND parent_level = 1)
    )
);
CREATE INDEX categories_parent_idx ON categories (parent_id);
CREATE INDEX categories_name_trgm_idx ON categories USING gin (name gin_trgm_ops);
CREATE INDEX categories_search_terms_idx ON categories USING gin (search_terms);

-- ----------------------------------------------------------- slug history
-- Old slugs kept so changed URLs can 301 to the new one. scope_key narrows uniqueness
-- (for example the locality id for a listing slug); use an empty string when not needed.
CREATE TABLE slug_history (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    entity_type  text NOT NULL,
    entity_id    bigint NOT NULL,
    scope_key    text NOT NULL DEFAULT '',
    old_slug     text NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT slug_history_entity_type_check CHECK (entity_type IN ('city', 'locality', 'category', 'listing')),
    CONSTRAINT slug_history_unique UNIQUE (entity_type, scope_key, old_slug)
);
CREATE INDEX slug_history_entity_idx ON slug_history (entity_type, entity_id);
