-- S0003: owner OTP + sessions, admin accounts (password + TOTP MFA), roles/permissions,
-- append-only audit log. The SQL Server counterpart of PostgreSQL V0003, with V0005's
-- `totp_last_step` folded in.
--
-- Secrets (OTP codes, refresh tokens, passwords, TOTP seeds) are never stored in plain text, exactly
-- as before: the hash columns are `varbinary`, the password hash is a formatted string carrying its
-- own iteration count, and the TOTP secret is encrypted before it reaches the database.

-- Spatial and filtered indexes require these to be ON, and a client that leaves either OFF makes
-- the index creation fail rather than silently differ. Microsoft.Data.SqlClient has them ON already;
-- sqlcmd does not, so they are set here and the file is correct whichever applies it.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ---------------------------------------------------------- otp_challenges
-- One row per OTP sent. Rate limits (per phone, per IP, resend cooldown) are counted from these rows,
-- so they work across several API instances. code_hash is an HMAC (key held in Key Vault), never the code.
CREATE TABLE otp_challenges (
    id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT otp_challenges_pkey PRIMARY KEY,
    phone_e164     varchar(16) NOT NULL,
    purpose        varchar(30) NOT NULL CONSTRAINT otp_challenges_purpose_default DEFAULT 'owner_login',
    code_hash      varbinary(64) NOT NULL,
    expires_at     datetimeoffset(3) NOT NULL,
    attempts       smallint NOT NULL CONSTRAINT otp_challenges_attempts_default DEFAULT 0,
    max_attempts   smallint NOT NULL CONSTRAINT otp_challenges_max_attempts_default DEFAULT 5,
    consumed_at    datetimeoffset(3) NULL,
    requester_ip   varchar(45) NULL,
    created_at     datetimeoffset(3) NOT NULL CONSTRAINT otp_challenges_created_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT otp_challenges_phone_format CHECK (
        phone_e164 LIKE '+[1-9]%'
        AND LEN(phone_e164) BETWEEN 9 AND 16
        AND SUBSTRING(phone_e164, 2, LEN(phone_e164)) NOT LIKE '%[^0-9]%'
    ),
    CONSTRAINT otp_challenges_purpose_check CHECK (purpose IN ('owner_login')),
    CONSTRAINT otp_challenges_attempts_check CHECK (attempts >= 0 AND max_attempts > 0)
);
CREATE INDEX otp_challenges_phone_created_idx ON otp_challenges (phone_e164, created_at DESC);
CREATE INDEX otp_challenges_ip_created_idx ON otp_challenges (requester_ip, created_at DESC) WHERE requester_ip IS NOT NULL;
CREATE INDEX otp_challenges_expires_idx ON otp_challenges (expires_at);

-- ----------------------------------------------------------- user_sessions
-- Owner sessions. The refresh token is opaque and stored hashed. Each use rotates it (old row gets
-- rotated_at, new row shares the same family_id). Presenting an already-rotated token means theft:
-- the whole family is revoked.
CREATE TABLE user_sessions (
    id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT user_sessions_pkey PRIMARY KEY,
    user_id             bigint NOT NULL CONSTRAINT user_sessions_user_fk REFERENCES users (id),
    family_id           uniqueidentifier NOT NULL CONSTRAINT user_sessions_family_default DEFAULT NEWID(),
    refresh_token_hash  varbinary(64) NOT NULL,
    created_at          datetimeoffset(3) NOT NULL CONSTRAINT user_sessions_created_default DEFAULT SYSDATETIMEOFFSET(),
    expires_at          datetimeoffset(3) NOT NULL,
    rotated_at          datetimeoffset(3) NULL,
    revoked_at          datetimeoffset(3) NULL,
    revoked_reason      nvarchar(200) NULL,
    ip                  varchar(45) NULL,
    user_agent          nvarchar(400) NULL,
    CONSTRAINT user_sessions_token_key UNIQUE (refresh_token_hash)
);
CREATE INDEX user_sessions_user_idx ON user_sessions (user_id);
CREATE INDEX user_sessions_family_idx ON user_sessions (family_id);

-- ------------------------------------------------------------- admin_users
-- Invite-only. Email + password + mandatory TOTP MFA + recovery codes + lockout (docs/security.md).
-- The first super-admin is created by a one-time command-line bootstrap, never by an HTTP endpoint.
CREATE TABLE admin_users (
    id                    bigint IDENTITY(1,1) NOT NULL CONSTRAINT admin_users_pkey PRIMARY KEY,
    email                 nvarchar(254) NOT NULL,
    display_name          nvarchar(200) NOT NULL,
    password_hash         nvarchar(400) NULL,
    status                varchar(20) NOT NULL CONSTRAINT admin_users_status_default DEFAULT 'invited',
    totp_secret_encrypted varbinary(256) NULL,
    totp_enabled_at       datetimeoffset(3) NULL,
    /* The TOTP step of the most recently accepted code (V0005). Codes at or before it are refused as
       replays; the step cannot be inferred from a timestamp, because a code from the next step is
       accepted for clock drift and would otherwise stay replayable. */
    totp_last_step        bigint NULL,
    failed_login_count    int NOT NULL CONSTRAINT admin_users_failed_login_default DEFAULT 0,
    locked_until          datetimeoffset(3) NULL,
    invite_token_hash     varbinary(64) NULL,
    invite_expires_at     datetimeoffset(3) NULL,
    invited_by_admin_id   bigint NULL CONSTRAINT admin_users_invited_by_fk REFERENCES admin_users (id),
    last_login_at         datetimeoffset(3) NULL,
    password_changed_at   datetimeoffset(3) NULL,
    created_at            datetimeoffset(3) NOT NULL CONSTRAINT admin_users_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at            datetimeoffset(3) NOT NULL CONSTRAINT admin_users_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT admin_users_email_key UNIQUE (email),
    /* Lower case with an @ in it, as before. The comparison is forced to a case-sensitive collation,
       because the database's own collation would consider 'A@b.com' equal to 'a@b.com' and the check
       would pass for both. */
    CONSTRAINT admin_users_email_lowercase CHECK (
        email COLLATE Latin1_General_CS_AS = LOWER(email) COLLATE Latin1_General_CS_AS
        AND CHARINDEX('@', email) > 1
    ),
    CONSTRAINT admin_users_status_check CHECK (status IN ('invited', 'active', 'disabled')),
    CONSTRAINT admin_users_failed_login_check CHECK (failed_login_count >= 0)
);
CREATE UNIQUE INDEX admin_users_invite_token_idx ON admin_users (invite_token_hash) WHERE invite_token_hash IS NOT NULL;

-- The review decision on a listing points at the admin who made it. The foreign key waits until here
-- because `listings` is created first (S0002) and `admin_users` only exists now.
ALTER TABLE listings
    ADD CONSTRAINT listings_reviewed_by_fk FOREIGN KEY (reviewed_by_admin_id) REFERENCES admin_users (id);

CREATE TABLE admin_recovery_codes (
    id             bigint IDENTITY(1,1) NOT NULL CONSTRAINT admin_recovery_codes_pkey PRIMARY KEY,
    admin_user_id  bigint NOT NULL CONSTRAINT admin_recovery_codes_admin_fk
                       REFERENCES admin_users (id) ON DELETE CASCADE,
    code_hash      varbinary(64) NOT NULL,
    used_at        datetimeoffset(3) NULL,
    created_at     datetimeoffset(3) NOT NULL CONSTRAINT admin_recovery_codes_created_default DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX admin_recovery_codes_admin_idx ON admin_recovery_codes (admin_user_id);

-- ---------------------------------------------------------- admin_sessions
-- Same rotation scheme as user_sessions. last_mfa_at records when the second factor was completed.
CREATE TABLE admin_sessions (
    id                  bigint IDENTITY(1,1) NOT NULL CONSTRAINT admin_sessions_pkey PRIMARY KEY,
    admin_user_id       bigint NOT NULL CONSTRAINT admin_sessions_admin_fk REFERENCES admin_users (id),
    family_id           uniqueidentifier NOT NULL CONSTRAINT admin_sessions_family_default DEFAULT NEWID(),
    refresh_token_hash  varbinary(64) NOT NULL,
    created_at          datetimeoffset(3) NOT NULL CONSTRAINT admin_sessions_created_default DEFAULT SYSDATETIMEOFFSET(),
    expires_at          datetimeoffset(3) NOT NULL,
    last_mfa_at         datetimeoffset(3) NULL,
    rotated_at          datetimeoffset(3) NULL,
    revoked_at          datetimeoffset(3) NULL,
    revoked_reason      nvarchar(200) NULL,
    ip                  varchar(45) NULL,
    user_agent          nvarchar(400) NULL,
    CONSTRAINT admin_sessions_token_key UNIQUE (refresh_token_hash)
);
CREATE INDEX admin_sessions_admin_idx ON admin_sessions (admin_user_id);
CREATE INDEX admin_sessions_family_idx ON admin_sessions (family_id);

-- ------------------------------------------------- roles and permissions
-- Permissions are loaded server-side on every request, so revoking one takes effect immediately.
CREATE TABLE roles (
    id           int IDENTITY(1,1) NOT NULL CONSTRAINT roles_pkey PRIMARY KEY,
    slug         varchar(60) NOT NULL,
    name         nvarchar(120) NOT NULL,
    description  nvarchar(400) NULL,
    is_system    bit NOT NULL CONSTRAINT roles_is_system_default DEFAULT 0,
    created_at   datetimeoffset(3) NOT NULL CONSTRAINT roles_created_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT roles_slug_key UNIQUE (slug),
    CONSTRAINT roles_slug_format CHECK (
        slug COLLATE Latin1_General_BIN2 LIKE '[a-z]%'
        AND slug COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9_]%'
    )
);

CREATE TABLE permissions (
    id           int IDENTITY(1,1) NOT NULL CONSTRAINT permissions_pkey PRIMARY KEY,
    code         varchar(60) NOT NULL,
    description  nvarchar(400) NOT NULL,
    CONSTRAINT permissions_code_key UNIQUE (code),
    /* 'area.action', lower case with at least one dot: the dot has to be there, it cannot start or
       end the code, and nothing else but letters, underscores and dots is allowed. */
    CONSTRAINT permissions_code_format CHECK (
        code COLLATE Latin1_General_BIN2 LIKE '[a-z]%'
        AND code COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z_.]%'
        AND CHARINDEX('.', code) > 1
        AND code NOT LIKE '%.'
        AND code NOT LIKE '%..%'
    )
);

CREATE TABLE role_permissions (
    role_id        int NOT NULL CONSTRAINT role_permissions_role_fk REFERENCES roles (id) ON DELETE CASCADE,
    permission_id  int NOT NULL CONSTRAINT role_permissions_permission_fk
                       REFERENCES permissions (id) ON DELETE CASCADE,
    CONSTRAINT role_permissions_pkey PRIMARY KEY (role_id, permission_id)
);
CREATE INDEX role_permissions_permission_idx ON role_permissions (permission_id);

CREATE TABLE admin_user_roles (
    /* ON DELETE CASCADE is deliberately absent, unlike the PostgreSQL original. SQL Server refuses a
       table with two foreign keys to the same parent where one of them cascades ("may cause cycles or
       multiple cascade paths"), and `assigned_by_admin_id` is the second one. Deleting an admin
       account therefore means deleting its role rows first; nothing in the application deletes an
       account anyway -- it disables one, which keeps the history. */
    admin_user_id        bigint NOT NULL CONSTRAINT admin_user_roles_admin_fk REFERENCES admin_users (id),
    role_id              int NOT NULL CONSTRAINT admin_user_roles_role_fk REFERENCES roles (id),
    assigned_by_admin_id bigint NULL CONSTRAINT admin_user_roles_assigned_by_fk REFERENCES admin_users (id),
    created_at           datetimeoffset(3) NOT NULL CONSTRAINT admin_user_roles_created_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT admin_user_roles_pkey PRIMARY KEY (admin_user_id, role_id)
);
CREATE INDEX admin_user_roles_role_idx ON admin_user_roles (role_id);

-- ---------------------------------------------------------------- audit_logs
-- Every admin / super-admin action, and security events.
CREATE TABLE audit_logs (
    id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT audit_logs_pkey PRIMARY KEY,
    occurred_at  datetimeoffset(3) NOT NULL CONSTRAINT audit_logs_occurred_default DEFAULT SYSDATETIMEOFFSET(),
    actor_type   varchar(20) NOT NULL,
    actor_id     bigint NULL,
    action       varchar(100) NOT NULL,
    entity_type  varchar(40) NULL,
    entity_id    nvarchar(100) NULL,
    -- PostgreSQL's jsonb. SQL Server has no JSON type, so the column is text the engine checks is JSON.
    details      nvarchar(max) NULL,
    ip           varchar(45) NULL,
    request_id   nvarchar(100) NULL,
    CONSTRAINT audit_logs_actor_type_check CHECK (actor_type IN ('owner', 'admin', 'system')),
    CONSTRAINT audit_logs_action_not_blank CHECK (LEN(LTRIM(RTRIM(action))) > 0),
    CONSTRAINT audit_logs_details_is_json CHECK (details IS NULL OR ISJSON(details) = 1)
);
CREATE INDEX audit_logs_occurred_idx ON audit_logs (occurred_at DESC);
CREATE INDEX audit_logs_entity_idx ON audit_logs (entity_type, entity_id);
CREATE INDEX audit_logs_actor_idx ON audit_logs (actor_type, actor_id, occurred_at DESC);
GO

/* Append-only, which PostgreSQL did with two rules that rewrote UPDATE and DELETE into nothing.
   SQL Server has no rules, so the equivalent is a pair of INSTEAD OF triggers with empty bodies: the
   statement is accepted, affects no rows, and the log keeps what it has. A privileged user can still
   drop the trigger, exactly as a superuser could drop the rule -- this stops the application and an
   ordinary mistake, not an administrator with intent. */
CREATE TRIGGER audit_logs_no_update ON audit_logs INSTEAD OF UPDATE
AS
BEGIN
    SET NOCOUNT ON;   -- accept the statement and change nothing
END;
GO

CREATE TRIGGER audit_logs_no_delete ON audit_logs INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
END;
GO
