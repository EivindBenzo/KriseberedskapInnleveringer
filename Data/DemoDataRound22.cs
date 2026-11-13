using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>Flere pågående hendelser, udekkede akutte behov og ubehandlede forespørsler, slik at alle roller har innhold. Kjøres én gang.</summary>
public static class DemoDataRound22
{
    private const string Marker = "DemodataUtvidelse22";

    public static async Task EnsureAsync(AppDbContext db, UserManager<Bruker> users, int dispatchId, int leaderId)
    {
        if (await db.Hendelseslogg.AnyAsync(a => a.Handling == Marker)) return;

        async Task<Bruker> User(string email) =>
            await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("Demokontoene mangler.");
        int Behovstype(string navn) => SeedData.Behovstyper.Single(t => t.Navn == navn).Id;
        var now = DateTime.UtcNow;

        var hendelseRader = new (string Navn, string Beskrivelse, string Omrade, decimal Lat, decimal Lng, string Prio, int Timer)[]
        {
            ("Ekstremvær i Stavanger", "Kraftig storm og stormflo langs kysten. Flere veier stengt.", "Stavanger", 58.9700m, 5.7331m, Statuser.HendelsePrioritet.Hoy, 20),
            ("Jordskred i Hordaland", "Jordskred har tatt med seg vei og strømlinjer.", "Voss", 60.6280m, 6.4180m, Statuser.HendelsePrioritet.Hoy, 30),
            ("Snøskred i Tromsø", "Snøskred over riksvei, flere biler innesperret.", "Tromsø", 69.6492m, 18.9553m, Statuser.HendelsePrioritet.Medium, 14),
            ("Strømbrudd i Trøndelag", "Langvarig strømbrudd etter ising på kraftlinjer.", "Steinkjer", 64.0148m, 11.4954m, Statuser.HendelsePrioritet.Medium, 40),
            ("Flom i Telemark", "Elver går over sine bredder. Evakuering av boliger.", "Skien", 59.2096m, 9.6090m, Statuser.HendelsePrioritet.Hoy, 12),
            ("Gressbrann i Kristiansand", "Tørre forhold og vind sprer brann i gress og skog.", "Kristiansand", 58.1467m, 7.9956m, Statuser.HendelsePrioritet.Medium, 6),
            ("Utrasing av vei i Nordland", "Rasutsatt strekning stengt. Lokalsamfunn er isolert.", "Bodø", 67.2804m, 14.4049m, Statuser.HendelsePrioritet.Medium, 52),
            ("Vannforsyning brutt i Drammen", "Hovedvannledning sprengt, 5 000 abonnenter uten vann.", "Drammen", 59.7440m, 10.2045m, Statuser.HendelsePrioritet.Lav, 9),
            ("Isgang i Finnmark", "Isgang i Tana truer bebyggelse langs elva.", "Tana", 70.1980m, 28.1900m, Statuser.HendelsePrioritet.Lav, 70),
            ("Industribrann i Ålesund", "Brann i industribygg med fare for utslipp. Området er sperret av.", "Ålesund", 62.4722m, 6.1549m, Statuser.HendelsePrioritet.Hoy, 3)
        };
        var hendelser = new List<Hendelse>();
        foreach (var h in hendelseRader)
            hendelser.Add(new Hendelse
            {
                OpprettetAvID = leaderId, Navn = h.Navn, Beskrivelse = h.Beskrivelse, Område = h.Omrade, Breddegrad = h.Lat, Lengdegrad = h.Lng,
                Prioritet = h.Prio, Status = Statuser.Hendelse.Aktiv, Starttidspunkt = now.AddHours(-h.Timer)
            });
        db.Hendelser.AddRange(hendelser);
        await db.SaveChangesAsync();

        var needRows = new (string Aktor, string Type, string Tekst, string Sted, decimal Lat, decimal Lng, string Prio, int Antall, int Timer, int Hendelse)[]
        {
            ("stavanger@gmail.com", "Strøm", "Strømbrudd på sykehjem etter storm. Trenger nødaggregat.", "Stavanger", 58.9650m, 5.7400m, Statuser.Prioritet.Akutt, 2, 2, 0),
            ("bergen@gmail.com", "Graving og rydding", "Jordskred over vei. Trenger gravemaskin for å nå isolerte hus.", "Voss", 60.6300m, 6.4200m, Statuser.Prioritet.Akutt, 2, 3, 1),
            ("tromso@gmail.com", "Mannskap", "Redningsmannskap til skredområde. Personer er savnet.", "Tromsø", 69.6600m, 18.9700m, Statuser.Prioritet.Akutt, 10, 1, 2),
            ("trondheim@gmail.com", "Strøm", "Nødstrøm til omsorgsboliger uten strøm i over 24 timer.", "Steinkjer", 64.0148m, 11.4954m, Statuser.Prioritet.Akutt, 3, 5, 3),
            ("offentlig@gmail.com", "Utstyr", "Sandsekker og pumper til flomutsatte boliger.", "Skien", 59.2150m, 9.6100m, Statuser.Prioritet.Akutt, 400, 2, 4),
            ("kommune@gmail.com", "Mannskap", "Brannmannskap til gressbrann nær boligfelt.", "Kristiansand", 58.1500m, 8.0000m, Statuser.Prioritet.Akutt, 8, 1, 5),
            ("bergen@gmail.com", "Transport", "Evakueringstransport for isolert bygd.", "Bodø", 67.2900m, 14.4100m, Statuser.Prioritet.Akutt, 2, 4, 6),
            ("stavanger@gmail.com", "Utstyr", "Drikkevann til husstander uten vannforsyning.", "Drammen", 59.7500m, 10.2100m, Statuser.Prioritet.Akutt, 3000, 3, 7),
            ("tromso@gmail.com", "Søk og overvåking", "Dronekartlegging av isgang.", "Tana", 70.2000m, 28.2000m, Statuser.Prioritet.Planlagt, 2, 10, 8),
            ("trondheim@gmail.com", "Mannskap", "Helsepersonell til skadde etter industribrann.", "Ålesund", 62.4750m, 6.1600m, Statuser.Prioritet.Akutt, 6, 1, 9),
            ("offentlig@gmail.com", "Utstyr", "Telt og feltsenger til evakuerte.", "Skien", 59.2100m, 9.6200m, Statuser.Prioritet.Planlagt, 40, 8, 4),
            ("kommune@gmail.com", "Transport", "Transport av materiell til Voss.", "Voss", 60.6280m, 6.4180m, Statuser.Prioritet.Planlagt, 1, 11, 1),
            ("bergen@gmail.com", "Strøm", "Reservestrøm til mobilmast.", "Bergen", 60.3913m, 5.3221m, Statuser.Prioritet.Planlagt, 1, 16, -1),
            ("stavanger@gmail.com", "Graving og rydding", "Rydding av trær over vei etter storm.", "Sandnes", 58.8517m, 5.7352m, Statuser.Prioritet.Planlagt, 1, 18, 0),
            ("tromso@gmail.com", "Utstyr", "Varme og tepper til evakuerte.", "Tromsø", 69.6492m, 18.9553m, Statuser.Prioritet.Planlagt, 60, 15, 2),
            ("trondheim@gmail.com", "Søk og overvåking", "Kartlegging av kraftlinjer med drone.", "Levanger", 63.7461m, 11.2996m, Statuser.Prioritet.Planlagt, 1, 13, 3),
            ("offentlig@gmail.com", "Mannskap", "Frivillige til pakking av sandsekker.", "Porsgrunn", 59.1405m, 9.6552m, Statuser.Prioritet.Planlagt, 15, 7, 4),
            ("kommune@gmail.com", "Strøm", "Nødstrøm til varmesentral.", "Lillehammer", 61.1153m, 10.4662m, Statuser.Prioritet.Akutt, 1, 2, -1)
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
                HendelseID = n.Hendelse >= 0 ? hendelser[n.Hendelse].HendelseID : null
            });
        }
        db.Behov.AddRange(needs);
        await db.SaveChangesAsync();

        // Godtatte forespørsler uten tildeling (dispatch kan opprette tildeling) og ventende forespørsler (leverandør kan svare).
        var tilgjengelige = await db.Ressurser.Where(r => r.Status == Statuser.Ressurs.Tilgjengelig && r.ReservertAntall == 0).OrderBy(r => r.RessursID).ToListAsync();
        var koblinger = new (int Need, int Res, bool Godtatt)[] { (3, 0, true), (4, 3, true), (7, 5, true), (1, 7, false), (2, 9, false), (9, 11, false), (5, 13, false) };
        foreach (var (need, res, godtatt) in koblinger)
        {
            if (res >= tilgjengelige.Count) continue;
            var ressurs = tilgjengelige[res];
            var behov = needs[need];
            var antall = Math.Min(ressurs.TilgjengeligAntall, Math.Max(1, behov.Antall));
            db.Forespørsler.Add(new Forespørsel
            {
                BehovID = behov.BehovID, RessursID = ressurs.RessursID, SendtAvID = dispatchId, Antall = antall,
                Sendt = now.AddHours(-1.5), Svar = godtatt ? Statuser.Svar.Godtatt : Statuser.Svar.Venter, Svart = godtatt ? now.AddHours(-1) : null
            });
            ressurs.ReservertAntall = antall;
            ressurs.Status = godtatt ? Statuser.Ressurs.Tildelt : Statuser.Ressurs.Forespurt;
            behov.Status = Statuser.Behov.Vurderes;
        }

        foreach (var behov in needs)
            db.Hendelseslogg.Add(new Hendelseslogg
            {
                UtførtAvID = behov.RegistrertAvID, Handling = "BehovRegistrert", Begrunnelse = behov.Beskrivelse,
                Tidspunkt = behov.Registrert, BehovID = behov.BehovID, HendelseID = behov.HendelseID
            });
        db.Hendelseslogg.Add(new Hendelseslogg { UtførtAvID = dispatchId, Handling = Marker, Begrunnelse = "Ekstra demodata opprettet.", Tidspunkt = now });
        await db.SaveChangesAsync();
    }
}
