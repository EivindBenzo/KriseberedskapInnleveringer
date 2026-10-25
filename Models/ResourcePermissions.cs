using System.Security.Claims;

namespace HeimevernetInnlevering1.Models;

public static class ResourcePermissions
{
    public static bool CanManage(ClaimsPrincipal user, string? ownerId) =>
        user.Identity?.IsAuthenticated == true &&
        (user.IsInRole("Admin") ||
         (ownerId is not null && ownerId == user.FindFirstValue(ClaimTypes.NameIdentifier)));
}
