-- S0007: the record of every notification we tried to send.
-- The SQL Server counterpart of PostgreSQL V0010.
--
-- Sending is deliberately allowed to fail: an enquiry that was stored matters more than an email
-- that went out, so the API records the enquiry, commits, and only then attempts delivery. That is
-- exactly why this table exists -- without it an unconfigured provider or a dead API key would be a
-- silent nothing, and the first person to notice would be an owner wondering why they never hear
-- about their customers.
--
-- This is a log of attempts, not a queue. Nothing retries from it yet; a row sitting at 'failed' is
-- a thing for a person to look at.

-- Spatial and filtered indexes require these to be ON, and a client that leaves either OFF makes
-- the index creation fail rather than silently differ. Microsoft.Data.SqlClient has them ON already;
-- sqlcmd does not, so they are set here and the file is correct whichever applies it.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE notifications (
    id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT notifications_pkey PRIMARY KEY,
    -- Which event caused it. Adding a kind later means extending this constraint, which is the
    -- point: a typo in the API cannot invent a notification type nobody handles.
    kind          varchar(40) NOT NULL,
    -- Email today. SMS is listed because the OTP sender already exists and will be the second channel.
    channel       varchar(20) NOT NULL CONSTRAINT notifications_channel_default DEFAULT 'email',
    -- The address or number it was sent to, as it was at the time. Kept here rather than read back
    -- from `users` so the record still says where it actually went after someone edits their profile.
    recipient     nvarchar(254) NOT NULL,
    /* The owner being notified, when they have an account. Null for a recipient who is not a user.
       ON DELETE SET NULL keeps the delivery record when an account goes away. */
    user_id       bigint NULL CONSTRAINT notifications_user_fk REFERENCES users (id) ON DELETE SET NULL,
    -- What it was about. The same column pair as `reports`, for the same reason.
    subject_type  varchar(20) NOT NULL,
    subject_id    bigint NOT NULL,
    status        varchar(20) NOT NULL CONSTRAINT notifications_status_default DEFAULT 'pending',
    -- How many delivery attempts have been made. 0 while pending.
    attempts      int NOT NULL CONSTRAINT notifications_attempts_default DEFAULT 0,
    /* Why it failed, short and in plain words. Never a provider API key, a token or a password:
       whatever writes here must pass only an exception type and message. */
    error         nvarchar(1000) NULL,
    sent_at       datetimeoffset(3) NULL,
    created_at    datetimeoffset(3) NOT NULL CONSTRAINT notifications_created_default DEFAULT SYSDATETIMEOFFSET(),
    updated_at    datetimeoffset(3) NOT NULL CONSTRAINT notifications_updated_default DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT notifications_kind_check CHECK (kind IN (
        'enquiry_received',
        'listing_approved',
        'listing_rejected'
    )),
    CONSTRAINT notifications_channel_check CHECK (channel IN ('email', 'sms')),
    CONSTRAINT notifications_subject_type_check CHECK (subject_type IN ('enquiry', 'listing')),
    CONSTRAINT notifications_status_check CHECK (status IN ('pending', 'sent', 'failed')),
    CONSTRAINT notifications_recipient_not_blank CHECK (LEN(LTRIM(RTRIM(recipient))) > 0),
    CONSTRAINT notifications_recipient_length CHECK (LEN(recipient) <= 254),
    /* An email recipient follows the same rule as `enquiries.email`: lowercased, with an @ in it.
       Whitespace is refused outright because a newline in an address is how a header injection
       reaches a provider's API. PostgreSQL said "no whitespace" with the regex class \s; here each
       of the four characters that can appear in an address is tested by name. */
    CONSTRAINT notifications_email_format CHECK (
        channel <> 'email'
        OR (CHARINDEX('@', recipient) > 1
            AND recipient COLLATE Latin1_General_CS_AS = LOWER(recipient) COLLATE Latin1_General_CS_AS
            AND CHARINDEX(' ', recipient) = 0
            AND CHARINDEX(CHAR(9), recipient) = 0
            AND CHARINDEX(CHAR(10), recipient) = 0
            AND CHARINDEX(CHAR(13), recipient) = 0)
    ),
    CONSTRAINT notifications_error_length CHECK (error IS NULL OR LEN(error) <= 1000),
    CONSTRAINT notifications_attempts_not_negative CHECK (attempts >= 0),
    /* The three states are kept honest by the schema rather than by the API remembering:
         pending - never attempted, so no timestamp, no error, no attempts
         sent    - has the moment it went out, and no error
         failed  - has a reason, and no sent_at */
    CONSTRAINT notifications_pending_is_untouched CHECK (
        status <> 'pending' OR (sent_at IS NULL AND error IS NULL AND attempts = 0)
    ),
    CONSTRAINT notifications_sent_has_moment CHECK (
        status <> 'sent' OR (sent_at IS NOT NULL AND error IS NULL AND attempts > 0)
    ),
    CONSTRAINT notifications_failed_has_reason CHECK (
        status <> 'failed' OR (error IS NOT NULL AND sent_at IS NULL AND attempts > 0)
    )
);

/* What has not gone out, oldest first: the one question this table is here to answer.
   Filtered, because 'sent' rows will be almost all of them and none are interesting here. The
   PostgreSQL original filtered on `status <> 'sent'`; SQL Server allows only simple comparisons in a
   filtered index, so the two states that are not 'sent' are listed instead. Same rows, and it has to
   be kept in step with notifications_status_check if a fourth state is ever added. */
CREATE INDEX notifications_unsent_idx ON notifications (status, created_at)
    WHERE status IN ('pending', 'failed');

-- Everything sent about one enquiry or one listing, when looking into a single complaint.
CREATE INDEX notifications_subject_idx ON notifications (subject_type, subject_id);

-- One owner's notification history, newest first.
CREATE INDEX notifications_user_idx ON notifications (user_id, created_at DESC);
