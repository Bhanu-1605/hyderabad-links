-- Manual smoke test for the matching rules behind /api/v1/public/localities/search and
-- /api/v1/public/localities/nearest (the location autocomplete).
-- It inserts its own localities, asserts each rule, then rolls back, so it leaves no data and is
-- safe to re-run.
--
-- Run:  Get-Content database/checks/locality_search_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

-- Four areas in Hyderabad. One name starts with "Zephyr", one only contains it, one is inactive,
-- and one holds an ILIKE wildcard character so the escaping can be checked.
-- The points sit close together so the nearest-neighbour ordering is unambiguous.
INSERT INTO localities (city_id, slug, name, center, is_active)
SELECT c.id, v.slug, v.name, ST_SetSRID(ST_MakePoint(v.lng, v.lat), 4326)::geography, v.is_active
FROM cities c,
     (VALUES
        ('smoke-zephyrnagar',        'Zephyrnagar',       78.40050, 17.40000, true),
        ('smoke-new-zephyr-colony',  'New Zephyr Colony', 78.41000, 17.40000, true),
        ('smoke-inactive-zephyr',    'Zephyr Hidden',     78.40001, 17.40000, false),
        ('smoke-percent-road',       '50% Road',          78.42000, 17.40000, true)
     ) AS v(slug, name, lng, lat, is_active)
WHERE c.slug = 'hyderabad';

-- A second city, to prove the city filter keeps places with similar names apart.
INSERT INTO cities (slug, name, state, center)
VALUES ('smoke-city', 'Smoke City', 'Telangana', ST_SetSRID(ST_MakePoint(79.0, 18.0), 4326)::geography);

INSERT INTO localities (city_id, slug, name, center)
SELECT c.id, 'smoke-other-zephyr', 'Zephyr Elsewhere', ST_SetSRID(ST_MakePoint(79.001, 18.0), 4326)::geography
FROM cities c WHERE c.slug = 'smoke-city';

DO $$
DECLARE
  n          integer;
  first_name text;
  point      geography;
  metres     double precision;
BEGIN
  point := ST_SetSRID(ST_MakePoint(78.40000, 17.40000), 4326)::geography;

  -- 1. A name beginning with what was typed is ranked above one that merely contains it,
  --    and an inactive area never appears.
  SELECT name INTO first_name FROM (
      SELECT l.name
      FROM localities l
      JOIN cities c ON c.id = l.city_id
      WHERE l.is_active AND c.is_active AND c.slug = 'hyderabad'
        AND l.slug LIKE 'smoke-%'
        AND (l.name ILIKE '%zephyr%' OR l.name % 'zephyr')
      ORDER BY CASE WHEN l.name ILIKE 'zephyr%' THEN 0
                    WHEN l.name ILIKE '%zephyr%' THEN 1
                    ELSE 2 END,
               similarity(l.name, 'zephyr') DESC,
               l.name
      LIMIT 10
  ) ranked LIMIT 1;
  IF first_name <> 'Zephyrnagar' THEN
    RAISE EXCEPTION 'FAIL: top suggestion was %, expected Zephyrnagar', first_name;
  END IF;

  -- 2. Matching is case-insensitive and reaches a name's later words, but stops at inactive rows.
  SELECT count(*) INTO n
  FROM localities l
  JOIN cities c ON c.id = l.city_id
  WHERE l.is_active AND c.is_active AND c.slug = 'hyderabad'
    AND l.slug LIKE 'smoke-%'
    AND (l.name ILIKE '%ZEPHYR%' OR l.name % 'ZEPHYR');
  IF n <> 2 THEN
    RAISE EXCEPTION 'FAIL: case-insensitive match returned % rows, expected 2 (Zephyrnagar and New Zephyr Colony)', n;
  END IF;

  -- 3. Trigram similarity covers a misspelling that ILIKE alone would miss entirely.
  IF EXISTS (SELECT 1 FROM localities WHERE slug = 'smoke-zephyrnagar' AND name ILIKE '%zefyrnagar%') THEN
    RAISE EXCEPTION 'FAIL: the misspelling was supposed to be unmatchable by ILIKE';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM localities WHERE slug = 'smoke-zephyrnagar' AND name % 'zefyrnagar') THEN
    RAISE EXCEPTION 'FAIL: trigram matching did not tolerate the misspelling "zefyrnagar"';
  END IF;

  -- 4. The city filter keeps similarly named areas in other cities out.
  SELECT count(*) INTO n
  FROM localities l
  JOIN cities c ON c.id = l.city_id
  WHERE l.is_active AND c.is_active AND c.slug = 'hyderabad'
    AND l.slug LIKE 'smoke-%'
    AND (l.name ILIKE '%zephyr%' OR l.name % 'zephyr');
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: the hyderabad filter returned % rows, expected 2', n; END IF;

  SELECT count(*) INTO n
  FROM localities l
  JOIN cities c ON c.id = l.city_id
  WHERE l.is_active AND c.is_active
    AND l.slug LIKE 'smoke-%'
    AND (l.name ILIKE '%zephyr%' OR l.name % 'zephyr');
  IF n <> 3 THEN RAISE EXCEPTION 'FAIL: without a city filter % rows matched, expected 3', n; END IF;

  -- 5. A "%" typed by a visitor is escaped by the API, so it matches a literal per cent sign only
  --    and never every area in the city. (The backslash escape is the PostgreSQL default.)
  SELECT count(*) INTO n
  FROM localities l
  WHERE l.is_active AND l.slug LIKE 'smoke-%' AND l.name ILIKE '%50\%%';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the escaped per cent pattern matched % rows, expected 1', n; END IF;

  SELECT count(*) INTO n
  FROM localities l
  WHERE l.is_active AND l.slug LIKE 'smoke-%' AND l.name ILIKE '%%%';
  IF n <= 1 THEN
    RAISE EXCEPTION 'FAIL: an unescaped per cent sign was supposed to match everything, matched %', n;
  END IF;

  -- 6. The limit is honoured.
  SELECT count(*) INTO n FROM (
      SELECT l.id FROM localities l
      WHERE l.is_active AND l.slug LIKE 'smoke-%' AND (l.name ILIKE '%zephyr%' OR l.name % 'zephyr')
      LIMIT 1
  ) capped;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: limit 1 returned % rows', n; END IF;

  -- 7. Nearest resolves a point to the closest *active* area. The inactive one is 4 metres away and
  --    must still be skipped, so this fails loudly if the is_active filter is ever dropped.
  SELECT l.name, ST_Distance(l.center, point) INTO first_name, metres
  FROM localities l
  JOIN cities c ON c.id = l.city_id AND c.is_active
  WHERE l.is_active AND l.slug LIKE 'smoke-%'
  ORDER BY l.center <-> point
  LIMIT 1;
  IF first_name <> 'Zephyrnagar' THEN
    RAISE EXCEPTION 'FAIL: nearest active area was %, expected Zephyrnagar', first_name;
  END IF;

  -- 8. The distance comes back in metres, so the website can say when the nearest area is far off.
  --    0.00050 degrees of longitude at this latitude is roughly 53 metres.
  IF metres < 40 OR metres > 70 THEN
    RAISE EXCEPTION 'FAIL: distance to Zephyrnagar was % metres, expected roughly 53', round(metres);
  END IF;

  -- 9. Against the real reference data, any point in the city resolves to exactly one area.
  SELECT count(*) INTO n FROM (
      SELECT l.id
      FROM localities l
      JOIN cities c ON c.id = l.city_id AND c.is_active
      WHERE l.is_active
      ORDER BY l.center <-> ST_SetSRID(ST_MakePoint(78.4867, 17.3850), 4326)::geography
      LIMIT 1
  ) nearest;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: nearest returned % rows for a point in the city, expected 1', n; END IF;

  RAISE NOTICE 'ok: all locality lookup rules behaved as expected';
END $$;

ROLLBACK;
