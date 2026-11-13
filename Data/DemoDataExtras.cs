using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>Flere ressurser og behov fra ulike aktører, spredt over Norge og i ulike faser. Kjøres én gang.</summary>
public static class DemoDataExtras
{
    private const string ExtraMarker = "DemodataUtvidelse1";

    public static async Task EnsureAsync(AppDbContext db, UserManager<Bruker> users, int dispatchId)
    {
        if (await db.Hendelseslogg.AnyAsync(a => a.Handling == ExtraMarker)) return;

        async Task<Bruker> User(string email) =>
            await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("Demokontoene mangler.");
        int Type(string navn) => SeedData.Ressurstyper.Single(t => t.Navn == navn).Id;
        int Behovstype(string navn) => SeedData.Behovstyper.Single(t => t.Navn == navn).Id;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var skogbrann = await db.Hendelser.FirstOrDefaultAsync(h => h.Navn == "Skogbrann i Lærdal");
        var flom = await db.Hendelser.FirstOrDefaultAsync(h => h.Navn == "Flom i Glomma");

        var resourceRows = new (string Navn, string Type, string Sted, decimal Lat, decimal Lng, string Eier, int Antall, string Enhet, bool Forbruk)[]
        {
            ("Brannbil tankbil 10 000 L", "Kjøretøy", "Bergen", 60.3913m, 5.3221m, "vest@gmail.com", 2, "stk", false),
            ("Ambulansebuss evakuering", "Kjøretøy", "Trondheim", 63.4305m, 10.3951m, "transport@gmail.com", 1, "stk", false),
            ("Snøscooter med slede", "Kjøretøy", "Tromsø", 69.6492m, 18.9553m, "nord@gmail.com", 4, "stk", false),
            ("Lastebil 26 tonn", "Kjøretøy", "Stavanger", 58.9700m, 5.7331m, "transport@gmail.com", 3, "stk", false),
            ("Hjullaster", "Maskin", "Ålesund", 62.4722m, 6.1549m, "vest@gmail.com", 2, "stk", false),
            ("Gravemaskin 20 tonn", "Maskin", "Bodø", 67.2804m, 14.4049m, "nord@gmail.com", 1, "stk", false),
            ("Bulldoser D6", "Maskin", "Fauske", 67.2580m, 15.3920m, "nord@gmail.com", 1, "stk", false),
            ("Dronepakke kartlegging", "Drone", "Bergen", 60.3920m, 5.3300m, "vest@gmail.com", 3, "stk", false),
            ("Drone med lyskaster", "Drone", "Kristiansand", 58.1467m, 7.9956m, "ressurs@gmail.com", 2, "stk", false),
            ("Drone med varmekamera nord", "Drone", "Alta", 69.9689m, 23.2716m, "nord@gmail.com", 1, "stk", false),
            ("Aggregat 50 kVA", "Generator", "Trondheim", 63.4200m, 10.4100m, "energi@gmail.com", 2, "stk", false),
            ("Nødstrømsaggregat 10 kVA", "Generator", "Stavanger", 58.9600m, 5.7200m, "energi@gmail.com", 4, "stk", false),
            ("Aggregat 100 kVA", "Generator", "Narvik", 68.4385m, 17.4272m, "nord@gmail.com", 1, "stk", false),
            ("Mobilt strømlager", "Generator", "Oslo", 59.9139m, 10.7522m, "energi@gmail.com", 2, "stk", false),
            ("Brannmannskap", "Personell", "Sogndal", 61.2296m, 7.1004m, "vest@gmail.com", 12, "pers", false),
            ("Søk og redning hundepatrulje", "Personell", "Lillehammer", 61.1153m, 10.4662m, "ressurs@gmail.com", 6, "pers", false),
            ("Helsepersonell", "Personell", "Tromsø", 69.6500m, 18.9600m, "nord@gmail.com", 8, "pers", false),
            ("Frivillige Røde Kors", "Personell", "Drammen", 59.7440m, 10.2045m, "transport@gmail.com", 20, "pers", false),
            ("Feltsenger", "Utstyr", "Skien", 59.2096m, 9.6090m, "ressurs@gmail.com", 60, "stk", true),
            ("Drikkevann pall", "Utstyr", "Bergen", 60.3800m, 5.3400m, "vest@gmail.com", 500, "L", true),
            ("Sandsekker", "Utstyr", "Fredrikstad", 59.2181m, 10.9298m, "transport@gmail.com", 800, "stk", true),
            ("Varmeovner og tepper", "Utstyr", "Kirkenes", 69.7271m, 30.0458m, "nord@gmail.com", 100, "stk", true),
            ("Pumpeutstyr lensing", "Utstyr", "Hamar", 60.7950m, 11.0700m, "energi@gmail.com", 5, "stk", false),
            ("Telt oppvarmet 40 pers", "Utstyr", "Lærdal", 61.0990m, 7.4800m, "vest@gmail.com", 2, "stk", false),
            ("Kranbil 30 tonn", "Maskin", "Molde", 62.7375m, 7.1591m, "transport@gmail.com", 1, "stk", false)
        };

        var resources = new List<Ressurs>();
        var rIndex = 0;
        foreach (var r in resourceRows)
        {
            var owner = await User(r.Eier);
            var status = rIndex % 6 == 5 ? Statuser.Ressurs.VenterPåVerifisering : Statuser.Ressurs.Tilgjengelig;
            resources.Add(new Ressurs
            {
                Navn = r.Navn, RessurstypeID = Type(r.Type), Stedsnavn = r.Sted, Breddegrad = r.Lat, Lengdegrad = r.Lng,
                Beskrivelse = $"{r.Navn} utlånes til beredskap. Base: {r.Sted}.", EierID = owner.Id, Status = status,
                TilgjengeligFra = today.AddDays(-3), Kontaktperson = $"{owner.Fornavn} {owner.Etternavn}",
                Kontakttelefon = owner.PhoneNumber ?? "40000000", Registrert = now.AddHours(-6 - rIndex * 3),
                Antall = r.Antall, Enhet = r.Enhet, ErForbruksvare = r.Forbruk
            });
            rIndex++;
        }
        db.Ressurser.AddRange(resources);
        await db.SaveChangesAsync();
        foreach (var res in resources.Where(r => r.Status == Statuser.Ressurs.Tilgjengelig))
            db.Verifiseringer.Add(new Verifisering
            {
                RessursID = res.RessursID, VerifisertAvID = dispatchId, Tidspunkt = res.Registrert.AddHours(1),
                Resultat = Statuser.Resultat.Godkjent
            });

        var needRows = new (string Aktor, string Type, string Tekst, string Sted, decimal Lat, decimal Lng, string Prio, int Antall, int Timer, string Hendelse)[]
        {
            ("bergen@gmail.com", "Strøm", "Strømbrudd på sykehjem etter jordras. Trenger nødstrøm.", "Bergen", 60.3913m, 5.3221m, Statuser.Prioritet.Akutt, 1, 3, ""),
            ("tromso@gmail.com", "Mannskap", "Helsepersonell til midlertidig mottak for evakuerte.", "Tromsø", 69.6492m, 18.9553m, Statuser.Prioritet.Akutt, 4, 4, ""),
            ("trondheim@gmail.com", "Transport", "Evakuering av beboere fra rasutsatt område.", "Trondheim", 63.4305m, 10.3951m, Statuser.Prioritet.Akutt, 1, 5, ""),
            ("stavanger@gmail.com", "Utstyr", "Sandsekker til beskyttelse mot stormflo.", "Stavanger", 58.9700m, 5.7331m, Statuser.Prioritet.Planlagt, 300, 9, ""),
            ("offentlig@gmail.com", "Graving og rydding", "Rydding av vei etter snøskred.", "Fauske", 67.2580m, 15.3920m, Statuser.Prioritet.Akutt, 1, 7, ""),
            ("bergen@gmail.com", "Søk og overvåking", "Kartlegging av rasområde med drone.", "Bergen", 60.4100m, 5.3500m, Statuser.Prioritet.Planlagt, 1, 12, ""),
            ("tromso@gmail.com", "Utstyr", "Varme og tepper til isolert bygd.", "Kirkenes", 69.7271m, 30.0458m, Statuser.Prioritet.Planlagt, 50, 14, ""),
            ("trondheim@gmail.com", "Strøm", "Reservestrøm til vannverk.", "Trondheim", 63.4200m, 10.4100m, Statuser.Prioritet.Planlagt, 1, 20, ""),
            ("stavanger@gmail.com", "Mannskap", "Brannmannskap til skogbrann.", "Lærdal", 61.0987m, 7.4777m, Statuser.Prioritet.Akutt, 6, 2, "brann"),
            ("offentlig@gmail.com", "Utstyr", "Pumper til lensing av kjellere.", "Hamar", 60.7945m, 11.0680m, Statuser.Prioritet.Planlagt, 3, 10, "flom"),
            ("bergen@gmail.com", "Utstyr", "Drikkevann til evakuerte.", "Sogndal", 61.2296m, 7.1004m, Statuser.Prioritet.Akutt, 200, 1, "brann"),
            ("tromso@gmail.com", "Transport", "Transport av utstyr til Alta.", "Alta", 69.9689m, 23.2716m, Statuser.Prioritet.Planlagt, 1, 18, "")
        };

        var needs = new List<Behov>();
        foreach (var n in needRows)
        {
            var actor = await User(n.Aktor);
            needs.Add(new Behov
            {
                BehovstypeID = Behovstype(n.Type), RegistrertAvID = actor.Id, Beskrivelse = n.Tekst, Stedsnavn = n.Sted,
                Breddegrad = n.Lat, Lengdegrad = n.Lng, Prioritet = n.Prio, Status = Statuser.Behov.Ny, Antall = n.Antall,
                Frist = now.AddHours(24 - n.Timer), Kontaktperson = $"{actor.Fornavn} {actor.Etternavn}",
                Kontakttelefon = actor.PhoneNumber ?? "40000000", Registrert = now.AddHours(-n.Timer),
                HendelseID = n.Hendelse == "brann" ? skogbrann?.HendelseID : n.Hendelse == "flom" ? flom?.HendelseID : null
            });
        }
        db.Behov.AddRange(needs);
        await db.SaveChangesAsync();

        // (behov, ressurs, fremdrift; null = forespørsel venter på svar)
        var links = new (int Need, int Res, string? Steg, decimal Antall)[]
        {
            (0, 10, Statuser.Fremdrift.PåVei, 1), (1, 16, Statuser.Fremdrift.Fremme, 4), (2, 1, Statuser.Fremdrift.Bekreftet, 1),
            (4, 6, null, 1), (8, 14, Statuser.Fremdrift.PåVei, 6), (9, 22, Statuser.Fremdrift.Bekreftet, 3),
            (10, 18, null, 200), (3, 19, Statuser.Fremdrift.PåVei, 300)
        };
        foreach (var (need, res, steg, antall) in links)
        {
            var resource = resources[res];
            var behov = needs[need];
            if (resource.Status != Statuser.Ressurs.Tilgjengelig)
            {
                resource.Status = Statuser.Ressurs.Tilgjengelig;
                db.Verifiseringer.Add(new Verifisering
                {
                    RessursID = resource.RessursID, VerifisertAvID = dispatchId, Tidspunkt = now.AddHours(-2),
                    Resultat = Statuser.Resultat.Godkjent
                });
            }
            var request = new Forespørsel
            {
                BehovID = behov.BehovID, RessursID = resource.RessursID, SendtAvID = dispatchId, Antall = antall,
                Sendt = now.AddHours(-2), Svar = steg is null ? Statuser.Svar.Venter : Statuser.Svar.Godtatt,
                Svart = steg is null ? null : now.AddHours(-1.5)
            };
            db.Forespørsler.Add(request);
            await db.SaveChangesAsync();
            resource.ReservertAntall = antall;
            resource.Status = steg is null ? Statuser.Ressurs.Forespurt : Statuser.Ressurs.Tildelt;
            behov.Status = steg is null ? Statuser.Behov.Vurderes : Statuser.Behov.Tildelt;
            if (steg is not null)
                db.Tildelinger.Add(new Tildeling
                {
                    ForespørselID = request.ForespørselID, Tildelt = now.AddHours(-1.2), Fremdrift = steg,
                    SistOppdatert = now.AddMinutes(-30), ForventetFremme = now.AddHours(2),
                    Mottakskontakt = behov.Kontaktperson
                });
        }

        foreach (var behov in needs)
            db.Hendelseslogg.Add(new Hendelseslogg
            {
                UtførtAvID = behov.RegistrertAvID, Handling = "BehovRegistrert", Begrunnelse = behov.Beskrivelse,
                Tidspunkt = behov.Registrert, BehovID = behov.BehovID, HendelseID = behov.HendelseID
            });
        foreach (var res in resources)
            db.Hendelseslogg.Add(new Hendelseslogg
            {
                UtførtAvID = res.EierID, Handling = "RessursRegistrert", Begrunnelse = res.Navn,
                Tidspunkt = res.Registrert, RessursID = res.RessursID
            });
        db.Hendelseslogg.Add(new Hendelseslogg
        {
            UtførtAvID = dispatchId, Handling = ExtraMarker, Begrunnelse = "Ekstra demodata opprettet.", Tidspunkt = now
        });
        await db.SaveChangesAsync();
    }
}
