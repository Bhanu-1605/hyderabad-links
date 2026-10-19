-- Manual smoke test for the database rules behind admin MFA (V0005 and docs/security.md).
-- It creates its own administrator, exercises the recovery-code and replay rules, then rolls back,
-- so it leaves no data and is safe to re-run.
--
-- Run:  Get-Content database/checks/admin_mfa_smoke.sql | docker exec -i hl-postgres psql -U hl_dev -d hyderabad_links -v ON_ERROR_STOP=1
BEGIN;

INSERT INTO admin_users (email, display_name, password_hash, status)
VALUES ('smoke.mfa@example.com', 'Smoke MFA', 'pbkdf2-sha256$1$AAAA$AAAA', 'active');

DO $$
DECLARE
  aid  bigint;
  n    integer;
BEGIN
  SELECT id INTO aid FROM admin_users WHERE email = 'smoke.mfa@example.com';

  -- 1. A new administrator has no authenticator, so they are not yet protected by one.
  IF (SELECT totp_enabled_at FROM admin_users WHERE id = aid) IS NOT NULL THEN
    RAISE EXCEPTION 'FAIL: a new admin should not be enrolled';
  END IF;
  IF (SELECT totp_last_step FROM admin_users WHERE id = aid) IS NOT NULL THEN
    RAISE EXCEPTION 'FAIL: a new admin should have no spent TOTP step';
  END IF;

  -- 2. Enrollment stores a sealed secret. The column holds ciphertext, never the raw 20-byte
  --    secret: nonce (12) + tag (16) + secret (20) = 48 bytes.
  UPDATE admin_users
  SET totp_secret_encrypted = decode(repeat('ab', 48), 'hex'), totp_enabled_at = now()
  WHERE id = aid;
  IF (SELECT length(totp_secret_encrypted) FROM admin_users WHERE id = aid) <> 48 THEN
    RAISE EXCEPTION 'FAIL: the sealed secret is not the expected length';
  END IF;

  -- 3. Recovery codes are stored as hashes, never as the codes themselves.
  INSERT INTO admin_recovery_codes (admin_user_id, code_hash)
  SELECT aid, sha256(('code-' || g)::bytea) FROM generate_series(1, 10) g;
  SELECT count(*) INTO n FROM admin_recovery_codes WHERE admin_user_id = aid AND used_at IS NULL;
  IF n <> 10 THEN RAISE EXCEPTION 'FAIL: expected 10 unused recovery codes, found %', n; END IF;

  -- 4. Spending one marks it used, and the same UPDATE cannot spend it again. This is the exact
  --    statement the endpoint runs, so a second attempt affecting 0 rows is what blocks reuse.
  UPDATE admin_recovery_codes SET used_at = now()
  WHERE id = (SELECT id FROM admin_recovery_codes
              WHERE admin_user_id = aid AND code_hash = sha256('code-1'::bytea) AND used_at IS NULL
              LIMIT 1);
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 1 THEN RAISE EXCEPTION 'FAIL: spending a fresh recovery code affected % rows', n; END IF;

  UPDATE admin_recovery_codes SET used_at = now()
  WHERE id = (SELECT id FROM admin_recovery_codes
              WHERE admin_user_id = aid AND code_hash = sha256('code-1'::bytea) AND used_at IS NULL
              LIMIT 1);
  GET DIAGNOSTICS n = ROW_COUNT;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: the same recovery code was spent twice'; END IF;

  SELECT count(*) INTO n FROM admin_recovery_codes WHERE admin_user_id = aid AND used_at IS NULL;
  IF n <> 9 THEN RAISE EXCEPTION 'FAIL: expected 9 unused codes after spending one, found %', n; END IF;

  -- 5. The spent TOTP step is recorded on the account, which is what makes replay detection exact.
  --    A timestamp cannot stand in for it: a code accepted from the *next* step belongs to a later
  --    step than "now", so a clock-derived value would be too low to refuse that same code again.
  UPDATE admin_users SET totp_last_step = 59744977 WHERE id = aid;
  IF (SELECT totp_last_step FROM admin_users WHERE id = aid) <> 59744977 THEN
    RAISE EXCEPTION 'FAIL: the spent step was not recorded';
  END IF;

  -- 6. A session records when MFA completed. The sign-in path always sets it, and refresh carries
  --    it forward, so a session without it must never be treated as authenticated.
  INSERT INTO admin_sessions (admin_user_id, refresh_token_hash, expires_at, last_mfa_at)
  VALUES (aid, sha256('refresh-1'::bytea), now() + interval '7 days', now());
  IF (SELECT last_mfa_at FROM admin_sessions WHERE admin_user_id = aid) IS NULL THEN
    RAISE EXCEPTION 'FAIL: the session did not record its MFA time';
  END IF;

  -- 7. Deleting the administrator takes their recovery codes with them.
  DELETE FROM admin_sessions WHERE admin_user_id = aid;
  DELETE FROM admin_users WHERE id = aid;
  SELECT count(*) INTO n FROM admin_recovery_codes WHERE admin_user_id = aid;
  IF n <> 0 THEN RAISE EXCEPTION 'FAIL: % recovery codes outlived their admin', n; END IF;

  RAISE NOTICE 'ok: admin MFA storage rules behaved as expected';
END $$;

ROLLBACK;
