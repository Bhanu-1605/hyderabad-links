-- Manual smoke test for the counting behind GET /api/v1/me/overview.
-- The point that matters is isolation: one owner's dashboard must never count another owner's
-- listings. It creates two owners, gives them listings in several states, asserts the figures, then
-- rolls back, so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/owner_overview_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO users (phone_e164, phone_verified_at, name, email)
VALUES ('+919999900091', now(), 'Smoke Owner One', 'one@example.com'),
       ('+919999900092', now(), NULL, NULL);          -- second owner has no profile yet

DO $$
DECLARE
  one   bigint;
  two   bigint;
  loc   record;
  cat   integer;
  bid   bigint;
  n     integer;
  done  integer;
BEGIN
  SELECT id INTO one FROM users WHERE phone_e164 = '+919999900091';
  SELECT id INTO two FROM users WHERE phone_e164 = '+919999900092';
  SELECT id, center INTO loc FROM localities WHERE slug = 'madhapur';
  SELECT id INTO cat FROM categories WHERE slug = 'plumbers';

  -- Owner one: one active (with address and description), one pending, one draft, one rejected,
  -- and one archived that must be ignored entirely.
  INSERT INTO businesses (name) VALUES ('Smoke Overview One') RETURNING id INTO bid;
  INSERT INTO business_members (business_id, user_id, role) VALUES (bid, one, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, description, address_line, location, phone_primary, status, created_by_user_id)
  VALUES (bid, loc.id, 'smoke-ov-active',   'Smoke Ov Active',   'Full description here', 'Plot 1, Madhapur', loc.center, '+919999900091', 'active',         one),
         (bid, loc.id, 'smoke-ov-pending',  'Smoke Ov Pending',  NULL, NULL, loc.center, '+919999900091', 'pending_review', one),
         (bid, loc.id, 'smoke-ov-draft',    'Smoke Ov Draft',    NULL, NULL, loc.center, '+919999900091', 'draft',          one),
         (bid, loc.id, 'smoke-ov-rejected', 'Smoke Ov Rejected', NULL, NULL, loc.center, '+919999900091', 'rejected',       one),
         (bid, loc.id, 'smoke-ov-archived', 'Smoke Ov Archived', NULL, NULL, loc.center, '+919999900091', 'archived',       one);
  INSERT INTO listing_categories (listing_id, category_id, is_primary)
  SELECT id, cat, true FROM listings WHERE slug LIKE 'smoke-ov-%';

  -- Owner two: a single active listing of their own.
  INSERT INTO businesses (name) VALUES ('Smoke Overview Two') RETURNING id INTO bid;
  INSERT INTO business_members (business_id, user_id, role) VALUES (bid, two, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, created_by_user_id)
  VALUES (bid, loc.id, 'smoke-ov-other', 'Smoke Ov Other', loc.center, '+919999900092', 'active', two);
  INSERT INTO listing_categories (listing_id, category_id, is_primary)
  SELECT id, cat, true FROM listings WHERE slug = 'smoke-ov-other';

  -- 1. Owner one counts four listings: the archived one is excluded, and owner two's is not theirs.
  SELECT count(*) INTO n
  FROM listings li
  JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one
  WHERE li.status <> 'archived';
  IF n <> 4 THEN RAISE EXCEPTION 'FAIL: owner one counted % listings, expected 4', n; END IF;

  -- 2. Each state is counted separately.
  SELECT count(*) INTO n FROM listings li
    JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one
    WHERE li.status = 'active';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: active count was %, expected 1', n; END IF;

  SELECT count(*) INTO n FROM listings li
    JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one
    WHERE li.status = 'pending_review';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: pending count was %, expected 1', n; END IF;

  SELECT count(*) INTO n FROM listings li
    JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one
    WHERE li.status = 'rejected';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: rejected count was %, expected 1', n; END IF;

  -- 3. Owner two sees only their own single listing, never owner one's five.
  SELECT count(*) INTO n
  FROM listings li
  JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = two
  WHERE li.status <> 'archived';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: owner two counted % listings, expected 1', n; END IF;

  -- 4. The completion inputs are read per owner. Owner one has a name, an email, a listing with an
  --    address, one with a description and one live: all six steps.
  SELECT (u.name IS NOT NULL)::int
       + (btrim(coalesce(u.email, '')) <> '')::int
       + (EXISTS (SELECT 1 FROM listings li JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one WHERE li.status <> 'archived'))::int
       + (EXISTS (SELECT 1 FROM listings li JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one WHERE li.status <> 'archived' AND btrim(coalesce(li.address_line, '')) <> ''))::int
       + (EXISTS (SELECT 1 FROM listings li JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one WHERE li.status <> 'archived' AND btrim(coalesce(li.description, '')) <> ''))::int
       + (EXISTS (SELECT 1 FROM listings li JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one WHERE li.status = 'active'))::int
    INTO done
  FROM users u WHERE u.id = one;
  IF done <> 6 THEN RAISE EXCEPTION 'FAIL: owner one completed % steps, expected 6', done; END IF;

  -- 5. Owner two has no name and no email, so those two steps are not done.
  SELECT (u.name IS NOT NULL)::int + (btrim(coalesce(u.email, '')) <> '')::int INTO done
  FROM users u WHERE u.id = two;
  IF done <> 0 THEN RAISE EXCEPTION 'FAIL: owner two completed % profile steps, expected 0', done; END IF;

  RAISE NOTICE 'ok: overview counts are isolated per owner and states are counted correctly';
END $$;

ROLLBACK;
