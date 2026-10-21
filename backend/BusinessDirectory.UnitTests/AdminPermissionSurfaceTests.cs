using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.UnitTests;

// The two admin surfaces must not overlap: an ordinary admin endpoint may never gate on a
// super-admin permission, and the reverse. These tests guard that rule at the point where it is
// easiest to break — someone adding a permission to the wrong list.
public class AdminPermissionSurfaceTests
{
    [Fact]
    public void The_two_surfaces_share_no_permission()
    {
        var shared = AdminPermissions.AdminSurface.Intersect(AdminPermissions.SuperAdminOnly).ToArray();

        Assert.Empty(shared);
    }

    [Fact]
    public void A_super_admin_permission_is_refused_on_the_admin_surface_even_to_someone_who_holds_it()
    {
        // The account holds every permission there is, which is what a super admin looks like.
        var everything = new AdminIdentity(
            1, "super@example.com", "Super", ["super_admin"],
            [.. AdminPermissions.AdminSurface, .. AdminPermissions.SuperAdminOnly]);

        foreach (var permission in AdminPermissions.SuperAdminOnly)
        {
            Assert.NotNull(AdminAccess.Authorize(everything, permission));
        }
    }

    [Fact]
    public void An_admin_permission_is_refused_on_the_super_admin_surface_even_to_someone_who_holds_it()
    {
        var everything = new AdminIdentity(
            1, "super@example.com", "Super", ["super_admin"],
            [.. AdminPermissions.AdminSurface, .. AdminPermissions.SuperAdminOnly]);

        foreach (var permission in AdminPermissions.AdminSurface)
        {
            Assert.NotNull(AdminAccess.AuthorizeSuper(everything, permission));
        }
    }

    [Fact]
    public void Each_surface_allows_its_own_permissions_to_someone_who_holds_them()
    {
        var admin = new AdminIdentity(1, "a@example.com", "Admin", ["admin"], AdminPermissions.AdminSurface);
        var super = new AdminIdentity(2, "s@example.com", "Super", ["super_admin"], AdminPermissions.SuperAdminOnly);

        Assert.All(AdminPermissions.AdminSurface, permission => Assert.Null(AdminAccess.Authorize(admin, permission)));
        Assert.All(AdminPermissions.SuperAdminOnly, permission => Assert.Null(AdminAccess.AuthorizeSuper(super, permission)));
    }

    [Fact]
    public void Holding_nothing_is_refused_everywhere()
    {
        var nobody = new AdminIdentity(3, "n@example.com", "Nobody", [], []);

        Assert.All(AdminPermissions.AdminSurface, permission => Assert.NotNull(AdminAccess.Authorize(nobody, permission)));
        Assert.All(AdminPermissions.SuperAdminOnly, permission => Assert.NotNull(AdminAccess.AuthorizeSuper(nobody, permission)));
    }

    [Fact]
    public void An_unknown_permission_string_is_refused_on_both_surfaces()
    {
        // A typo must fail closed, never open.
        var admin = new AdminIdentity(1, "a@example.com", "Admin", ["admin"], ["listings.reveiw"]);

        Assert.NotNull(AdminAccess.Authorize(admin, "listings.reveiw"));
        Assert.NotNull(AdminAccess.AuthorizeSuper(admin, "listings.reveiw"));
    }
}
