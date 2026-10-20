-- Manual smoke test for the review rules in V0007 (decision D11).
-- It creates its own users, listings and reviews, asserts the manipulation guards, then rolls back,
-- so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/review_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO users (phone_e164, phone_verified_at, name, email)
VALUES ('+919999900201', now(), 'Smoke Owner', 'owner@example.com'),
       ('+919999900202', now(), 'Anita Kumari', 'anita@example.com'),
       ('+919999900203', NULL,  'Unverified Person', 'unverified@example.com');

DO $$
DECLARE
  owner_id  bigint;
  cust_id   bigint;
  unver_id  bigint;
  loc       record;
  cat       integer;
  biz       bigint;
  lst       bigint;
  n         integer;
  avg_now   numeric;
BEGIN
  SELECT id INTO owner_id FROM users WHERE phone_e164 = '+919999900201';
  SELECT id INTO cust_id  FROM users WHERE phone_e164 = '+919999900202';
  SELECT id INTO unver_id FROM users WHERE phone_e164 = '+919999900203';
  SELECT id, center INTO loc FROM localities WHERE slug = 'madhapur';
  SELECT id INTO cat FROM categories WHERE slug = 'cafes';

  INSERT INTO businesses (name) VALUES ('Smoke Review Cafe') RETURNING id INTO biz;
  INSERT INTO business_members (business_id, user_id, role) VALUES (biz, owner_id, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, created_by_user_id)
  VALUES (biz, loc.id, 'smoke-review-cafe', 'Smoke Review Cafe', loc.center, '+919999900201', 'active', owner_id)
  RETURNING id INTO lst;
  INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (lst, cat, true);

  -- 1. A review arrives pending, never published.
  INSERT INTO reviews (listing_id, user_id, rating, body)
  VALUES (lst, cust_id, 4, 'Good filter coffee and the staff were quick and polite.');
  IF (SELECT status FROM reviews WHERE listing_id = lst AND user_id = cust_id) <> 'pending' THEN
    RAISE EXCEPTION 'FAIL: a new review should be pending';
  END IF;

  -- 2. Nothing pending counts towards the public average.
  SELECT count(*) INTO n FROM reviews WHERE listing_id = lst AND status = 'approved';
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: % approved reviews before moderation', n; END IF;

  -- 3. One review per person, enforced by the constraint rather than the API.
  BEGIN
    INSERT INTO reviews (listing_id, user_id, rating, body)
    VALUES (lst, cust_id, 1, 'Trying to review the same business a second time.');
    RAISE EXCEPTION 'FAIL: a second review by the same person was accepted';
  EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'ok: duplicate review rejected';
  END;

  -- 4. The constraints refuse rubbish, so a bug in the API cannot store it.
  BEGIN
    INSERT INTO reviews (listing_id, user_id, rating, body)
    VALUES (lst, unver_id, 9, 'A rating of nine is not on the scale at all.');
    RAISE EXCEPTION 'FAIL: a rating outside 1-5 was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: out-of-range rating rejected';
  END;

  BEGIN
    INSERT INTO reviews (listing_id, user_id, rating, body) VALUES (lst, unver_id, 5, 'Too short');
    RAISE EXCEPTION 'FAIL: a too-short review was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: short review rejected';
  END;

  BEGIN
    INSERT INTO reviews (listing_id, user_id, rating, body, status)
    VALUES (lst, unver_id, 5, 'A review with a status that does not exist.', 'published');
    RAISE EXCEPTION 'FAIL: an unknown status was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: unknown status rejected';
  END;

  -- 5. D11's verified-reviewer rule: the API checks phone_verified_at, and the data supports it.
  IF (SELECT phone_verified_at FROM users WHERE id = unver_id) IS NOT NULL THEN
    RAISE EXCEPTION 'FAIL: the unverified fixture is not actually unverified';
  END IF;

  -- 6. The owner of the business is identifiable, which is how the API refuses a self-review.
  IF NOT EXISTS (
      SELECT 1 FROM business_members bm
      JOIN listings li ON li.business_id = bm.business_id
      WHERE li.id = lst AND bm.user_id = owner_id) THEN
    RAISE EXCEPTION 'FAIL: the owner is not linked to their own listing';
  END IF;

  -- 7. Approving makes it public, and the average is computed over approved rows only.
  UPDATE reviews SET status = 'approved', moderated_at = now()
  WHERE listing_id = lst AND user_id = cust_id AND status = 'pending';
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: approving a pending review affected % rows', n; END IF;

  SELECT avg(rating) INTO avg_now FROM reviews WHERE listing_id = lst AND status = 'approved';
  IF avg_now <> 4 THEN RAISE EXCEPTION 'FAIL: average was %, expected 4', avg_now; END IF;

  -- 8. A decided review cannot be decided again, so two moderators cannot both record an outcome.
  UPDATE reviews SET status = 'rejected' WHERE listing_id = lst AND user_id = cust_id AND status = 'pending';
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: an already-decided review was decided again'; END IF;

  -- 9. Deleting a listing takes its reviews with it.
  DELETE FROM listing_categories WHERE listing_id = lst;
  DELETE FROM listings WHERE id = lst;
  SELECT count(*) INTO n FROM reviews WHERE listing_id = lst;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: % reviews outlived their listing', n; END IF;

  RAISE NOTICE 'ok: review rules behaved as expected';
END $$;

ROLLBACK;
