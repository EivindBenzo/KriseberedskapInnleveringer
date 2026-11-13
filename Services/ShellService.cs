using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Services;

public record VarselElement(string Kategori, string Tekst, string Detalj, string Controller, string Action, DateTime Tid);

public class ShellModel
{
    public string Navn { get; init; } = "";
    public string Initialer { get; init; } = "?";
    public IReadOnlyList<VarselElement> Varsler { get; init; } = [];
    public int AntallUlest { get; init; }
    public bool HarBilde { get; init; }
    public string BildeVersjon { get; init; } = "";
    public IReadOnlyList<string> Nøkler { get; init; } = [];
    public int Forespørsler { get; init; }
    public int Meldinger { get; init; }
    public int AktiveHendelser { get; init; }
}

/// <summary>
/// Samler det toppfeltet og sidemenyen trenger: varsler (bjellen) og tellere. Varslene hentes fra databasen og
/// filtreres etter valgene brukeren har lagret under «Varsler og preferanser».
/// </summary>
public class ShellService(AppDbContext db, UserManager<Bruker> users)
{
    public const string Provider = "Profil";
    public const string VarselToken = "Varsler";
    public const string VaktToken = "Vaktstatus";
    public const string SettToken = "VarselSett";

    public static string Nøkkel(VarselElement v) => v.Kategori + "|" + v.Tekst;

    public static string Menneskelig(string? tekst)
    {
        if (string.IsNullOrEmpty(tekst)) return "";
        var s = System.Text.RegularExpressions.Regex.Replace(tekst, @"(?<=\p{Ll})(?=\p{Lu})", " ");
        return s.Length > 1 ? s[..1] + s[1..].ToLowerInvariant() : s;
    }

    public static readonly (string Nøkkel, string Tittel, string Beskrivelse)[] Kategorier =
    [
        ("foresporsler", "Nye forespørsler", "Når en forespørsel venter på svar eller behandling."),
        ("akutt", "Akutte behov", "Når et akutt behov er registrert og ikke løst."),
        ("status", "Statusendringer", "Når svar, tildelinger eller behov du følger endres."),
        ("meldinger", "Meldinger", "Når du får en ny melding i portalen.")
    ];

    public static readonly (string Verdi, string Tekst)[] Vaktstatuser =
        [("pa", "På vakt"), ("beredskap", "I beredskap"), ("av", "Av vakt")];

    private ShellModel? _cache;

    public static Dictionary<string, bool> LesValg(string? lagret)
    {
        var valg = Kategorier.ToDictionary(k => k.Nøkkel, _ => true);
        foreach (var del in (lagret ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var deler = del.Split('=');
            if (deler.Length == 2 && valg.ContainsKey(deler[0])) valg[deler[0]] = deler[1] == "1";
        }
        return valg;
    }

    public static string SkrivValg(IEnumerable<string> på) =>
        string.Join(';', Kategorier.Select(k => $"{k.Nøkkel}={(på.Contains(k.Nøkkel) ? 1 : 0)}"));

    public async Task<ShellModel> HentAsync(ClaimsPrincipal principal)
    {
        if (_cache is not null) return _cache;
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || await users.FindByIdAsync(id.ToString()) is not { } user)
            return _cache = new ShellModel();

        var valg = LesValg(await users.GetAuthenticationTokenAsync(user, Provider, VarselToken));
        var siden = DateTime.UtcNow.AddDays(-7);
        var driftsrolle = principal.IsInRole(AppRoles.DispatchTeam) || principal.IsInRole(AppRoles.Kriseledelse);
        var varsler = new List<VarselElement>();

        var ventende = 0;
        if (driftsrolle)
            ventende = await db.Forespørsler.AsNoTracking().CountAsync(f => f.Svar == Statuser.Svar.Venter);
        else if (principal.IsInRole(AppRoles.Ressursleverandor))
            ventende = await db.Forespørsler.AsNoTracking().CountAsync(f => f.Svar == Statuser.Svar.Venter && f.Ressurs!.EierID == id);
        if (ventende > 0 && valg["foresporsler"])
            varsler.Add(new("foresporsler", ventende == 1 ? "1 forespørsel venter på svar" : $"{ventende} forespørsler venter på svar",
                "Åpne forespørsler", "Operations", "Index", DateTime.UtcNow));

        if (driftsrolle && valg["akutt"])
        {
            var akutte = await db.Behov.AsNoTracking()
                .Where(b => b.Prioritet == Statuser.Prioritet.Akutt && (b.Status == Statuser.Behov.Ny || b.Status == Statuser.Behov.Vurderes))
                .OrderByDescending(b => b.Registrert).Take(3).Select(b => new { b.Beskrivelse, b.Stedsnavn, b.Registrert }).ToListAsync();
            varsler.AddRange(akutte.Select(b => new VarselElement("akutt", "Akutt behov: " + Forkort(b.Beskrivelse), b.Stedsnavn, "Home", "Map", b.Registrert)));
        }

        if (valg["status"])
        {
            var logg = db.Hendelseslogg.AsNoTracking().Where(l => l.Tidspunkt >= siden && l.UtførtAvID != id);
            if (principal.IsInRole(AppRoles.OffentligAktor))
                logg = logg.Where(l => l.BehovID != null && db.Behov.Any(b => b.BehovID == l.BehovID && b.RegistrertAvID == id));
            else if (principal.IsInRole(AppRoles.Ressursleverandor))
                logg = logg.Where(l => l.RessursID != null && db.Ressurser.Any(r => r.RessursID == l.RessursID && r.EierID == id));
            var endringer = await logg.OrderByDescending(l => l.Tidspunkt).Take(3).Select(l => new { l.Handling, l.Begrunnelse, l.Tidspunkt }).ToListAsync();
            varsler.AddRange(endringer.Select(l => new VarselElement("status", Forkort(Menneskelig(l.Handling)), Forkort(l.Begrunnelse), "Operations", "Index", l.Tidspunkt)));
        }

        var meldinger = await db.Meldinger.AsNoTracking().CountAsync(m => m.MottakerID == id && m.Lest == null);
        if (valg["meldinger"])
        {
            var nyeMeldinger = await db.Meldinger.AsNoTracking()
                .Where(m => m.MottakerID == id && m.Sendt >= siden)
                .OrderByDescending(m => m.Sendt).Take(5)
                .Select(m => new { Navn = m.SendtAv!.FulltNavn, m.Emne, m.Sendt }).ToListAsync();
            varsler.AddRange(nyeMeldinger.Select(m => new VarselElement("meldinger", "Ny melding fra " + m.Navn,
                Forkort(m.Emne) + " · " + m.Sendt.ToString("dd.MM HH:mm"), "Messages", "Inbox", m.Sendt)));
        }
        var sett = (await users.GetAuthenticationTokenAsync(user, Provider, SettToken) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var bilde = await users.GetAuthenticationTokenAsync(user, Provider, "Profilbilde");
        var hendelser = driftsrolle ? await db.Hendelser.AsNoTracking().CountAsync(h => h.Status == Statuser.Hendelse.Aktiv) : 0;
        return _cache = new ShellModel
        {
            Navn = user.FulltNavn,
            Initialer = string.Concat(user.FulltNavn.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetter(p[0])).Select(p => char.ToUpperInvariant(p[0])).Take(2)) is { Length: > 0 } i ? i : "?",
            Varsler = varsler.OrderByDescending(v => v.Tid).ToList(),
            AntallUlest = varsler.Count(v => !sett.Contains(Nøkkel(v))),
            Nøkler = varsler.Select(Nøkkel).ToList(),
            HarBilde = bilde is not null,
            BildeVersjon = bilde is null ? "" : bilde.Length.ToString("x") + bilde[^Math.Min(8, bilde.Length)..].GetHashCode().ToString("x"),
            Forespørsler = ventende,
            Meldinger = meldinger,
            AktiveHendelser = hendelser
        };
    }

    private static string Forkort(string? tekst) =>
        string.IsNullOrWhiteSpace(tekst) ? "" : tekst.Length > 70 ? tekst[..67] + "…" : tekst;
}
