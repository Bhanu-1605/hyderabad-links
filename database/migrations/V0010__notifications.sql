-- V0010: the record of every notification we tried to send. Additive only.
--
-- Until now nothing was sent to anybody, and nothing recorded that. An owner was never told about an
-- enquiry, and nobody was told about an approval or a rejection.
--
-- Sending is deliberately allowed to fail: an enquiry that was stored matters more than an email
-- that went out, so the API records the enquiry, commits, and only then attempts delivery. That is
-- exactly why this table exists. Without it an unconfigured provider, a bounced address or a dead
-- API key would be a silent nothing, and the first person to notice would be an owner wondering why
-- they never hear about their customers.
--
-- This is a log of attempts, not a queue. Nothing retries from it yet; a row sitting at 'failed' is
-- a thing for a person to look at. A retry worker can be added later without changing the shape.

CREATE TABLE notifications (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    -- Which event caused it. Adding a kind later means extending this constraint, which is the point:
    -- a typo in the API cannot invent a notification type nobody handles.
    kind          text NOT NULL,
    -- Email today. SMS is listed because the OTP sender already exists and will be the second channel.
    channel       text NOT NULL DEFAULT 'email',
    -- The address or number it was sent to, as it was at the time. Kept here rather than read back
    -- from `users` so the record still says where it actually went after someone edits their profile.
    recipient     text NOT NULL,
    /* The owner being notified, when they have an account. Null for a recipient who is not a user -
       a customer confirmation would be one. ON DELETE SET NULL keeps the delivery record when an
       account goes away. */
    user_id       bigint REFERENCES users (id) ON DELETE SET NULL,
    -- What it was about. The same column pair as `reports`, for the same reason: a third kind of
    -- subject later needs no new column.
    subject_type  text NOT NULL,
    subject_id    bigint NOT NULL,
    status        text NOT NULL DEFAULT 'pending',
    -- How many delivery attempts have been made. 0 while pending.
    attempts      integer NOT NULL DEFAULT 0,
    /* Why it failed, short and in plain words. Never a provider API key, a token or a password:
       whatever writes here must pass only an exception type and message. */
    error         text,
    sent_at       timestamptz,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT notifications_kind_check CHECK (kind IN (
        'enquiry_received',
        'listing_approved',
        'listing_rejected'
    )),
    CONSTRAINT notifications_channel_check CHECK (channel IN ('email', 'sms')),
    CONSTRAINT notifications_subject_type_check CHECK (subject_type IN ('enquiry', 'listing')),
    CONSTRAINT notifications_status_check CHECK (status IN ('pending', 'sent', 'failed')),
    CONSTRAINT notifications_recipient_not_blank CHECK (btrim(recipient) <> ''),
    CONSTRAINT notifications_recipient_length CHECK (char_length(recipient) <= 254),
    /* An email recipient follows the same rule as `enquiries.email`: lowercased, with an @ in it.
       Whitespace is refused outright because a newline in an address is how a header injection
       reaches a provider's API. */
    CONSTRAINT notifications_email_format CHECK (
        channel <> 'email'
        OR (position('@' in recipient) > 1 AND recipient = lower(recipient) AND recipient !~ '\s')
    ),
    CONSTRAINT notifications_error_length CHECK (error IS NULL OR char_length(error) <= 1000),
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

-- What has not gone out, oldest first: the one question this table is here to answer.
-- Partial, because 'sent' rows will be almost all of them and none of them are interesting here.
CREATE INDEX notifications_unsent_idx ON notifications (status, created_at)
    WHERE status <> 'sent';

-- Everything sent about one enquiry or one listing, when looking into a single complaint.
CREATE INDEX notifications_subject_idx ON notifications (subject_type, subject_id);

-- One owner's notification history, newest first.
CREATE INDEX notifications_user_idx ON notifications (user_id, created_at DESC);

COMMENT ON TABLE notifications IS
    'Every notification we attempted. A log of attempts, not a queue: nothing retries from it yet, and a failed row is for a person to look at.';
COMMENT ON COLUMN notifications.recipient IS
    'The address as it was when we sent, so the record still says where it went after a profile edit.';
COMMENT ON COLUMN notifications.error IS
    'Plain reason for a failure. Never a provider key, token or password.';
