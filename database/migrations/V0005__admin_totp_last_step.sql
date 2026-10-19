-- V0005: the TOTP step an administrator's last accepted code belonged to. Additive only.
--
-- Replay protection needs to know which code was spent, and the step cannot be inferred from
-- admin_sessions.last_mfa_at. A code from one step either side of now is accepted for clock drift,
-- so a code belonging to the *next* step records a completion time whose own step is earlier than
-- the code's. Deriving the spent step from that timestamp therefore lets the same code be presented
-- a second time, which it did until this column existed.
--
-- Nullable: an administrator who has never completed a TOTP check has no step to record, and the
-- existing rows predate MFA entirely.
ALTER TABLE admin_users
    ADD COLUMN totp_last_step bigint;

COMMENT ON COLUMN admin_users.totp_last_step IS
    'TOTP step of the most recently accepted code. Codes at or before it are refused as replays.';
