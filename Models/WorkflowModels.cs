using System.Linq.Expressions;

namespace HeimevernetInnlevering1.Models;

public class WorkflowOptions
{
    /// <summary>Hvor mange dager en godkjenning (Verifisering med resultat «godkjent») gjelder.</summary>
    public int VerificationValidityDays { get; set; } = 30;
}

/// <summary>Regler for når en ressurs kan forespørres. Brukes både i databasespørringer og i minnet.</summary>
public static class WorkflowEligibility
{
    public const int StandardGyldighetDager = 30;

    /// <summary>Siste godkjenning for ressursen (krever at Verifiseringer er lastet).</summary>
    public static DateTime? SisteGodkjenning(Ressurs ressurs) =>
        ressurs.Verifiseringer.Where(v => v.Resultat == Statuser.Resultat.Godkjent)
            .Select(v => (DateTime?)v.Tidspunkt).Max();

    public static DateTime? GodkjentTil(Ressurs ressurs, int dager = StandardGyldighetDager) =>
        SisteGodkjenning(ressurs)?.AddDays(dager);

    public static bool HasCurrentApproval(Ressurs ressurs, DateTime nowUtc, int dager = StandardGyldighetDager) =>
        SisteGodkjenning(ressurs) is DateTime godkjent && godkjent <= nowUtc && godkjent.AddDays(dager) > nowUtc;

    public static bool IsEligible(Ressurs ressurs, DateTime nowUtc, int dager = StandardGyldighetDager) =>
        ressurs.Status == Statuser.Ressurs.Tilgjengelig && ressurs.TilgjengeligAntall > 0 &&
        HasCurrentApproval(ressurs, nowUtc, dager);

    /// <summary>Samme regel som <see cref="IsEligible"/>, skrevet slik at EF Core kan oversette den til SQL.</summary>
    public static Expression<Func<Ressurs, bool>> EligibleExpression(DateTime nowUtc, int dager = StandardGyldighetDager)
    {
        var grense = nowUtc.AddDays(-dager);
        return r => r.Status == Statuser.Ressurs.Tilgjengelig && r.Antall > r.ReservertAntall &&
                    r.Verifiseringer.Any(v => v.Resultat == Statuser.Resultat.Godkjent &&
                                              v.Tidspunkt > grense && v.Tidspunkt <= nowUtc);
    }

    /// <summary>Ressurser verifiseringsansvarlig skal se på: nye, returnerte, eller med utløpt godkjenning.</summary>
    public static Expression<Func<Ressurs, bool>> TrengerVerifisering(DateTime nowUtc, int dager = StandardGyldighetDager)
    {
        var grense = nowUtc.AddDays(-dager);
        return r => r.Status == Statuser.Ressurs.VenterPåVerifisering || r.Status == Statuser.Ressurs.Returnert ||
                    ((r.Status == Statuser.Ressurs.Tilgjengelig || r.Status == Statuser.Ressurs.Utilgjengelig) &&
                     !r.Verifiseringer.Any(v => v.Resultat == Statuser.Resultat.Godkjent && v.Tidspunkt > grense));
    }
}

public static class WorkflowTransitions
{
    /// <summary>Lovlige overganger for Behov.Status som kriseledelsen kan gjøre manuelt.</summary>
    public static bool CanMoveNeed(string from, string to) => (from, to) switch
    {
        (Statuser.Behov.Ny, Statuser.Behov.Vurderes) => true,
        (Statuser.Behov.Ny or Statuser.Behov.Vurderes or Statuser.Behov.Tildelt, Statuser.Behov.Løst) => true,
        _ => false
    };

    public static bool IsActive(string status) => Statuser.Behov.Aktive.Contains(status);

    /// <summary>Fremdriften i en tildeling går ett steg om gangen: bekreftet → på vei → fremme → fullført.</summary>
    public static bool CanMoveAssignment(string from, string to)
    {
        var steg = Array.IndexOf(Statuser.Fremdrift.Rekkefølge, from);
        return steg >= 0 && steg < Statuser.Fremdrift.Rekkefølge.Length - 1 &&
               Statuser.Fremdrift.Rekkefølge[steg + 1] == to;
    }

    public static string? NextAssignmentStep(string from)
    {
        var steg = Array.IndexOf(Statuser.Fremdrift.Rekkefølge, from);
        return steg >= 0 && steg < Statuser.Fremdrift.Rekkefølge.Length - 1 ? Statuser.Fremdrift.Rekkefølge[steg + 1] : null;
    }

    public static string Label(string? status) => Statuser.Visning(status);

    /// <summary>Sorteringsrekkefølge: akutte behov først.</summary>
    public static int PriorityRank(string prioritet) => prioritet == Statuser.Prioritet.Akutt ? 0 : 1;
}

public class OperationsViewModel
{
    public IReadOnlyList<Ressurs> Resources { get; set; } = [];
    public IReadOnlyList<Behov> Needs { get; set; } = [];
    public IReadOnlyList<Forespørsel> Requests { get; set; } = [];
    public IReadOnlyList<Tildeling> Assignments { get; set; } = [];
    public IReadOnlyList<Hendelseslogg> Audit { get; set; } = [];
    public IReadOnlyDictionary<int, string> ActorNames { get; set; } = new Dictionary<int, string>();
}

public record TimelineEntry(string Label, string Actor, DateTime OccurredAtUtc);

public static class WorkflowLabels
{
    public static string Action(string action) => action switch
    {
        "RessursRegistrert" => "Registrert",
        "RessursGodkjent" => "Verifisert",
        "RessursAvvist" => "Avvist ved verifisering",
        "RessursEndret" => "Endret, venter på ny verifisering",
        "RessursSlettet" => "Slettet",
        "TilgjengelighetEndret" => "Tilgjengelighet endret",
        "ForespørselSendt" => "Forespørsel sendt",
        "ForespørselGodtatt" => "Godtatt av eier",
        "ForespørselAvslått" => "Avslått av eier",
        "ForespørselAvbrutt" => "Forespørsel avbrutt",
        "Tildelt" => "Tildelt",
        "FremdriftPåVei" => "På vei",
        "FremdriftFremme" => "Fremme",
        "FremdriftFullført" => "Fullført",
        "BehovRegistrert" => "Behov registrert",
        "BehovStatus" => "Behovsstatus endret",
        "BehovPrioritet" => "Prioritet endret",
        "HendelseOpprettet" => "Hendelse opprettet",
        "RessurstypeOpprettet" => "Ny ressurstype",
        "BehovstypeOpprettet" => "Ny behovstype",
        "HendelseAvsluttet" => "Hendelse avsluttet",
        _ => action
    };
}
