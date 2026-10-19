-- V0006: customer enquiries sent to a business through its listing page. Additive only.
--
-- Until now the only way to contact a business was the Call and WhatsApp buttons, which leave no
-- record: the owner cannot see who tried to reach them and nothing can be followed up. An enquiry is
-- that record.
--
-- The sender is not a user of the site. They are a member of the public who filled in a form, so
-- there is no users row and no account - just the contact details they chose to give.

CREATE TABLE enquiries (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    listing_id   bigint NOT NULL REFERENCES listings (id) ON DELETE CASCADE,
    name         text NOT NULL,
    phone_e164   text NOT NULL,
    email        text,
    message      text NOT NULL,
    status       text NOT NULL DEFAULT 'new',
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    -- Where it came from, for abuse control only. Never shown to the business owner.
    ip           inet,
    CONSTRAINT enquiries_status_check CHECK (status IN ('new', 'read', 'replied', 'closed', 'spam')),
    CONSTRAINT enquiries_name_not_blank CHECK (btrim(name) <> ''),
    CONSTRAINT enquiries_name_length CHECK (char_length(btrim(name)) <= 100),
    CONSTRAINT enquiries_message_length CHECK (char_length(btrim(message)) BETWEEN 10 AND 2000),
    CONSTRAINT enquiries_phone_format CHECK (phone_e164 ~ '^\+[1-9][0-9]{7,14}$'),
    CONSTRAINT enquiries_email_format CHECK (email IS NULL OR (position('@' in email) > 1 AND email = lower(email)))
);

-- The owner's inbox: newest first, for one listing or for all the listings they own.
CREATE INDEX enquiries_listing_created_idx ON enquiries (listing_id, created_at DESC);

-- Counting unread enquiries for the dashboard, without scanning the whole table.
CREATE INDEX enquiries_new_idx ON enquiries (listing_id) WHERE status = 'new';

-- Per-sender rate limiting, the same approach otp_challenges uses.
CREATE INDEX enquiries_ip_created_idx ON enquiries (ip, created_at);

COMMENT ON TABLE enquiries IS
    'Messages sent to a business through its public listing page. The sender has no account.';
