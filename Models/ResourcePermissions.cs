using System.Security.Claims;

namespace HeimevernetInnlevering1.Models;

public static class ResourcePermissions
{
    /// <summary>BrukerID til innlogget bruker, eller null om brukeren ikke er logget inn.</summary>
    public static int? BrukerId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Bare ressursleverandøren som eier ressursen kan endre eller slette den.</summary>
    public static bool CanManage(ClaimsPrincipal user, int eierId) =>
        user.Identity?.IsAuthenticated == true &&
        user.IsInRole(AppRoles.Ressursleverandor) &&
        BrukerId(user) == eierId;
}
