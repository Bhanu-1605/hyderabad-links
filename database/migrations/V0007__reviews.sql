-- V0007: customer reviews and ratings. Additive only.
--
-- Decision D11: a reviewer must be a phone-OTP-verified user, and every review is moderated before
-- it appears. Both are enforced here as well as in the API - the reviewer is a real `users` row
-- (the same table the owner sign-in creates), and a review starts as 'pending'.
--
-- One review per person per listing, enforced by a unique constraint rather than by the API, so a
-- double submit or a race cannot produce two.

CREATE TABLE reviews (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    listing_id          bigint NOT NULL REFERENCES listings (id) ON DELETE CASCADE,
    user_id             bigint NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    rating              smallint NOT NULL,
    body                text NOT NULL,
    status              text NOT NULL DEFAULT 'pending',
    -- Why a moderator rejected it. Shown to the reviewer, never to the public.
    moderation_note     text,
    moderated_at        timestamptz,
    moderated_by_admin_id bigint REFERENCES admin_users (id),
    created_at          timestamptz NOT NULL DEFAULT now(),
    updated_at          timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT reviews_one_per_person UNIQUE (listing_id, user_id),
    CONSTRAINT reviews_rating_range CHECK (rating BETWEEN 1 AND 5),
    CONSTRAINT reviews_status_check CHECK (status IN ('pending', 'approved', 'rejected')),
    CONSTRAINT reviews_body_length CHECK (char_length(btrim(body)) BETWEEN 20 AND 2000),
    CONSTRAINT reviews_note_length CHECK (moderation_note IS NULL OR char_length(moderation_note) <= 1000)
);

-- The public view of a listing: approved reviews, newest first.
CREATE INDEX reviews_listing_approved_idx ON reviews (listing_id, created_at DESC) WHERE status = 'approved';

-- The moderation queue: oldest first, one status at a time.
CREATE INDEX reviews_status_created_idx ON reviews (status, created_at);

-- A reviewer's own reviews, so they can see what happened to them.
CREATE INDEX reviews_user_idx ON reviews (user_id);

COMMENT ON TABLE reviews IS
    'Customer reviews. D11: the reviewer must be a phone-verified user and every review is moderated.';
