-- Manual smoke test for the matching rules behind /api/v1/public/listings/search.
-- It inserts its own listings, asserts each way a visitor can find them, then rolls back,
-- so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/search_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

-- Two listings in the same category: one in Madhapur, one far away in Uppal that also
-- declares Madhapur as a service area.
INSERT INTO businesses (name) VALUES ('Smoke Search Business');

INSERT INTO listings (business_id, locality_id, slug, name, description, location, phone_primary, status)
SELECT b.id, lo.id, 'smoke-zephyr-pipeworks', 'Smoke Zephyr Pipeworks',
       'Emergency pipe and tap work across the city.',
       lo.center, '+919999900051', 'active'
FROM businesses b, localities lo
WHERE b.name = 'Smoke Search Business' AND lo.slug = 'madhapur';

INSERT INTO listings (business_id, locality_id, slug, name, description, location, phone_primary, status)
SELECT b.id, lo.id, 'smoke-faraway-drains', 'Smoke Faraway Drains',
       'Drain clearing.', lo.center, '+919999900052', 'active'
FROM businesses b, localities lo
WHERE b.name = 'Smoke Search Business' AND lo.slug = 'uppal';

-- A draft listing must never appear in public search results.
INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status)
SELECT b.id, lo.id, 'smoke-hidden-zephyr', 'Smoke Hidden Zephyr Pipeworks',
       lo.center, '+919999900053', 'draft'
FROM businesses b, localities lo
WHERE b.name = 'Smoke Search Business' AND lo.slug = 'madhapur';

INSERT INTO listing_categories (listing_id, category_id, is_primary)
SELECT li.id, c.id, true
FROM listings li, categories c
WHERE li.slug IN ('smoke-zephyr-pipeworks', 'smoke-faraway-drains', 'smoke-hidden-zephyr')
  AND c.slug = 'plumbers';

-- The Uppal listing serves Madhapur too.
INSERT INTO listing_service_areas (listing_id, locality_id)
SELECT li.id, lo.id FROM listings li, localities lo
WHERE li.slug = 'smoke-faraway-drains' AND lo.slug = 'madhapur';

DO $$
DECLARE
  n           integer;
  point       geography;
  first_slug  text;
BEGIN
  SELECT center INTO point FROM localities WHERE slug = 'madhapur';

  -- 1. Full-text search on the name finds the listing, and ignores the draft one.
  SELECT count(*) INTO n
  FROM listings li
  WHERE li.status = 'active'
    AND li.slug LIKE 'smoke-%'
    AND li.search_vector @@ websearch_to_tsquery('simple', 'Zephyr');
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: name search matched % rows, expected 1', n; END IF;

  -- 2. Full-text search reaches the description as well.
  SELECT count(*) INTO n
  FROM listings li
  WHERE li.status = 'active'
    AND li.slug LIKE 'smoke-%'
    AND li.search_vector @@ websearch_to_tsquery('simple', 'Emergency');
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: description search matched % rows, expected 1', n; END IF;

  -- 3. pg_trgm covers a misspelling that full-text search alone would miss.
  IF NOT EXISTS (SELECT 1 FROM listings WHERE slug = 'smoke-zephyr-pipeworks' AND name % 'Zephyr Pipewroks') THEN
    RAISE EXCEPTION 'FAIL: trigram matching did not tolerate a misspelled name';
  END IF;

  -- 4. A service word from the category reaches the listing even though the word
  --    appears nowhere in its own text.
  SELECT count(*) INTO n
  FROM listings li
  WHERE li.status = 'active'
    AND li.slug LIKE 'smoke-%'
    AND EXISTS (SELECT 1
                FROM listing_categories kc
                JOIN categories kcat ON kcat.id = kc.category_id
                WHERE kc.listing_id = li.id AND 'tap repair' ILIKE ANY (kcat.search_terms));
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: service-word search matched % rows, expected 2', n; END IF;

  -- 5. A parent category slug matches listings filed under its sub-categories.
  SELECT count(*) INTO n
  FROM listings li
  WHERE li.status = 'active'
    AND li.slug LIKE 'smoke-%'
    AND EXISTS (SELECT 1
                FROM listing_categories fc
                JOIN categories fcat ON fcat.id = fc.category_id
                LEFT JOIN categories fparent ON fparent.id = fcat.parent_id
                WHERE fc.listing_id = li.id
                  AND (fcat.slug = 'home-services' OR fparent.slug = 'home-services'));
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: parent-category search matched % rows, expected 2', n; END IF;

  -- 6. The locality filter covers a listing's own area and every area it serves.
  SELECT count(*) INTO n
  FROM listings li
  JOIN localities lo ON lo.id = li.locality_id
  WHERE li.status = 'active'
    AND li.slug LIKE 'smoke-%'
    AND (lo.slug = 'madhapur'
         OR EXISTS (SELECT 1
                    FROM listing_service_areas sa
                    JOIN localities sl ON sl.id = sa.locality_id
                    WHERE sa.listing_id = li.id AND sl.slug = 'madhapur'));
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: locality filter matched % rows, expected 2 (own area plus service area)', n; END IF;

  -- 7. The proximity window keeps the distant listing out.
  SELECT count(*) INTO n
  FROM listings li
  WHERE li.status = 'active'
    AND li.slug LIKE 'smoke-%'
    AND ST_DWithin(li.location, point, 8000);
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: 8 km window matched % rows, expected 1', n; END IF;

  -- 8. The total must count every match, not just the rows on the page.
  SELECT total INTO n FROM (
    WITH matches AS (
        SELECT li.id FROM listings li
        WHERE li.status = 'active' AND li.slug LIKE 'smoke-%'
    ),
    page AS (SELECT * FROM matches LIMIT 1 OFFSET 0)
    SELECT (SELECT count(*) FROM matches) AS total
    FROM (SELECT 1) AS always_one_row
    LEFT JOIN page p ON true
  ) counted;
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: paged total was %, expected 2', n; END IF;

  -- 9. A page past the end still reports the total, so a client can recover.
  SELECT total INTO n FROM (
    WITH matches AS (
        SELECT li.id FROM listings li
        WHERE li.status = 'active' AND li.slug LIKE 'smoke-%'
    ),
    page AS (SELECT * FROM matches LIMIT 20 OFFSET 2000)
    SELECT (SELECT count(*) FROM matches) AS total
    FROM (SELECT 1) AS always_one_row
    LEFT JOIN page p ON true
  ) counted;
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: empty page reported a total of %, expected 2', n; END IF;

  -- 10. The requested order must survive the join that carries the total. Ordering only inside
  --     the CTE returns the right rows in an arbitrary order, so the final SELECT orders too.
  SELECT slug INTO first_slug FROM (
    WITH matches AS (
        SELECT li.slug, ST_Distance(li.location, point) AS distance_metres
        FROM listings li
        WHERE li.status = 'active' AND li.slug LIKE 'smoke-%'
    ),
    page AS (SELECT * FROM matches ORDER BY distance_metres ASC NULLS LAST, slug ASC LIMIT 20 OFFSET 0)
    SELECT p.slug
    FROM (SELECT 1) AS always_one_row
    LEFT JOIN page p ON true
    ORDER BY p.distance_metres ASC NULLS LAST, p.slug ASC
  ) ordered LIMIT 1;
  IF first_slug <> 'smoke-zephyr-pipeworks' THEN
    RAISE EXCEPTION 'FAIL: nearest listing was %, expected smoke-zephyr-pipeworks', first_slug;
  END IF;

  RAISE NOTICE 'ok: all search matching rules behaved as expected';
END $$;

ROLLBACK;
