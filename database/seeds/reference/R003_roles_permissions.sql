-- Reference seed: admin roles and permissions. Idempotent. Re-running grants any newly added permission to super_admin.
-- Nobody can change their own role (enforced in the application). The first super_admin is created by a CLI bootstrap.

INSERT INTO permissions (code, description) VALUES
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
ON CONFLICT (code) DO NOTHING;

INSERT INTO roles (slug, name, description, is_system) VALUES
  ('admin',       'Admin',       'Day-to-day listing and moderation work', true),
  ('super_admin', 'Super admin', 'Full access, including admins, plans and audit log', true)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r CROSS JOIN permissions p
WHERE r.slug = 'super_admin'
ON CONFLICT DO NOTHING;

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p
  ON p.code IN ('listings.view', 'listings.create', 'listings.edit', 'listings.review', 'listings.verify', 'reports.handle', 'users.view')
WHERE r.slug = 'admin'
ON CONFLICT DO NOTHING;
