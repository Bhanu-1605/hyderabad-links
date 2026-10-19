-- Manual smoke test for the enquiry rules in V0006.
-- It creates its own owners, listings and enquiries, asserts the isolation and constraint rules,
-- then rolls back, so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/enquiry_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO users (phone_e164, phone_verified_at, name, email)
VALUES ('+919999900101', now(), 'Smoke Owner One', 'one@example.com'),
       ('+919999900102', now(), 'Smoke Owner Two', 'two@example.com');

DO $$
DECLARE
  one   bigint;
  two   bigint;
  loc   record;
  cat   integer;
  b1    bigint;
  b2    bigint;
  l1    bigint;
  l2    bigint;
  n     integer;
BEGIN
  SELECT id INTO one FROM users WHERE phone_e164 = '+919999900101';
  SELECT id INTO two FROM users WHERE phone_e164 = '+919999900102';
  SELECT id, center INTO loc FROM localities WHERE slug = 'madhapur';
  SELECT id INTO cat FROM categories WHERE slug = 'plumbers';

  INSERT INTO businesses (name) VALUES ('Smoke Enquiry One') RETURNING id INTO b1;
  INSERT INTO business_members (business_id, user_id, role) VALUES (b1, one, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, created_by_user_id)
  VALUES (b1, loc.id, 'smoke-enq-one', 'Smoke Enq One', loc.center, '+919999900101', 'active', one)
  RETURNING id INTO l1;
  INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (l1, cat, true);

  INSERT INTO businesses (name) VALUES ('Smoke Enquiry Two') RETURNING id INTO b2;
  INSERT INTO business_members (business_id, user_id, role) VALUES (b2, two, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, created_by_user_id)
  VALUES (b2, loc.id, 'smoke-enq-two', 'Smoke Enq Two', loc.center, '+919999900102', 'active', two)
  RETURNING id INTO l2;
  INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (l2, cat, true);

  INSERT INTO enquiries (listing_id, name, phone_e164, message)
  VALUES (l1, 'Customer A', '+919876500001', 'Please call me about a leaking tap today.'),
         (l1, 'Customer B', '+919876500002', 'Do you work on Sundays at all? Need a quote.'),
         (l2, 'Customer C', '+919876500003', 'Looking for a quote on bathroom fitting.');

  -- 1. An enquiry arrives unread.
  SELECT count(*) INTO n FROM enquiries WHERE listing_id = l1 AND status = 'new';
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: expected 2 new enquiries for owner one, found %', n; END IF;

  -- 2. Isolation: each owner sees only their own, through business_members.
  SELECT count(*) INTO n
  FROM enquiries e
  JOIN listings li ON li.id = e.listing_id
  JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = one;
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: owner one saw % enquiries, expected 2', n; END IF;

  SELECT count(*) INTO n
  FROM enquiries e
  JOIN listings li ON li.id = e.listing_id
  JOIN business_members bm ON bm.business_id = li.business_id AND bm.user_id = two;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: owner two saw % enquiries, expected 1', n; END IF;

  -- 3. The ownership-scoped UPDATE the endpoint runs matches nothing for the wrong owner.
  UPDATE enquiries e SET status = 'read', updated_at = now()
  FROM listings li
  JOIN business_members bm ON bm.business_id = li.business_id
  WHERE e.listing_id = l1 AND li.id = e.listing_id AND bm.user_id = two;
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: owner two changed % of owner one''s enquiries', n; END IF;

  -- 4. And it does match for the right one.
  UPDATE enquiries e SET status = 'replied', updated_at = now()
  FROM listings li
  JOIN business_members bm ON bm.business_id = li.business_id
  WHERE e.listing_id = l1 AND li.id = e.listing_id AND bm.user_id = one;
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: owner one changed % of their own enquiries, expected 2', n; END IF;

  -- 5. The constraints refuse rubbish, so a bug in the API cannot store it.
  BEGIN
    INSERT INTO enquiries (listing_id, name, phone_e164, message)
    VALUES (l1, 'Too Short', '+919876500004', 'hi');
    RAISE EXCEPTION 'FAIL: a too-short message was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: short message rejected';
  END;

  BEGIN
    INSERT INTO enquiries (listing_id, name, phone_e164, message)
    VALUES (l1, 'Bad Phone', '98765', 'This message is long enough to pass.');
    RAISE EXCEPTION 'FAIL: a non-E.164 phone number was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: bad phone rejected';
  END;

  BEGIN
    INSERT INTO enquiries (listing_id, name, phone_e164, email, message)
    VALUES (l1, 'Upper Email', '+919876500005', 'Mixed@Example.com', 'This message is long enough.');
    RAISE EXCEPTION 'FAIL: a mixed-case email was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: mixed-case email rejected';
  END;

  BEGIN
    INSERT INTO enquiries (listing_id, name, phone_e164, message, status)
    VALUES (l1, 'Bad Status', '+919876500006', 'This message is long enough.', 'archived');
    RAISE EXCEPTION 'FAIL: an unknown status was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: unknown status rejected';
  END;

  -- 6. Deleting a listing takes its enquiries with it, so no orphaned customer data is left.
  DELETE FROM listing_categories WHERE listing_id = l2;
  DELETE FROM listings WHERE id = l2;
  SELECT count(*) INTO n FROM enquiries WHERE listing_id = l2;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: % enquiries outlived their listing', n; END IF;

  RAISE NOTICE 'ok: enquiry rules behaved as expected';
END $$;

ROLLBACK;
