using System.Security.Claims;

namespace HeimevernetInnlevering1.Models;

public static class AppRoles
{
    public const string OffentligAktor = "OffentligAktor";
    public const string Ressursleverandor = "Ressursleverandor";
    public const string DispatchTeam = "DispatchTeam";
    public const string Kriseledelse = "Kriseledelse";

    public const string Any = OffentligAktor + "," + Ressursleverandor + "," + DispatchTeam + "," + Kriseledelse;

    public static readonly IReadOnlyList<string> All =
        [OffentligAktor, Ressursleverandor, DispatchTeam, Kriseledelse];

    /// <summary>Roller en ny bruker kan velge selv. Dispatch og kriseledelse tildeles av kriseledelsen.</summary>
    public static readonly IReadOnlyList<string> SelfService = [Ressursleverandor, OffentligAktor];

    public static string Label(string role) => role switch
    {
        OffentligAktor => "Offentlig aktør",
        Ressursleverandor => "Ressursleverandør",
        DispatchTeam => "Dispatch-team",
        Kriseledelse => "Kriseledelse",
        _ => role
    };

    public static bool HasAny(ClaimsPrincipal user) => All.Any(user.IsInRole);

    /// <summary>Dispatch ser operativt relevante ressurser, uten lederens administrative oversikt.</summary>
    public static bool DispatchOnly(ClaimsPrincipal user) =>
        user.IsInRole(DispatchTeam) && !user.IsInRole(Kriseledelse);
}
