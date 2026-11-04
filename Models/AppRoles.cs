using System.Security.Claims;

namespace HeimevernetInnlevering1.Models;

public static class AppRoles
{
    public const string OffentligAktor = "OffentligAktor";
    public const string Ressursleverandor = "Ressursleverandor";
    public const string DispatchTeam = "DispatchTeam";
    public const string Kriseledelse = "Kriseledelse";
    public const string Verifiseringsansvarlig = "Verifiseringsansvarlig";

    public const string Any = OffentligAktor + "," + Ressursleverandor + "," + DispatchTeam + "," +
        Kriseledelse + "," + Verifiseringsansvarlig;

    public static readonly IReadOnlyList<string> All =
        [OffentligAktor, Ressursleverandor, DispatchTeam, Kriseledelse, Verifiseringsansvarlig];

    /// <summary>
    /// Roller en ny bruker kan velge selv ved registrering. Dispatch, kriseledelse og verifisering
    /// tildeles av kriseledelsen under «Brukere», slik at ingen kan gi seg selv slik tilgang.
    /// </summary>
    public static readonly IReadOnlyList<string> SelfService = [Ressursleverandor, OffentligAktor];

    public static string Label(string role) => role switch
    {
        OffentligAktor => "Offentlig aktør",
        Ressursleverandor => "Ressursleverandør",
        DispatchTeam => "Dispatch-team",
        Kriseledelse => "Kriseledelse",
        Verifiseringsansvarlig => "Verifiseringsansvarlig",
        _ => role
    };

    public static bool HasAny(ClaimsPrincipal user) => All.Any(user.IsInRole);

    /// <summary>Dispatch only sees verified, available resources unless another role needs the full overview.</summary>
    public static bool DispatchOnly(ClaimsPrincipal user) =>
        user.IsInRole(DispatchTeam) && !user.IsInRole(Kriseledelse) && !user.IsInRole(Verifiseringsansvarlig);
}
