using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>Fullførte og pågående tildelinger i hver aktive hendelse, slik at fremdriftslinjene viser ulike nivåer. Kjøres én gang.</summary>
public static class DemoDataRound28
{
    private const string Marker = "DemodataUtvidelse28";

    public static async Task EnsureAsync(AppDbContext db, UserManager<Bruker> users, int dispatchId)
    {
        if (await db.Hendelseslogg.AnyAsync(a => a.Handling == Marker)) return;

        var hendelser = await db.Hendelser.Where(h => h.Status == Statuser.Hendelse.Aktiv).OrderBy(h => h.HendelseID).ToListAsync();
        var ressurser = await db.Ressurser
            .Where(r => r.Status != Statuser.Ressurs.Avvist && r.Status != Statuser.Ressurs.VenterPåVerifisering)
            .OrderBy(r => r.RessursID).ToListAsync();
        if (hendelser.Count == 0 || ressurser.Count == 0) return;

        var aktorer = new[] { "offentlig@gmail.com", "kommune@gmail.com", "bergen@gmail.com", "stavanger@gmail.com", "tromso@gmail.com", "trondheim@gmail.com" };
        var aktorBrukere = new List<Bruker>();
        foreach (var e in aktorer) { var b = await users.FindByEmailAsync(e); if (b is not null) aktorBrukere.Add(b); }
        if (aktorBrukere.Count == 0) return;

        // (ferdige, pågående) per hendelse: gir ca. 10 % til 100 %
        var mønster = new (int Ferdig, int Pågår)[] { (1, 9), (2, 6), (3, 5), (1, 1), (4, 4), (5, 3), (7, 3), (9, 1), (3, 0), (2, 8) };
        var typer = SeedData.Behovstyper.Select(t => t.Id).ToList();
        var now = DateTime.UtcNow;
        var teller = 0;

        for (var i = 0; i < hendelser.Count; i++)
        {
            var h = hendelser[i];
            var (ferdig, pågår) = mønster[i % mønster.Length];
            for (var k = 0; k < ferdig + pågår; k++, teller++)
            {
                var ferdigK = k < ferdig;
                var aktor = aktorBrukere[teller % aktorBrukere.Count];
                var ressurs = ressurser[teller % ressurser.Count];
                var behov = new Behov
                {
                    BehovstypeID = typer[teller % typer.Count], RegistrertAvID = aktor.Id,
                    Beskrivelse = ferdigK ? $"Levert til {h.Område}: {ressurs.Navn}." : $"Pågående leveranse til {h.Område}.",
                    Stedsnavn = h.Område, Breddegrad = h.Breddegrad ?? 0m, Lengdegrad = h.Lengdegrad ?? 0m,
                    Prioritet = Statuser.Prioritet.Planlagt, Antall = 1,
                    Status = ferdigK ? Statuser.Behov.Løst : Statuser.Behov.Tildelt,
                    Frist = now.AddHours(12), Kontaktperson = $"{aktor.Fornavn} {aktor.Etternavn}",
                    Kontakttelefon = aktor.PhoneNumber ?? "40000000", Registrert = now.AddHours(-30 - k), HendelseID = h.HendelseID
                };
                db.Behov.Add(behov);
                await db.SaveChangesAsync();
                var forespørsel = new Forespørsel
                {
                    BehovID = behov.BehovID, RessursID = ressurs.RessursID, SendtAvID = dispatchId, Antall = 1,
                    Sendt = now.AddHours(-28 - k), Svar = Statuser.Svar.Godtatt, Svart = now.AddHours(-27 - k)
                };
                db.Forespørsler.Add(forespørsel);
                await db.SaveChangesAsync();
                db.Tildelinger.Add(new Tildeling
                {
                    ForespørselID = forespørsel.ForespørselID, Tildelt = now.AddHours(-26 - k),
                    Fremdrift = ferdigK ? Statuser.Fremdrift.Fullført : (k % 2 == 0 ? Statuser.Fremdrift.PåVei : Statuser.Fremdrift.Bekreftet),
                    SistOppdatert = now.AddHours(-1), Fullført = ferdigK ? now.AddHours(-2) : null,
                    OverleveringBekreftet = ferdigK ? now.AddHours(-2) : null, LevertAntall = ferdigK ? 1 : null,
                    ForventetFremme = now.AddHours(3), Mottakskontakt = behov.Kontaktperson
                });
                db.Hendelseslogg.Add(new Hendelseslogg
                {
                    UtførtAvID = aktor.Id, Handling = "BehovRegistrert", Begrunnelse = behov.Beskrivelse,
                    Tidspunkt = behov.Registrert, BehovID = behov.BehovID, HendelseID = h.HendelseID
                });
            }
        }
        db.Hendelseslogg.Add(new Hendelseslogg { UtførtAvID = dispatchId, Handling = Marker, Begrunnelse = "Ekstra demodata opprettet.", Tidspunkt = now });
        await db.SaveChangesAsync();
    }
}
