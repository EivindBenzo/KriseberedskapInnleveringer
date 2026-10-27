using System.Security.Claims;
using HeimevernetInnlevering1.Models;
using Xunit;

namespace MariaDbIntegrationTests;

public class ResourceAuthorizationTests
{
    [Theory]
    [InlineData("owner", "owner", AppRoles.Ressursleverandor, true)]
    [InlineData("other", "owner", AppRoles.Ressursleverandor, false)]
    [InlineData("owner", null, AppRoles.Ressursleverandor, false)]
    [InlineData("owner", "owner", AppRoles.OffentligAktor, false)]
    [InlineData("owner", "owner", AppRoles.DispatchTeam, false)]
    [InlineData("owner", "owner", AppRoles.Verifiseringsansvarlig, false)]
    [InlineData("owner", "owner", AppRoles.Kriseledelse, false)]
    [InlineData("owner", "owner", "Admin", false)]
    public void OnlyTheProviderWhoOwnsTheResourceCanManageIt(string userId, string? ownerId, string role, bool expected)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Role, role) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        Assert.Equal(expected, ResourcePermissions.CanManage(user, ownerId));
    }

    [Fact]
    public void AnonymousCannotManageEvenWithMatchingClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, AppRoles.Ressursleverandor)]));
        Assert.False(ResourcePermissions.CanManage(user, "owner"));
    }
}
