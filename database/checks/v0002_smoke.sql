-- Manual smoke test for V0002. Runs inside a transaction that is ROLLED BACK, so it leaves no data behind.
-- Run:  Get-Content database/checks/v0002_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO users (phone_e164, phone_verified_at, name) VALUES ('+919999900001', now(), 'Smoke Test Owner');
INSERT INTO businesses (name) VALUES ('Smoke Test Plumbing');
INSERT INTO business_members (business_id, user_id)
SELECT b.id, u.id FROM businesses b, users u WHERE b.name = 'Smoke Test Plumbing' AND u.phone_e164 = '+919999900001';

INSERT INTO listings (business_id, locality_id, slug, name, description, location, phone_primary, status)
SELECT b.id, l.id, 'smoke-test-plumbing', 'Smoke Test Plumbing', 'Pipe leakage and bathroom fitting',
       ST_SetSRID(ST_MakePoint(78.3489, 17.4401), 4326)::geography, '+919999900002', 'active'
FROM businesses b, localities l WHERE b.name = 'Smoke Test Plumbing' AND l.slug = 'gachibowli';

INSERT INTO listing_categories (listing_id, category_id, is_primary)
SELECT li.id, c.id, true FROM listings li, categories c WHERE li.slug = 'smoke-test-plumbing' AND c.slug = 'plumbers';

-- Constraints must REJECT bad data. Each block fails the script if the bad insert is wrongly accepted.
DO $$ BEGIN
  BEGIN  -- a second primary category
    INSERT INTO listing_categories (listing_id, category_id, is_primary)
    SELECT li.id, c.id, true FROM listings li, categories c WHERE li.slug = 'smoke-test-plumbing' AND c.slug = 'electricians';
    RAISE EXCEPTION 'FAIL: second primary category was accepted';
  EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'ok: second primary rejected'; END;

  BEGIN  -- a parent (level 1) category on a listing
    INSERT INTO listing_categories (listing_id, category_id, category_level, is_primary)
    SELECT li.id, c.id, 1, false FROM listings li, categories c WHERE li.slug = 'smoke-test-plumbing' AND c.slug = 'home-services';
    RAISE EXCEPTION 'FAIL: parent category was accepted';
  EXCEPTION WHEN check_violation OR foreign_key_violation THEN RAISE NOTICE 'ok: parent category rejected'; END;

  BEGIN  -- bad phone number
    UPDATE listings SET phone_primary = '9999900002' WHERE slug = 'smoke-test-plumbing';
    RAISE EXCEPTION 'FAIL: non-E.164 phone was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: bad phone rejected'; END;

  BEGIN  -- bad slug
    UPDATE listings SET slug = 'Bad Slug' WHERE slug = 'smoke-test-plumbing';
    RAISE EXCEPTION 'FAIL: bad slug was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: bad slug rejected'; END;
END $$;

-- Nearest active plumbers to Madhapur, closest first (this is the core "near me" query).
SELECT l.name, l.slug, round(ST_Distance(l.location, p.pt))::int AS metres
FROM listings l
JOIN listing_categories lc ON lc.listing_id = l.id
JOIN categories c ON c.id = lc.category_id AND c.slug = 'plumbers',
     (SELECT ST_SetSRID(ST_MakePoint(78.3908, 17.4486), 4326)::geography AS pt) p
WHERE l.status = 'active'
ORDER BY l.location <-> p.pt
LIMIT 10;

-- Typo-tolerant name search ("plmbing" should still find it) and full-text search.
SELECT name FROM listings WHERE name % 'Smoke Test Plmbing';
SELECT name FROM listings WHERE search_vector @@ to_tsquery('simple', 'pipe');

ROLLBACK;
