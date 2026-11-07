using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>Et lite sett demodata slik at hver rolle har noe å jobbe med. Kjøres én gang (se <see cref="Marker"/>).</summary>
public static class DemoDataSeeder
{
    public const string Marker = "DemodataOpprettet";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.Hendelseslogg.AnyAsync(a => a.Handling == Marker)) return;

        var users = services.GetRequiredService<UserManager<Bruker>>();
        async Task<int> Id(string role) =>
            (await users.FindByEmailAsync(IdentitySeeder.DemoAccounts.First(a => a.Role == role).Email))?.Id
            ?? throw new InvalidOperationException("Demokontoene mangler.");
        var publicActor = await Id(AppRoles.OffentligAktor);
        var provider = await Id(AppRoles.Ressursleverandor);
        var dispatch = await Id(AppRoles.DispatchTeam);
        var leader = await Id(AppRoles.Kriseledelse);

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        int Type(string navn) => SeedData.Ressurstyper.Single(t => t.Navn == navn).Id;
        int Behovstype(string navn) => SeedData.Behovstyper.Single(t => t.Navn == navn).Id;

        Ressurs Resource(string name, string type, string place, decimal lat, decimal lng, string description,
            string status, int createdHoursAgo) => new()
        {
            Navn = name, RessurstypeID = Type(type), Stedsnavn = place, Breddegrad = lat, Lengdegrad = lng,
            Beskrivelse = description, EierID = provider, Status = status, TilgjengeligFra = today.AddDays(-2),
            Kontaktperson = "Rita Ressurs", Kontakttelefon = "40000000", Registrert = now.AddHours(-createdHoursAgo)
        };

        var truck = Resource("Terrengbil 4x4", "Kjøretøy", "Hamar", 60.7945m, 11.0680m,
            "Plass til 8 personer, vinterutstyr", Statuser.Ressurs.Tilgjengelig, 30);
        var drone = Resource("Drone med varmekamera", "Drone", "Lillehammer", 61.1153m, 10.4662m,
            "Rekkevidde 6 km, 40 min flytid", Statuser.Ressurs.Forespurt, 28);
        var generator = Resource("Aggregat 20 kVA", "Generator", "Elverum", 60.8819m, 11.5623m,
            "230/400 V, på tilhenger", Statuser.Ressurs.Tildelt, 26);
        var excavator = Resource("Beltegraver 14 tonn", "Maskin", "Kongsvinger", 60.1904m, 12.0014m,
            "Transport med trailer", Statuser.Ressurs.VenterPåVerifisering, 2);
        db.Ressurser.AddRange(truck, drone, generator, excavator);

        var flom = new Hendelse
        {
            OpprettetAvID = leader, Navn = "Flom i Glomma", Område = "Innlandet",
            Beskrivelse = "Høy vannstand langs Glomma etter kraftig regn.", Starttidspunkt = now.AddHours(-30),
            Status = Statuser.Hendelse.Aktiv
        };
        db.Hendelser.Add(flom);
        await db.SaveChangesAsync();

        foreach (var (ressurs, hoursAgo) in new[] { (truck, 29), (drone, 27), (generator, 25) })
            db.Verifiseringer.Add(new Verifisering
            {
                RessursID = ressurs.RessursID, VerifisertAvID = dispatch, Tidspunkt = now.AddHours(-hoursAgo),
                Resultat = Statuser.Resultat.Godkjent
            });

        Behov Need(string type, string description, string place, decimal lat, decimal lng, string priority,
            string status, int antall, int hoursAgo, bool knyttTilFlom) => new()
        {
            BehovstypeID = Behovstype(type), RegistrertAvID = publicActor, Beskrivelse = description,
            Stedsnavn = place, Breddegrad = lat, Lengdegrad = lng, Prioritet = priority, Status = status,
            Antall = antall, Frist = now.AddHours(24 - hoursAgo), Kontaktperson = "Olav Offentlig",
            Kontakttelefon = "40000000", Registrert = now.AddHours(-hoursAgo),
            HendelseID = knyttTilFlom ? flom.HendelseID : null
        };
        var evakuering = Need("Transport", "Evakuering av 12 beboere fra flomutsatt område. Trenger kjøretøy med god bakkeklaring.",
            "Elverum", 60.8819m, 11.5623m, Statuser.Prioritet.Akutt, Statuser.Behov.Ny, 2, 5, true);
        var strøm = Need("Strøm", "Strøm til midlertidig mottak i idrettshallen.",
            "Hamar", 60.7945m, 11.0680m, Statuser.Prioritet.Akutt, Statuser.Behov.Tildelt, 1, 24, true);
        var søk = Need("Søk og overvåking", "Søk etter savnet person i skogsområde nord for tettstedet.",
            "Kongsvinger", 60.1904m, 12.0014m, Statuser.Prioritet.Planlagt, Statuser.Behov.Vurderes, 1, 8, false);
        db.Behov.AddRange(evakuering, strøm, søk);
        await db.SaveChangesAsync();

        var tildelt = new Forespørsel
        {
            BehovID = strøm.BehovID, RessursID = generator.RessursID, SendtAvID = dispatch,
            Sendt = now.AddHours(-22), Svar = Statuser.Svar.Godtatt, Svart = now.AddHours(-21)
        };
        var venter = new Forespørsel
        {
            BehovID = søk.BehovID, RessursID = drone.RessursID, SendtAvID = dispatch,
            Sendt = now.AddHours(-1), Svar = Statuser.Svar.Venter
        };
        db.Forespørsler.AddRange(tildelt, venter);
        await db.SaveChangesAsync();

        db.Tildelinger.Add(new Tildeling
        {
            ForespørselID = tildelt.ForespørselID, Fremdrift = Statuser.Fremdrift.PåVei,
            Tildelt = now.AddHours(-20), SistOppdatert = now.AddHours(-19)
        });

        void Logg(int actor, string action, string reason, double hoursAgo, int? resourceId = null,
            int? needId = null, int? requestId = null) =>
            db.Hendelseslogg.Add(new Hendelseslogg
            {
                UtførtAvID = actor, Handling = action, Begrunnelse = reason, Tidspunkt = now.AddHours(-hoursAgo),
                RessursID = resourceId, BehovID = needId, ForespørselID = requestId
            });
        Logg(leader, "HendelseOpprettet", flom.Navn, 30);
        Logg(provider, "RessursRegistrert", "Kjøretøy: Terrengbil 4x4", 30, truck.RessursID);
        Logg(dispatch, "RessursGodkjent", "Godkjent av dispatch.", 29, truck.RessursID);
        Logg(provider, "RessursRegistrert", "Drone: Drone med varmekamera", 28, drone.RessursID);
        Logg(dispatch, "RessursGodkjent", "Godkjent av dispatch.", 27, drone.RessursID);
        Logg(provider, "RessursRegistrert", "Generator: Aggregat 20 kVA", 26, generator.RessursID);
        Logg(dispatch, "RessursGodkjent", "Godkjent av dispatch.", 25, generator.RessursID);
        Logg(provider, "RessursRegistrert", "Maskin: Beltegraver 14 tonn", 2, excavator.RessursID);
        Logg(publicActor, "BehovRegistrert", strøm.Beskrivelse, 24, needId: strøm.BehovID);
        Logg(publicActor, "BehovRegistrert", søk.Beskrivelse, 8, needId: søk.BehovID);
        Logg(publicActor, "BehovRegistrert", evakuering.Beskrivelse, 5, needId: evakuering.BehovID);
        Logg(dispatch, "ForespørselSendt", "Aggregatet dekker behovet i hallen.", 22, generator.RessursID, strøm.BehovID, tildelt.ForespørselID);
        Logg(provider, "ForespørselGodtatt", "Aggregatet er klart.", 21, generator.RessursID, strøm.BehovID, tildelt.ForespørselID);
        Logg(dispatch, "Tildelt", "Tildelt til mottaket.", 20, generator.RessursID, strøm.BehovID, tildelt.ForespørselID);
        Logg(dispatch, "FremdriftPåVei", "Aggregatet er på vei.", 19, generator.RessursID, strøm.BehovID, tildelt.ForespørselID);
        Logg(dispatch, "ForespørselSendt", "Varmekamera trengs til søk i skogen.", 1, drone.RessursID, søk.BehovID, venter.ForespørselID);
        Logg(leader, Marker, "Demodata opprettet.", 0);
        await db.SaveChangesAsync();
    }
}
