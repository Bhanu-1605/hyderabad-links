namespace BusinessDirectory.Api.Auth;

/// <summary>
/// The permission codes seeded by database/seeds/reference/R003_roles_permissions.sql, split by which
/// surface may check them. Endpoints name a constant from here instead of a loose string, so a typo is a
/// build error rather than a permission that silently never matches.
///
/// The split matters: the ordinary admin surface (/api/v1/admin) must never gate on a super-admin
/// permission, because that would make a super-admin-only capability reachable with an admin token.
/// Super-admin work belongs on /api/v1/super-admin with its own token audience.
/// </summary>
public static class AdminPermissions
{
    // ---- ordinary admin work
    public const string ListingsView = "listings.view";
    public const string ListingsCreate = "listings.create";
    public const string ListingsEdit = "listings.edit";
    public const string ListingsReview = "listings.review";
    public const string ListingsVerify = "listings.verify";
    public const string ReportsHandle = "reports.handle";
    public const string UsersView = "users.view";

    // ---- super-admin only (listed so the separation is visible in one place; not checked here)
    public const string AnalyticsView = "analytics.view";
    public const string DataExport = "data.export";
    public const string PlansManage = "plans.manage";
    public const string AdminsManage = "admins.manage";
    public const string AuditView = "audit.view";

    /// <summary>Permissions an /api/v1/admin endpoint is allowed to require.</summary>
    public static readonly string[] AdminSurface =
    [
        ListingsView, ListingsCreate, ListingsEdit, ListingsReview, ListingsVerify, ReportsHandle, UsersView,
    ];

    /// <summary>Permissions reserved for the super-admin surface.</summary>
    public static readonly string[] SuperAdminOnly =
    [
        AnalyticsView, DataExport, PlansManage, AdminsManage, AuditView,
    ];

    /// <summary>Guards the rule above at the point of use, so a mistake shows up as a failing test, not a hole.</summary>
    public static bool IsAdminSurface(string permission) => AdminSurface.Contains(permission);
}
