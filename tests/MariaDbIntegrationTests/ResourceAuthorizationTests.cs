using System.Security.Claims;
using HeimevernetInnlevering1.Models;
using Xunit;

namespace MariaDbIntegrationTests;

public class ResourceAuthorizationTests
{
    [Theory]
    [InlineData("owner", "owner", false, true)]
    [InlineData("other", "owner", false, false)]
    [InlineData("other", "owner", true, true)]
    [InlineData("owner", null, false, false)]
    [InlineData("admin", null, true, true)]
    public void OnlyOwnerOrAdminCanManage(string userId, string? ownerId, bool admin, bool expected)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        Assert.Equal(expected, ResourcePermissions.CanManage(user, ownerId));
    }

    [Fact]
    public void AnonymousCannotManageEvenWithMatchingClaim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, "Admin")]));
        Assert.False(ResourcePermissions.CanManage(user, "owner"));
    }
}
