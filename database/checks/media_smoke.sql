-- Manual smoke test for the plans and listing media added in V0008.
-- It creates its own user, business, listing and media rows, asserts the storage rules, then rolls
-- back, so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/media_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO users (phone_e164, phone_verified_at, name, email)
VALUES ('+919999900401', now(), 'Smoke Media Owner', 'media-owner@example.com');

DO $$
DECLARE
  owner_id bigint;
  loc      record;
  cat      integer;
  biz      bigint;
  lst      bigint;
  n        integer;
  key      text;
BEGIN
  SELECT id INTO owner_id FROM users WHERE phone_e164 = '+919999900401';
  SELECT id, center INTO loc FROM localities WHERE slug = 'madhapur';
  SELECT id INTO cat FROM categories WHERE slug = 'cafes';

  INSERT INTO businesses (name) VALUES ('Smoke Media Cafe') RETURNING id INTO biz;
  INSERT INTO business_members (business_id, user_id, role) VALUES (biz, owner_id, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, created_by_user_id)
  VALUES (biz, loc.id, 'smoke-media-cafe', 'Smoke Media Cafe', loc.center, '+919999900401', 'active', owner_id)
  RETURNING id INTO lst;
  INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (lst, cat, true);

  -- 1. A new listing is on the free plan, which costs nothing and includes no media.
  IF (SELECT plan_slug FROM listings WHERE id = lst) <> 'free' THEN
    RAISE EXCEPTION 'FAIL: a new listing should default to the free plan';
  END IF;
  IF (SELECT photo_limit + video_limit + logo_limit FROM plans WHERE slug = 'free') <> 0 THEN
    RAISE EXCEPTION 'FAIL: the free plan should include no media';
  END IF;

  -- 2. The four plans exist, are priced in paise and are ordered for display.
  SELECT count(*) INTO n FROM plans WHERE slug IN ('free', 'basic', 'advanced', 'premium');
  IF n <> 4 THEN RAISE EXCEPTION 'FAIL: expected four plans, found %', n; END IF;
  IF (SELECT count(DISTINCT sort_order) FROM plans) <> (SELECT count(*) FROM plans) THEN
    RAISE EXCEPTION 'FAIL: two plans share a sort order';
  END IF;

  -- 3. No plan grants verification. requirements.md says a paid plan must never do so automatically,
  --    so this is a rule about the data, not only about the code that reads it.
  SELECT count(*) INTO n FROM plans WHERE grants_verified_badge;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: % plan(s) grant a verified badge', n; END IF;

  -- 4. The media allowances are still marked provisional, so the UI can say so.
  SELECT count(*) INTO n FROM plans WHERE media_limits_confirmed;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: % plan(s) claim confirmed allowances the owner has not approved', n; END IF;

  -- 5. A listing cannot be put on a plan that does not exist.
  BEGIN
    UPDATE listings SET plan_slug = 'platinum' WHERE id = lst;
    RAISE EXCEPTION 'FAIL: a made-up plan was accepted';
  EXCEPTION WHEN foreign_key_violation THEN RAISE NOTICE 'ok: unknown plan rejected';
  END;

  UPDATE listings SET plan_slug = 'advanced' WHERE id = lst;

  -- 6. Media rows record what was stored, and the storage key is unique across the whole table:
  --    two listings can never end up pointing at the same blob.
  INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, width, height, created_by_user_id)
  VALUES (lst, 'photo', 'listings/x/photo/aaa.jpg', 'image/jpeg', 12345, 800, 600, owner_id)
  RETURNING storage_key INTO key;

  BEGIN
    INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, created_by_user_id)
    VALUES (lst, 'photo', key, 'image/jpeg', 999, owner_id);
    RAISE EXCEPTION 'FAIL: the same storage key was accepted twice';
  EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'ok: duplicate storage key rejected';
  END;

  -- 7. The constraints refuse rubbish, so a bug in the API cannot store it.
  BEGIN
    INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, created_by_user_id)
    VALUES (lst, 'banner', 'listings/x/banner/a.jpg', 'image/jpeg', 10, owner_id);
    RAISE EXCEPTION 'FAIL: an unknown media kind was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: unknown kind rejected';
  END;

  BEGIN
    INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, created_by_user_id)
    VALUES (lst, 'photo', 'listings/x/photo/empty.jpg', 'image/jpeg', 0, owner_id);
    RAISE EXCEPTION 'FAIL: an empty file was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: zero-byte file rejected';
  END;

  BEGIN
    -- Half a size is no size: either both dimensions are known or neither is.
    INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, width, created_by_user_id)
    VALUES (lst, 'photo', 'listings/x/photo/half.jpg', 'image/jpeg', 10, 800, owner_id);
    RAISE EXCEPTION 'FAIL: a width without a height was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: half a dimension pair rejected';
  END;

  -- 8. A video legitimately has no dimensions: nothing reads them out of the container yet.
  INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, created_by_user_id)
  VALUES (lst, 'video', 'listings/x/video/clip.mp4', 'video/mp4', 4000000, owner_id);

  -- 9. The quota count is over visible media only, which is what makes a downgrade non-destructive
  --    (decision D10): hiding a photo frees the slot without deleting anybody's file.
  INSERT INTO listing_media (listing_id, kind, storage_key, content_type, byte_size, is_hidden, created_by_user_id)
  VALUES (lst, 'photo', 'listings/x/photo/hidden.jpg', 'image/jpeg', 2222, true, owner_id);

  SELECT count(*) INTO n FROM listing_media WHERE listing_id = lst AND kind = 'photo' AND NOT is_hidden;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: expected 1 visible photo, counted %', n; END IF;
  SELECT count(*) INTO n FROM listing_media WHERE listing_id = lst AND kind = 'photo';
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: the hidden photo should still exist, found % photos', n; END IF;

  -- 10. Deleting a listing takes its media rows with it, so nothing is left pointing at a listing
  --     that is gone. (Removing the blobs themselves is the API's job.)
  DELETE FROM listing_categories WHERE listing_id = lst;
  DELETE FROM listings WHERE id = lst;
  SELECT count(*) INTO n FROM listing_media WHERE listing_id = lst;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: % media rows outlived their listing', n; END IF;

  RAISE NOTICE 'ok: plan and media rules behaved as expected';
END $$;

ROLLBACK;
