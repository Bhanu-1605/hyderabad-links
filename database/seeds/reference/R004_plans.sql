-- The four subscription plans (decision D23). Reference data, re-applied on every migrate, so it is
-- written to be idempotent and never to overwrite an allowance the owner has since corrected.
--
-- Prices are APPROVED (D23). Media allowances are PROVISIONAL: requirements.md documents
-- Free 0/0, Basic 4/0, Gold 4/2, Premium 16/4, but D23 renamed the middle tier from "Gold" to
-- "Advanced", so placing Gold's figures on Advanced is an inference. `media_limits_confirmed`
-- stays false until the owner confirms, and the application tells the owner they are provisional
-- rather than presenting them as settled.
--
-- No plan grants verification: requirements.md states a paid plan must never do so automatically.

INSERT INTO plans (slug, name, price_paise, photo_limit, video_limit, logo_limit, grants_verified_badge, media_limits_confirmed, sort_order)
VALUES
  ('free',     'Free',     0,         0,  0, 0, false, false, 0),
  ('basic',    'Basic',    69900,     4,  0, 1, false, false, 1),
  ('advanced', 'Advanced', 699900,    4,  2, 1, false, false, 2),
  ('premium',  'Premium',  3699900,  16,  4, 1, false, false, 3)
ON CONFLICT (slug) DO UPDATE
  SET name = EXCLUDED.name,
      price_paise = EXCLUDED.price_paise,
      sort_order = EXCLUDED.sort_order,
      updated_at = now()
  -- Deliberately NOT updating the media allowances or the badge flag: once the owner sets a real
  -- number, re-running the seed must not put the provisional one back.
  WHERE plans.media_limits_confirmed = false
     OR plans.name IS DISTINCT FROM EXCLUDED.name
     OR plans.price_paise IS DISTINCT FROM EXCLUDED.price_paise;
