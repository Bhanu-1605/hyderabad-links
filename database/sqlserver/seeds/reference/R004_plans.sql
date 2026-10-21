-- The four subscription plans (decision D23). Reference data, re-applied on every migrate, so it is
-- written to be idempotent and never to overwrite an allowance the owner has since corrected.
--
-- Prices are APPROVED (D23). Media allowances are PROVISIONAL: requirements.md documents
-- Free 0/0, Basic 4/0, Gold 4/2, Premium 16/4, but D23 renamed the middle tier from "Gold" to
-- "Advanced", so placing Gold's figures on Advanced is an inference. media_limits_confirmed stays 0
-- until the owner confirms, and the application tells the owner they are provisional rather than
-- presenting them as settled.
--
-- No plan grants verification: requirements.md states a paid plan must never do so automatically.
--
-- The SQL Server counterpart of database/seeds/reference/R004_plans.sql. PostgreSQL did this with
-- ON CONFLICT ... DO UPDATE; here the update and the insert are two statements over the same values,
-- which says the same thing and keeps the "never touch a confirmed allowance" rule visible.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

WITH wanted AS (
    SELECT *
    FROM (VALUES
        ('free',     'Free',     CAST(0 AS bigint),        0,  0, 0, 0),
        ('basic',    'Basic',    CAST(69900 AS bigint),    4,  0, 1, 1),
        ('advanced', 'Advanced', CAST(699900 AS bigint),   4,  2, 1, 2),
        ('premium',  'Premium',  CAST(3699900 AS bigint), 16,  4, 1, 3)
    ) AS v(slug, name, price_paise, photo_limit, video_limit, logo_limit, sort_order)
)
-- Name, price and ordering are kept current. The media allowances and the badge flag are
-- deliberately NOT updated: once the owner sets a real number, re-running this must not put the
-- provisional one back.
UPDATE p
SET name = w.name,
    price_paise = w.price_paise,
    sort_order = w.sort_order,
    updated_at = SYSDATETIMEOFFSET()
FROM plans p
JOIN wanted w ON w.slug = p.slug
WHERE p.name <> w.name OR p.price_paise <> w.price_paise OR p.sort_order <> w.sort_order;
GO

INSERT INTO plans (slug, name, price_paise, photo_limit, video_limit, logo_limit,
                   grants_verified_badge, media_limits_confirmed, sort_order)
SELECT v.slug, v.name, v.price_paise, v.photo_limit, v.video_limit, v.logo_limit, 0, 0, v.sort_order
FROM (VALUES
    ('free',     'Free',     CAST(0 AS bigint),        0,  0, 0, 0),
    ('basic',    'Basic',    CAST(69900 AS bigint),    4,  0, 1, 1),
    ('advanced', 'Advanced', CAST(699900 AS bigint),   4,  2, 1, 2),
    ('premium',  'Premium',  CAST(3699900 AS bigint), 16,  4, 1, 3)
) AS v(slug, name, price_paise, photo_limit, video_limit, logo_limit, sort_order)
WHERE NOT EXISTS (SELECT 1 FROM plans p WHERE p.slug = v.slug);
GO
