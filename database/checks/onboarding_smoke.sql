-- Manual smoke test for the database rules behind owner onboarding (decision D24).
-- It creates its own user, registers them the way POST /api/v1/me/onboarding does, asserts the
-- invariants that endpoint depends on, then rolls back, so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/onboarding_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

-- A phone that has just verified an OTP: the row exists, but there is no profile yet.
INSERT INTO users (phone_e164, phone_verified_at) VALUES ('+919999900081', now());

DO $$
DECLARE
  uid          bigint;
  bid          bigint;
  lid          bigint;
  loc          record;
  cat_id       integer;
  owner_phone  text;
  n            integer;
BEGIN
  SELECT id, phone_e164 INTO uid, owner_phone FROM users WHERE phone_e164 = '+919999900081';
  SELECT id, center INTO loc FROM localities WHERE slug = 'kukatpally';
  SELECT id INTO cat_id FROM categories WHERE slug = 'plumbers';

  -- 1. A freshly verified user has no name, which is what "needs onboarding" means.
  IF (SELECT name FROM users WHERE id = uid) IS NOT NULL THEN
    RAISE EXCEPTION 'FAIL: a new user should have no name';
  END IF;

  -- 2. The listing is built only from data onboarding actually has: the chosen locality's centre
  --    point and the verified phone number. Nothing is invented.
  INSERT INTO businesses (name) VALUES ('Smoke Onboarding Plumbing') RETURNING id INTO bid;
  INSERT INTO business_members (business_id, user_id, role) VALUES (bid, uid, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, source, created_by_user_id)
  VALUES (bid, loc.id, 'smoke-onboarding-plumbing', 'Smoke Onboarding Plumbing',
          loc.center, owner_phone, 'draft', 'owner', uid)
  RETURNING id INTO lid;
  INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (lid, cat_id, true);

  -- 3. The profile update is guarded by `name IS NULL`, so it applies exactly once.
  UPDATE users SET name = 'Ramesh Kumar', email = 'ramesh@example.com' WHERE id = uid AND name IS NULL;
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the first profile update affected % rows, expected 1', n; END IF;

  -- 4. A repeated request cannot overwrite the profile that is already there.
  UPDATE users SET name = 'Someone Else', email = 'other@example.com' WHERE id = uid AND name IS NULL;
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: a repeat overwrote the profile (% rows)', n; END IF;
  IF (SELECT name FROM users WHERE id = uid) <> 'Ramesh Kumar' THEN
    RAISE EXCEPTION 'FAIL: the name was changed by the repeat';
  END IF;

  -- 5. Ownership cannot be recorded twice for the same person and business.
  BEGIN
    INSERT INTO business_members (business_id, user_id, role) VALUES (bid, uid, 'owner');
    RAISE EXCEPTION 'FAIL: duplicate ownership row was accepted';
  EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'ok: duplicate ownership rejected';
  END;

  -- 6. The first listing is a draft owned by this user, and a draft is not public.
  SELECT count(*) INTO n FROM listings WHERE id = lid AND status = 'draft' AND source = 'owner' AND created_by_user_id = uid;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the first listing is not an owner draft'; END IF;

  SELECT count(*) INTO n FROM listings WHERE id = lid AND status = 'active';
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: a newly registered listing must not be public'; END IF;

  -- 7. The listing sits at the locality centre and carries the verified number.
  IF NOT EXISTS (
      SELECT 1 FROM listings li
      WHERE li.id = lid AND li.phone_primary = owner_phone AND ST_Equals(li.location::geometry, loc.center::geometry)) THEN
    RAISE EXCEPTION 'FAIL: the listing location or phone did not come from the expected source';
  END IF;

  -- 8. Exactly one business and one listing for this owner.
  SELECT count(*) INTO n FROM business_members WHERE user_id = uid;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: owner has % businesses, expected 1', n; END IF;
  SELECT count(*) INTO n FROM listings WHERE created_by_user_id = uid;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: owner has % listings, expected 1', n; END IF;

  RAISE NOTICE 'ok: onboarding rules behaved as expected';
END $$;

ROLLBACK;
