-- Reference seed: admin roles and permissions. Idempotent. Re-running grants any newly added
-- permission to super_admin. Nobody can change their own role (enforced in the application), and the
-- first super_admin is created by a CLI bootstrap.
--
-- The SQL Server counterpart of database/seeds/reference/R003_roles_permissions.sql.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

INSERT INTO permissions (code, description)
SELECT v.code, v.description
FROM (VALUES
('listings.view',    'View all listings'),
  ('listings.create',  'Add listings manually'),
  ('listings.edit',    'Edit any listing'),
  ('listings.review',  'Approve or reject listings'),
  ('listings.verify',  'Mark listings verified'),
  ('reports.handle',   'Handle abuse reports and reviews'),
  ('users.view',       'View business owner accounts'),
  ('analytics.view',   'View analytics dashboards'),
  ('data.export',      'Use the filter explorer and export CSV'),
  ('plans.manage',     'Manage plans, entitlements and pricing'),
  ('admins.manage',    'Invite admins and manage roles'),
  ('audit.view',       'View the audit log')
) AS v(code, description)
WHERE NOT EXISTS (SELECT 1 FROM permissions p WHERE p.code = v.code);
GO

INSERT INTO roles (slug, name, description, is_system)
SELECT v.slug, v.name, v.description, v.is_system
FROM (VALUES
('admin',       'Admin',       'Day-to-day listing and moderation work', 1),
  ('super_admin', 'Super admin', 'Full access, including admins, plans and audit log', 1)
) AS v(slug, name, description, is_system)
WHERE NOT EXISTS (SELECT 1 FROM roles r WHERE r.slug = v.slug);
GO

-- The super admin holds everything there is, including any permission added later.
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.slug = 'super_admin'
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );
GO

-- The ordinary admin holds the day-to-day ones only. This list is deliberately explicit: a new
-- permission does not reach an admin until someone adds it here.
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p ON p.code IN (
    'listings.view', 'listings.create', 'listings.edit', 'listings.review',
    'listings.verify', 'reports.handle', 'users.view'
)
WHERE r.slug = 'admin'
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );
GO
