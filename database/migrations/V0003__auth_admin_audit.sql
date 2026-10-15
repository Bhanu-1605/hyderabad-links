-- V0003: owner OTP + sessions, admin accounts (password + TOTP MFA), roles/permissions, append-only audit log.
-- Additive only. Matches docs/security.md. Secrets (OTP codes, refresh tokens, passwords, TOTP seeds) are never stored in plain text.

-- ---------------------------------------------------------- otp_challenges
-- One row per OTP sent. Rate limits (per phone, per IP, resend cooldown) are counted from these rows,
-- so they work across several API instances. code_hash is an HMAC (key held in Key Vault), never the code.
CREATE TABLE otp_challenges (
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    phone_e164     text NOT NULL,
    purpose        text NOT NULL DEFAULT 'owner_login',
    code_hash      bytea NOT NULL,
    expires_at     timestamptz NOT NULL,
    attempts       smallint NOT NULL DEFAULT 0,
    max_attempts   smallint NOT NULL DEFAULT 5,
    consumed_at    timestamptz,
    requester_ip   inet,
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT otp_challenges_phone_format CHECK (phone_e164 ~ '^\+[1-9][0-9]{7,14}$'),
    CONSTRAINT otp_challenges_purpose_check CHECK (purpose IN ('owner_login')),
    CONSTRAINT otp_challenges_attempts_check CHECK (attempts >= 0 AND max_attempts > 0)
);
CREATE INDEX otp_challenges_phone_created_idx ON otp_challenges (phone_e164, created_at DESC);
CREATE INDEX otp_challenges_ip_created_idx ON otp_challenges (requester_ip, created_at DESC) WHERE requester_ip IS NOT NULL;
CREATE INDEX otp_challenges_expires_idx ON otp_challenges (expires_at);

-- ----------------------------------------------------------- user_sessions
-- Owner sessions. The refresh token is opaque and stored hashed. Each use rotates it (old row gets rotated_at, new row
-- shares the same family_id). Presenting an already-rotated token means theft: the whole family is revoked.
CREATE TABLE user_sessions (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id             bigint NOT NULL REFERENCES users (id),
    family_id           uuid NOT NULL DEFAULT gen_random_uuid(),
    refresh_token_hash  bytea NOT NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    expires_at          timestamptz NOT NULL,
    rotated_at          timestamptz,
    revoked_at          timestamptz,
    revoked_reason      text,
    ip                  inet,
    user_agent          text,
    CONSTRAINT user_sessions_token_key UNIQUE (refresh_token_hash)
);
CREATE INDEX user_sessions_user_idx ON user_sessions (user_id);
CREATE INDEX user_sessions_family_idx ON user_sessions (family_id);

-- ------------------------------------------------------------- admin_users
-- Invite-only. Email + password + mandatory TOTP MFA + recovery codes + lockout (docs/security.md).
-- The first super-admin is created by a one-time command-line bootstrap, never by an HTTP endpoint.
CREATE TABLE admin_users (
    id                    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email                 text NOT NULL,
    display_name          text NOT NULL,
    password_hash         text,
    status                text NOT NULL DEFAULT 'invited',
    totp_secret_encrypted bytea,
    totp_enabled_at       timestamptz,
    failed_login_count    integer NOT NULL DEFAULT 0,
    locked_until          timestamptz,
    invite_token_hash     bytea,
    invite_expires_at     timestamptz,
    invited_by_admin_id   bigint REFERENCES admin_users (id),
    last_login_at         timestamptz,
    password_changed_at   timestamptz,
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT admin_users_email_key UNIQUE (email),
    CONSTRAINT admin_users_email_lowercase CHECK (email = lower(email) AND position('@' in email) > 1),
    CONSTRAINT admin_users_status_check CHECK (status IN ('invited', 'active', 'disabled')),
    CONSTRAINT admin_users_failed_login_check CHECK (failed_login_count >= 0)
);
CREATE UNIQUE INDEX admin_users_invite_token_idx ON admin_users (invite_token_hash) WHERE invite_token_hash IS NOT NULL;

CREATE TABLE admin_recovery_codes (
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    admin_user_id  bigint NOT NULL REFERENCES admin_users (id) ON DELETE CASCADE,
    code_hash      bytea NOT NULL,
    used_at        timestamptz,
    created_at     timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX admin_recovery_codes_admin_idx ON admin_recovery_codes (admin_user_id);

-- ---------------------------------------------------------- admin_sessions
-- Same rotation scheme as user_sessions. last_mfa_at supports "sensitive actions require MFA re-entry".
CREATE TABLE admin_sessions (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    admin_user_id       bigint NOT NULL REFERENCES admin_users (id),
    family_id           uuid NOT NULL DEFAULT gen_random_uuid(),
    refresh_token_hash  bytea NOT NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    expires_at          timestamptz NOT NULL,
    last_mfa_at         timestamptz,
    rotated_at          timestamptz,
    revoked_at          timestamptz,
    revoked_reason      text,
    ip                  inet,
    user_agent          text,
    CONSTRAINT admin_sessions_token_key UNIQUE (refresh_token_hash)
);
CREATE INDEX admin_sessions_admin_idx ON admin_sessions (admin_user_id);
CREATE INDEX admin_sessions_family_idx ON admin_sessions (family_id);

-- ------------------------------------------------- roles and permissions
-- Permissions are loaded server-side on every request, so revoking one takes effect immediately.
CREATE TABLE roles (
    id           integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    slug         text NOT NULL,
    name         text NOT NULL,
    description  text,
    is_system    boolean NOT NULL DEFAULT false,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT roles_slug_key UNIQUE (slug),
    CONSTRAINT roles_slug_format CHECK (slug ~ '^[a-z][a-z0-9_]*$')
);

CREATE TABLE permissions (
    id           integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code         text NOT NULL,
    description  text NOT NULL,
    CONSTRAINT permissions_code_key UNIQUE (code),
    CONSTRAINT permissions_code_format CHECK (code ~ '^[a-z][a-z_]*(\.[a-z][a-z_]*)+$')
);

CREATE TABLE role_permissions (
    role_id        integer NOT NULL REFERENCES roles (id) ON DELETE CASCADE,
    permission_id  integer NOT NULL REFERENCES permissions (id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);
CREATE INDEX role_permissions_permission_idx ON role_permissions (permission_id);

CREATE TABLE admin_user_roles (
    admin_user_id        bigint NOT NULL REFERENCES admin_users (id) ON DELETE CASCADE,
    role_id              integer NOT NULL REFERENCES roles (id),
    assigned_by_admin_id bigint REFERENCES admin_users (id),
    created_at           timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (admin_user_id, role_id)
);
CREATE INDEX admin_user_roles_role_idx ON admin_user_roles (role_id);

-- ---------------------------------------------------------------- audit_logs
-- Every admin / super-admin action, and security events. Append-only: the rules below turn UPDATE and DELETE into no-ops
-- for every role. (When least-privilege database roles are introduced, the application role also loses those privileges.)
CREATE TABLE audit_logs (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at  timestamptz NOT NULL DEFAULT now(),
    actor_type   text NOT NULL,
    actor_id     bigint,
    action       text NOT NULL,
    entity_type  text,
    entity_id    text,
    details      jsonb,
    ip           inet,
    request_id   text,
    CONSTRAINT audit_logs_actor_type_check CHECK (actor_type IN ('owner', 'admin', 'system')),
    CONSTRAINT audit_logs_action_not_blank CHECK (btrim(action) <> '')
);
CREATE INDEX audit_logs_occurred_idx ON audit_logs (occurred_at DESC);
CREATE INDEX audit_logs_entity_idx ON audit_logs (entity_type, entity_id);
CREATE INDEX audit_logs_actor_idx ON audit_logs (actor_type, actor_id, occurred_at DESC);

CREATE RULE audit_logs_no_update AS ON UPDATE TO audit_logs DO INSTEAD NOTHING;
CREATE RULE audit_logs_no_delete AS ON DELETE TO audit_logs DO INSTEAD NOTHING;
