using System.Security.Claims;
using HeimevernetInnlevering1.Models;
using Xunit;

namespace MariaDbIntegrationTests;

/// <summary>Bare ressursleverandøren som eier ressursen kan endre eller slette den.</summary>
public class ResourceAuthorizationTests
{
    [Theory]
    [InlineData(7, 7, AppRoles.Ressursleverandor, true)]
    [InlineData(8, 7, AppRoles.Ressursleverandor, false)]
    [InlineData(7, 7, AppRoles.OffentligAktor, false)]
    [InlineData(7, 7, AppRoles.DispatchTeam, false)]
    [InlineData(7, 7, AppRoles.Verifiseringsansvarlig, false)]
    [InlineData(7, 7, AppRoles.Kriseledelse, false)]
    public void OnlyTheProviderWhoOwnsTheResourceCanManageIt(int userId, int eierId, string role, bool expected)
    {
        var user = TestDatabase.Principal(userId, role);
        Assert.Equal(expected, ResourcePermissions.CanManage(user, eierId));
    }

    [Fact]
    public void AnonymousCannotManageEvenWithMatchingClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, AppRoles.Ressursleverandor)]));
        Assert.False(ResourcePermissions.CanManage(user, 7));
    }
}
