-- Manual smoke test for V0003 (rolled back, leaves no data).
-- Run:  Get-Content database/checks/v0003_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO users (phone_e164, phone_verified_at) VALUES ('+919999900011', now());
INSERT INTO otp_challenges (phone_e164, code_hash, expires_at, requester_ip)
VALUES ('+919999900011', '\xdeadbeef', now() + interval '5 minutes', '203.0.113.7');
INSERT INTO user_sessions (user_id, refresh_token_hash, expires_at)
SELECT id, '\x0102030405', now() + interval '30 days' FROM users WHERE phone_e164 = '+919999900011';

INSERT INTO admin_users (email, display_name, status) VALUES ('smoke.admin@example.com', 'Smoke Admin', 'active');
INSERT INTO admin_user_roles (admin_user_id, role_id)
SELECT a.id, r.id FROM admin_users a, roles r WHERE a.email = 'smoke.admin@example.com' AND r.slug = 'admin';

INSERT INTO audit_logs (actor_type, actor_id, action, entity_type, entity_id, details)
SELECT 'admin', a.id, 'listing.approve', 'listing', '42', '{"reason":"smoke"}'::jsonb FROM admin_users a WHERE a.email = 'smoke.admin@example.com';

DO $$
DECLARE n integer;
BEGIN
  BEGIN  -- mixed-case admin email
    INSERT INTO admin_users (email, display_name) VALUES ('Mixed.Case@Example.com', 'X');
    RAISE EXCEPTION 'FAIL: mixed-case email was accepted';
  EXCEPTION WHEN check_violation THEN RAISE NOTICE 'ok: mixed-case admin email rejected'; END;

  BEGIN  -- same refresh token twice
    INSERT INTO user_sessions (user_id, refresh_token_hash, expires_at)
    SELECT id, '\x0102030405', now() + interval '1 day' FROM users WHERE phone_e164 = '+919999900011';
    RAISE EXCEPTION 'FAIL: duplicate refresh token was accepted';
  EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'ok: duplicate refresh token rejected'; END;

  UPDATE audit_logs SET action = 'tampered' WHERE action = 'listing.approve';
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 0 OR NOT EXISTS (SELECT 1 FROM audit_logs WHERE action = 'listing.approve') THEN
    RAISE EXCEPTION 'FAIL: audit log row was modified';
  END IF;
  DELETE FROM audit_logs WHERE action = 'listing.approve';
  IF NOT EXISTS (SELECT 1 FROM audit_logs WHERE action = 'listing.approve') THEN
    RAISE EXCEPTION 'FAIL: audit log row was deleted';
  END IF;
  RAISE NOTICE 'ok: audit log is append-only';
END $$;

-- Effective permissions per role (super_admin should have all 12, admin 7).
SELECT r.slug AS role, count(*) AS permissions
FROM roles r JOIN role_permissions rp ON rp.role_id = r.id
GROUP BY r.slug ORDER BY r.slug;

ROLLBACK;
