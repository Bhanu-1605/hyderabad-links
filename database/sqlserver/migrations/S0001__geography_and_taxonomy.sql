-- S0001: geography (cities, localities), taxonomy (categories), slug history.
-- The SQL Server counterpart of PostgreSQL V0001. See README.md in this folder for the type and
-- syntax conventions, and why each differs.
--
-- No extensions: spatial and full-text search are part of the engine, so there is nothing to
-- allow-list the way Azure PostgreSQL needed `azure.extensions`.

-- Spatial and filtered indexes require these to be ON, and a client that leaves either OFF makes
-- the index creation fail rather than silently differ. Microsoft.Data.SqlClient has them ON already;
-- sqlcmd does not, so they are set here and the file is correct whichever applies it.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- A slug is lower case, digits and single hyphens, not starting or ending with one. PostgreSQL said
-- that in one regex. SQL Server has no regex in a check constraint, so it is spelled out -- and over
-- a case-sensitive collation, because the database's own collation is case-insensitive and would
-- otherwise accept 'Abids' as a valid slug.

-- ---------------------------------------------------------------- cities
CREATE TABLE cities (
    id            int IDENTITY(1,1) NOT NULL CONSTRAINT cities_pkey PRIMARY KEY,
    slug          nvarchar(120) NOT NULL,
    name          nvarchar(200) NOT NULL,
    state         nvarchar(120) NOT NULL,
    country_code  char(2) NOT NULL CONSTRAINT cities_country_default DEFAULT 'IN',
    center        geography NULL,
    is_active     bit NOT NULL CONSTRAINT cities_is_active_default DEFAULT 1,
    created_at    datetimeoffset(3) NOT NULL CONSTRAINT cities_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at    datetimeoffset(3) NOT NULL CONSTRAINT cities_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT cities_slug_key UNIQUE (slug),
    CONSTRAINT cities_slug_format CHECK (
        LEN(slug) > 0
        AND slug COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9-]%'
        AND slug NOT LIKE '-%' AND slug NOT LIKE '%-' AND slug NOT LIKE '%--%'
    )
);

-- ------------------------------------------------------------- localities
CREATE TABLE localities (
    id          int IDENTITY(1,1) NOT NULL CONSTRAINT localities_pkey PRIMARY KEY,
    city_id     int NOT NULL CONSTRAINT localities_city_fk REFERENCES cities (id),
    slug        nvarchar(120) NOT NULL,
    name        nvarchar(200) NOT NULL,
    center      geography NOT NULL,
    pincode     nvarchar(10) NULL,
    is_active   bit NOT NULL CONSTRAINT localities_is_active_default DEFAULT 1,
    created_at  datetimeoffset(3) NOT NULL CONSTRAINT localities_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at  datetimeoffset(3) NOT NULL CONSTRAINT localities_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT localities_city_slug_key UNIQUE (city_id, slug),
    CONSTRAINT localities_slug_format CHECK (
        LEN(slug) > 0
        AND slug COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9-]%'
        AND slug NOT LIKE '-%' AND slug NOT LIKE '%-' AND slug NOT LIKE '%--%'
    )
);

/* A spatial index needs a bounding box, which PostGIS's GiST index did not. These are the corners
   of Telangana with room to spare: a point outside the box is still stored and still found by an
   exact distance test, it simply does not benefit from the index. Widen it when a second state
   is added. */
CREATE SPATIAL INDEX localities_center_gix ON localities (center)
    USING GEOGRAPHY_GRID WITH (GRIDS = (MEDIUM, MEDIUM, MEDIUM, MEDIUM), CELLS_PER_OBJECT = 16);

-- ------------------------------------------------------------- categories
-- Two-level tree, enforced by the database itself (no trigger):
--   level 1 = parent (no parent), level 2 = child whose parent must be a level-1 row.
-- Slugs are globally unique across both levels (decision D4).
CREATE TABLE categories (
    id            int IDENTITY(1,1) NOT NULL CONSTRAINT categories_pkey PRIMARY KEY,
    level         smallint NOT NULL,
    parent_id     int NULL,
    parent_level  smallint NULL,
    slug          nvarchar(120) NOT NULL,
    name          nvarchar(200) NOT NULL,
    sort_order    int NOT NULL CONSTRAINT categories_sort_default DEFAULT 0,
    is_active     bit NOT NULL CONSTRAINT categories_is_active_default DEFAULT 1,
    created_at    datetimeoffset(3) NOT NULL CONSTRAINT categories_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at    datetimeoffset(3) NOT NULL CONSTRAINT categories_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT categories_slug_key UNIQUE (slug),
    CONSTRAINT categories_slug_format CHECK (
        LEN(slug) > 0
        AND slug COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9-]%'
        AND slug NOT LIKE '-%' AND slug NOT LIKE '%-' AND slug NOT LIKE '%--%'
    ),
    CONSTRAINT categories_id_level_key UNIQUE (id, level),
    CONSTRAINT categories_parent_fk FOREIGN KEY (parent_id, parent_level) REFERENCES categories (id, level),
    CONSTRAINT categories_two_levels CHECK (
        (level = 1 AND parent_id IS NULL AND parent_level IS NULL)
        OR (level = 2 AND parent_id IS NOT NULL AND parent_level = 1)
    )
);
CREATE INDEX categories_parent_idx ON categories (parent_id);

/* The words a category is also known by, which PostgreSQL held in a `text[]` column. SQL Server has
   no array type, and a child table indexes and joins properly instead of needing an array operator:
   "does any search term match what was typed" becomes an ordinary EXISTS. */
CREATE TABLE category_search_terms (
    category_id  int NOT NULL CONSTRAINT category_search_terms_category_fk
                     REFERENCES categories (id) ON DELETE CASCADE,
    term         nvarchar(200) NOT NULL,
    CONSTRAINT category_search_terms_pkey PRIMARY KEY (category_id, term),
    CONSTRAINT category_search_terms_not_blank CHECK (LEN(LTRIM(RTRIM(term))) > 0)
);
-- Looking a typed word up across every category.
CREATE INDEX category_search_terms_term_idx ON category_search_terms (term) INCLUDE (category_id);

-- ----------------------------------------------------------- slug history
-- Old slugs kept so changed URLs can 301 to the new one. scope_key narrows uniqueness
-- (for example the locality id for a listing slug); use an empty string when not needed.
CREATE TABLE slug_history (
    id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT slug_history_pkey PRIMARY KEY,
    entity_type  nvarchar(20) NOT NULL,
    entity_id    bigint NOT NULL,
    scope_key    nvarchar(120) NOT NULL CONSTRAINT slug_history_scope_default DEFAULT '',
    old_slug     nvarchar(120) NOT NULL,
    created_at   datetimeoffset(3) NOT NULL CONSTRAINT slug_history_created_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT slug_history_entity_type_check CHECK (entity_type IN ('city', 'locality', 'category', 'listing')),
    CONSTRAINT slug_history_unique UNIQUE (entity_type, scope_key, old_slug)
);
CREATE INDEX slug_history_entity_idx ON slug_history (entity_type, entity_id);
