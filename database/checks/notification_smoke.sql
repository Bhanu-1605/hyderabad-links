-- Manual smoke test for the notification rules in V0010.
-- It creates its own owner, listing, enquiry and notification rows, asserts the constraints and
-- indexes behave, then rolls back, so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/notification_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO users (phone_e164, phone_verified_at, name, email)
VALUES ('+919999900301', now(), 'Smoke Notify Owner', 'notify.owner@example.com');

DO $$
DECLARE
  owner_id bigint;
  loc      record;
  cat      integer;
  biz      bigint;
  listing  bigint;
  enquiry  bigint;
  note     bigint;
  n        integer;
  t        timestamptz;
BEGIN
  SELECT id INTO owner_id FROM users WHERE phone_e164 = '+919999900301';
  SELECT id, center INTO loc FROM localities WHERE slug = 'madhapur';
  SELECT id INTO cat FROM categories WHERE slug = 'plumbers';

  INSERT INTO businesses (name) VALUES ('Smoke Notify Business') RETURNING id INTO biz;
  INSERT INTO business_members (business_id, user_id, role) VALUES (biz, owner_id, 'owner');
  INSERT INTO listings (business_id, locality_id, slug, name, location, phone_primary, status, created_by_user_id)
  VALUES (biz, loc.id, 'smoke-notify', 'Smoke Notify', loc.center, '+919999900301', 'active', owner_id)
  RETURNING id INTO listing;
  INSERT INTO listing_categories (listing_id, category_id, is_primary) VALUES (listing, cat, true);

  INSERT INTO enquiries (listing_id, name, phone_e164, message)
  VALUES (listing, 'Customer N', '+919876500301', 'Please call me about a blocked drain today.')
  RETURNING id INTO enquiry;

  -- 1. A notification arrives the way the API writes it: pending, never attempted.
  INSERT INTO notifications (kind, channel, recipient, user_id, subject_type, subject_id)
  VALUES ('enquiry_received', 'email', 'notify.owner@example.com', owner_id, 'enquiry', enquiry)
  RETURNING id INTO note;

  SELECT count(*) INTO n
  FROM notifications
  WHERE id = note AND status = 'pending' AND attempts = 0 AND sent_at IS NULL AND error IS NULL;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: a new notification was not pending and untouched'; END IF;

  -- 2. The query the owner-lookup relies on: the listing's owner, with an address to send to.
  SELECT count(*) INTO n
  FROM listings li
  JOIN business_members bm ON bm.business_id = li.business_id AND bm.role = 'owner'
  JOIN users u ON u.id = bm.user_id
  WHERE li.id = listing AND u.email IS NOT NULL;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: the listing owner with an email was not found, got % rows', n; END IF;

  -- 3. Marking it sent, the way the API does after the provider accepts it.
  UPDATE notifications
  SET status = 'sent', attempts = attempts + 1, sent_at = now(), error = NULL, updated_at = now()
  WHERE id = note;
  SELECT sent_at INTO t FROM notifications WHERE id = note;
  IF t IS NULL THEN RAISE EXCEPTION 'FAIL: a sent notification has no sent_at'; END IF;

  -- 4. A sent row may not also carry a failure reason: the two states cannot both be true.
  BEGIN
    UPDATE notifications SET error = 'something went wrong' WHERE id = note;
    RAISE EXCEPTION 'FAIL: a sent notification accepted an error';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: sent + error rejected';
  END;

  -- 5. A failure must say why, and must not look like it went out.
  INSERT INTO notifications (kind, channel, recipient, user_id, subject_type, subject_id)
  VALUES ('listing_rejected', 'email', 'notify.owner@example.com', owner_id, 'listing', listing)
  RETURNING id INTO note;

  BEGIN
    UPDATE notifications SET status = 'failed', attempts = 1, updated_at = now() WHERE id = note;
    RAISE EXCEPTION 'FAIL: a failed notification was accepted with no reason';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: failed without a reason rejected';
  END;

  UPDATE notifications
  SET status = 'failed', attempts = attempts + 1,
      error = 'InvalidOperationException: No email provider is configured (Email:Provider).',
      updated_at = now()
  WHERE id = note;

  BEGIN
    UPDATE notifications SET sent_at = now() WHERE id = note;
    RAISE EXCEPTION 'FAIL: a failed notification accepted a sent_at';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: failed + sent_at rejected';
  END;

  -- 6. A pending row may not pretend to have been attempted.
  BEGIN
    INSERT INTO notifications (kind, channel, recipient, user_id, subject_type, subject_id, attempts)
    VALUES ('listing_approved', 'email', 'notify.owner@example.com', owner_id, 'listing', listing, 2);
    RAISE EXCEPTION 'FAIL: a pending notification was accepted with attempts';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: pending with attempts rejected';
  END;

  -- 7. The enumerations refuse anything the API does not handle.
  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
    VALUES ('owner_birthday', 'email', 'notify.owner@example.com', 'listing', listing);
    RAISE EXCEPTION 'FAIL: an unknown kind was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: unknown kind rejected';
  END;

  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
    VALUES ('listing_approved', 'pigeon', 'notify.owner@example.com', 'listing', listing);
    RAISE EXCEPTION 'FAIL: an unknown channel was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: unknown channel rejected';
  END;

  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
    VALUES ('listing_approved', 'email', 'notify.owner@example.com', 'invoice', listing);
    RAISE EXCEPTION 'FAIL: an unknown subject type was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: unknown subject type rejected';
  END;

  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id, status)
    VALUES ('listing_approved', 'email', 'notify.owner@example.com', 'listing', listing, 'queued');
    RAISE EXCEPTION 'FAIL: an unknown status was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: unknown status rejected';
  END;

  -- 8. The email address rules: no blank, no mixed case, and no whitespace - a newline in an
  --    address is how a header injection would reach a provider.
  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
    VALUES ('listing_approved', 'email', '   ', 'listing', listing);
    RAISE EXCEPTION 'FAIL: a blank recipient was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: blank recipient rejected';
  END;

  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
    VALUES ('listing_approved', 'email', 'Mixed@Example.com', 'listing', listing);
    RAISE EXCEPTION 'FAIL: a mixed-case recipient was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: mixed-case recipient rejected';
  END;

  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
    VALUES ('listing_approved', 'email', E'owner@example.com\nbcc: someone@else.com', 'listing', listing);
    RAISE EXCEPTION 'FAIL: a recipient containing a newline was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: newline in recipient rejected';
  END;

  BEGIN
    INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
    VALUES ('listing_approved', 'email', 'not-an-address', 'listing', listing);
    RAISE EXCEPTION 'FAIL: a recipient with no @ was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: recipient with no @ rejected';
  END;

  -- A number is a valid recipient on the sms channel, where the email rule does not apply.
  INSERT INTO notifications (kind, channel, recipient, subject_type, subject_id)
  VALUES ('listing_approved', 'sms', '+919999900301', 'listing', listing);

  -- 9. Everything that has not gone out, which is what the partial index is for.
  SELECT count(*) INTO n FROM notifications WHERE status <> 'sent' AND subject_id = listing;
  IF n <> 2 THEN RAISE EXCEPTION 'FAIL: expected 2 unsent notifications for the listing, found %', n; END IF;

  -- 10. Losing the owner's account keeps the delivery record: it still says where we sent.
  DELETE FROM notifications WHERE user_id = owner_id AND subject_type = 'enquiry';
  DELETE FROM enquiries WHERE listing_id = listing;
  DELETE FROM listing_categories WHERE listing_id = listing;
  DELETE FROM listings WHERE id = listing;
  DELETE FROM business_members WHERE user_id = owner_id;
  DELETE FROM users WHERE id = owner_id;

  SELECT count(*) INTO n
  FROM notifications
  WHERE subject_id = listing AND user_id IS NULL AND recipient = 'notify.owner@example.com';
  IF n < 1 THEN RAISE EXCEPTION 'FAIL: the delivery record did not outlive the account'; END IF;

  RAISE NOTICE 'ok: notification rules behaved as expected';
END $$;

ROLLBACK;
