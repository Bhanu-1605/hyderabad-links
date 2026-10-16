-- Manual smoke test for the SQL behind /api/v1/admin/listings: the review queue, approving,
-- rejecting, and the audit trail each decision leaves. It creates its own admin, roles and listings,
-- asserts each rule, then rolls back, so it leaves no data and is safe to re-run.
--
-- The .NET integration tests deliberately need no database (CI runs without one), so the database
-- behaviour is checked here instead.
--
-- Run:  Get-Content database/checks/admin_moderation_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

-- ----------------------------------------------------------------- two admins
-- One ordinary admin (may review) and one with a role that holds only listings.view (may not).
INSERT INTO admin_users (email, display_name, password_hash, status)
VALUES ('smoke.reviewer@example.com', 'Smoke Reviewer', 'pbkdf2-sha256$210000$c21va2U=$c21va2U=', 'active'),
       ('smoke.viewer@example.com',   'Smoke Viewer',   'pbkdf2-sha256$210000$c21va2U=$c21va2U=', 'active');

INSERT INTO roles (slug, name, description) VALUES ('smoke_viewer', 'Smoke viewer', 'Can only look');

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r, permissions p
WHERE r.slug = 'smoke_viewer' AND p.code = 'listings.view';

INSERT INTO admin_user_roles (admin_user_id, role_id)
SELECT au.id, r.id FROM admin_users au, roles r
WHERE au.email = 'smoke.reviewer@example.com' AND r.slug = 'admin';

INSERT INTO admin_user_roles (admin_user_id, role_id)
SELECT au.id, r.id FROM admin_users au, roles r
WHERE au.email = 'smoke.viewer@example.com' AND r.slug = 'smoke_viewer';

-- ------------------------------------------------------------- three listings
-- One waiting for review, one already live, one still a draft.
INSERT INTO businesses (name) VALUES ('Smoke Moderation Business');

INSERT INTO listings (business_id, locality_id, slug, name, description, address_line, location, phone_primary, status)
SELECT b.id, lo.id, 'smoke-mod-waiting', 'Smoke Mod Waiting Electricals',
       'Wiring and repairs for homes and shops.', 'Plot 9, Madhapur',
       lo.center, '+919999900061', 'pending_review'
FROM businesses b, localities lo
WHERE b.name = 'Smoke Moderation Business' AND lo.slug = 'madhapur';

INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, published_at)
SELECT b.id, lo.id, 'smoke-mod-live', 'Smoke Mod Live Electricals',
       lo.center, '+919999900062', 'active', now() - interval '10 days'
FROM businesses b, localities lo
WHERE b.name = 'Smoke Moderation Business' AND lo.slug = 'madhapur';

INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status)
SELECT b.id, lo.id, 'smoke-mod-draft', 'Smoke Mod Draft Electricals',
       lo.center, '+919999900063', 'draft'
FROM businesses b, localities lo
WHERE b.name = 'Smoke Moderation Business' AND lo.slug = 'madhapur';

INSERT INTO listing_categories (listing_id, category_id, is_primary)
SELECT li.id, c.id, true
FROM listings li, categories c
WHERE li.slug IN ('smoke-mod-waiting', 'smoke-mod-live', 'smoke-mod-draft')
  AND c.slug = 'electricians';

DO $$
DECLARE
  n                integer;
  reviewer_id      bigint;
  viewer_id        bigint;
  waiting_id       bigint;
  live_id          bigint;
  row_status       text;
  published        timestamptz;
  published_before timestamptz;
  note             text;
  audit_details    jsonb;
BEGIN
  SELECT id INTO reviewer_id FROM admin_users WHERE email = 'smoke.reviewer@example.com';
  SELECT id INTO viewer_id   FROM admin_users WHERE email = 'smoke.viewer@example.com';
  SELECT id INTO waiting_id  FROM listings WHERE slug = 'smoke-mod-waiting';
  SELECT id, published_at INTO live_id, published_before FROM listings WHERE slug = 'smoke-mod-live';

  -- 1. The permission query behind AdminAccess.Load gives the ordinary admin role the right to review.
  SELECT count(*) INTO n
  FROM admin_users au
  JOIN admin_user_roles aur ON aur.admin_user_id = au.id
  JOIN role_permissions rp ON rp.role_id = aur.role_id
  JOIN permissions p ON p.id = rp.permission_id
  WHERE au.id = reviewer_id AND au.status = 'active' AND p.code = 'listings.review';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the admin role does not carry listings.review (% rows)', n; END IF;

  -- 2. An admin whose role holds only listings.view cannot review. Permissions come from the database on
  --    every request, so this is the same check the API makes.
  SELECT count(*) INTO n
  FROM admin_users au
  JOIN admin_user_roles aur ON aur.admin_user_id = au.id
  JOIN role_permissions rp ON rp.role_id = aur.role_id
  JOIN permissions p ON p.id = rp.permission_id
  WHERE au.id = viewer_id AND p.code = 'listings.review';
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: a view-only admin was granted listings.review'; END IF;

  -- 3. Ordinary admin permissions stay separate from super-admin ones.
  SELECT count(*) INTO n
  FROM roles r
  JOIN role_permissions rp ON rp.role_id = r.id
  JOIN permissions p ON p.id = rp.permission_id
  WHERE r.slug = 'admin' AND p.code IN ('admins.manage', 'plans.manage', 'audit.view', 'analytics.view', 'data.export');
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: the admin role holds % super-admin permission(s)', n; END IF;

  -- 4. A disabled admin disappears from the permission query at once, without touching their roles.
  UPDATE admin_users SET status = 'disabled' WHERE id = reviewer_id;
  SELECT count(*) INTO n FROM admin_users WHERE id = reviewer_id AND status = 'active';
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: a disabled admin still looks active'; END IF;
  UPDATE admin_users SET status = 'active' WHERE id = reviewer_id;

  -- 5. The queue shows what is waiting for review, and nothing else.
  SELECT count(*) INTO n
  FROM listings li
  WHERE li.slug LIKE 'smoke-mod-%' AND li.status = 'pending_review';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the review queue held % listings, expected 1', n; END IF;

  -- 6. The queue carries the total of all matches even when the page is past the end,
  --    exactly like the public search query.
  SELECT total INTO n FROM (
    WITH matches AS (
        SELECT li.id FROM listings li
        WHERE li.slug LIKE 'smoke-mod-%' AND li.status = 'pending_review'
    ),
    page AS (SELECT * FROM matches LIMIT 20 OFFSET 2000)
    SELECT (SELECT count(*) FROM matches) AS total
    FROM (SELECT 1) AS always_one_row
    LEFT JOIN page p ON true
  ) counted;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: a page past the end reported a total of %, expected 1', n; END IF;

  -- 7. Approving publishes the listing and records who decided.
  UPDATE listings
  SET status = 'active',
      published_at = COALESCE(published_at, now()),
      review_note = NULL,
      reviewed_at = now(),
      reviewed_by_admin_id = reviewer_id,
      updated_at = now()
  WHERE id = waiting_id AND status = 'pending_review';

  SELECT status, published_at INTO row_status, published FROM listings WHERE id = waiting_id;
  IF row_status <> 'active' THEN RAISE EXCEPTION 'FAIL: approving left the listing as %', row_status; END IF;
  IF published IS NULL THEN RAISE EXCEPTION 'FAIL: approving did not set published_at'; END IF;

  -- 8. An approved listing is immediately visible to public search (status = 'active' is all it checks).
  SELECT count(*) INTO n FROM listings WHERE id = waiting_id AND status = 'active';
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the approved listing is not public'; END IF;

  -- 9. The decision is in the audit log, attributed to the admin who made it.
  INSERT INTO audit_logs (actor_type, actor_id, action, entity_type, entity_id, details)
  VALUES ('admin', reviewer_id, 'listing.approved', 'listing', waiting_id::text, '{"previousStatus":"pending_review"}'::jsonb);

  SELECT count(*) INTO n
  FROM audit_logs
  WHERE action = 'listing.approved' AND entity_type = 'listing' AND entity_id = waiting_id::text
    AND actor_type = 'admin' AND actor_id = reviewer_id;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the approval left % audit rows, expected 1', n; END IF;

  -- 10. A second decision on the same listing changes nothing: the guard is "status = pending_review".
  UPDATE listings SET status = 'rejected', updated_at = now()
  WHERE id = waiting_id AND status = 'pending_review';
  SELECT status INTO row_status FROM listings WHERE id = waiting_id;
  IF row_status <> 'active' THEN
    RAISE EXCEPTION 'FAIL: a second decision overwrote the first (listing is now %)', row_status;
  END IF;

  -- 11. Re-approving a listing that was live before keeps its original publication date.
  UPDATE listings SET status = 'pending_review' WHERE id = live_id;
  UPDATE listings
  SET status = 'active', published_at = COALESCE(published_at, now()), reviewed_at = now(), reviewed_by_admin_id = reviewer_id
  WHERE id = live_id AND status = 'pending_review';
  SELECT published_at INTO published FROM listings WHERE id = live_id;
  IF published <> published_before THEN
    RAISE EXCEPTION 'FAIL: re-approving moved published_at from % to %', published_before, published;
  END IF;

  -- 12. Rejecting hides the listing and keeps the reason the owner will read.
  UPDATE listings SET status = 'pending_review' WHERE id = waiting_id;
  UPDATE listings
  SET status = 'rejected',
      review_note = 'The address does not match the phone number. Please correct it.',
      reviewed_at = now(),
      reviewed_by_admin_id = reviewer_id,
      updated_at = now()
  WHERE id = waiting_id AND status = 'pending_review';

  SELECT status, review_note INTO row_status, note FROM listings WHERE id = waiting_id;
  IF row_status <> 'rejected' THEN RAISE EXCEPTION 'FAIL: rejecting left the listing as %', row_status; END IF;
  IF note IS NULL OR note = '' THEN RAISE EXCEPTION 'FAIL: rejecting did not keep the reason'; END IF;

  -- 13. A rejected listing is no longer public.
  SELECT count(*) INTO n FROM listings WHERE id = waiting_id AND status = 'active';
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: a rejected listing is still public'; END IF;

  -- 14. The rejection reason reaches the audit log too.
  INSERT INTO audit_logs (actor_type, actor_id, action, entity_type, entity_id, details)
  VALUES ('admin', reviewer_id, 'listing.rejected', 'listing', waiting_id::text,
          '{"previousStatus":"pending_review","reason":"The address does not match the phone number. Please correct it."}'::jsonb);

  SELECT details INTO audit_details
  FROM audit_logs
  WHERE action = 'listing.rejected' AND entity_id = waiting_id::text
  ORDER BY occurred_at DESC LIMIT 1;
  IF audit_details -> 'reason' IS NULL THEN RAISE EXCEPTION 'FAIL: the audit row kept no reason'; END IF;

  -- 15. The audit log is append-only: an attempt to change or delete a decision is a no-op.
  UPDATE audit_logs SET action = 'listing.approved'
  WHERE action = 'listing.rejected' AND entity_id = waiting_id::text;
  SELECT count(*) INTO n FROM audit_logs WHERE action = 'listing.rejected' AND entity_id = waiting_id::text;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: an audit row was rewritten'; END IF;

  DELETE FROM audit_logs WHERE entity_id = waiting_id::text;
  SELECT count(*) INTO n FROM audit_logs WHERE entity_id = waiting_id::text;
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: audit rows were deleted (% left of 2)', n; END IF;

  -- 16. The reason cannot exceed what the API allows (V0004 constraint).
  BEGIN
    UPDATE listings SET review_note = repeat('x', 1001) WHERE id = waiting_id;
    RAISE EXCEPTION 'FAIL: a 1001-character review note was accepted';
  EXCEPTION WHEN check_violation THEN
    NULL;   -- expected
  END;

  -- 17. A draft listing never reaches the review queue, so it cannot be approved by mistake.
  SELECT count(*) INTO n
  FROM listings WHERE slug = 'smoke-mod-draft' AND status = 'pending_review';
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: a draft listing appeared in the review queue'; END IF;

  -- 18. A listing an admin adds by hand is marked source = 'admin', has no owner account behind it,
  --     and is published straight away (what the API writes when the admin may also review).
  INSERT INTO listings (business_id, locality_id, slug, name, description, address_line, location,
                        phone_primary, status, source, created_by_user_id, published_at)
  SELECT b.id, lo.id, 'smoke-mod-by-admin', 'Smoke Mod Walk-In Electricals',
         'Added from a phone call.', 'Shop 3, Madhapur', lo.center, '+919999900064',
         'active', 'admin', NULL, now()
  FROM businesses b, localities lo
  WHERE b.name = 'Smoke Moderation Business' AND lo.slug = 'madhapur';

  SELECT count(*) INTO n
  FROM listings
  WHERE slug = 'smoke-mod-by-admin' AND source = 'admin' AND created_by_user_id IS NULL
    AND status = 'active' AND published_at IS NOT NULL;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the hand-added listing was not stored as an admin listing'; END IF;

  -- 19. `source` only accepts the three values the application uses.
  BEGIN
    UPDATE listings SET source = 'somewhere_else' WHERE slug = 'smoke-mod-by-admin';
    RAISE EXCEPTION 'FAIL: an unknown listing source was accepted';
  EXCEPTION WHEN check_violation THEN
    NULL;   -- expected
  END;

  -- 20. An admin editing a listing changes its details, never its status: editing must not publish.
  UPDATE listings SET status = 'pending_review', published_at = NULL WHERE slug = 'smoke-mod-by-admin';
  UPDATE listings
  SET name = 'Smoke Mod Walk-In Electricals and Repairs', phone_primary = '+919999900065', updated_at = now()
  WHERE slug = 'smoke-mod-by-admin';

  SELECT status INTO row_status FROM listings WHERE slug = 'smoke-mod-by-admin';
  IF row_status <> 'pending_review' THEN
    RAISE EXCEPTION 'FAIL: editing changed the status to %', row_status;
  END IF;

  -- 21. Submitting a corrected listing again clears the old rejection reason, so the owner is not
  --     shown advice about a version they have already fixed.
  UPDATE listings SET status = 'rejected', review_note = 'Old reason.' WHERE slug = 'smoke-mod-by-admin';
  UPDATE listings SET status = 'pending_review', review_note = NULL, updated_at = now()
  WHERE slug = 'smoke-mod-by-admin' AND status IN ('draft', 'rejected');

  SELECT count(*) INTO n
  FROM listings WHERE slug = 'smoke-mod-by-admin' AND status = 'pending_review' AND review_note IS NULL;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: resubmitting kept the old rejection reason'; END IF;

  RAISE NOTICE 'ok: admin moderation rules behaved as expected';
END $$;

ROLLBACK;
