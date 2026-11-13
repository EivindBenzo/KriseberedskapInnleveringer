using System.Data;
using System.Security.Claims;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeimevernetInnlevering1.Services;

public class WorkflowException(string message) : Exception(message);

/// <summary>
/// All forretningslogikk for arbeidsflyten: ressurs → verifisering → forespørsel → tildeling.
/// Hver metode sjekker rolle og eierskap på serveren, kjører i en transaksjon og skriver til Hendelseslogg.
///
/// Livsløp for Ressurs.Status (verdiene fra rapporten):
/// venter på verifisering → tilgjengelig ⇄ utilgjengelig → forespurt → tildelt → i bruk → returnert → (ny verifisering)
/// Avvist ved verifisering gir «avvist».
/// </summary>
public class WorkflowService(AppDbContext db, IOptions<WorkflowOptions> options)
{
    private int GyldighetDager => options.Value.VerificationValidityDays;

    // ---------- Hjelpemetoder ----------

    private static int Actor(ClaimsPrincipal actor) =>
        actor.Identity?.IsAuthenticated == true && ResourcePermissions.BrukerId(actor) is int id
            ? id
            : throw new WorkflowException("Du må være innlogget.");

    private static int Require(ClaimsPrincipal actor, string role)
    {
        var id = Actor(actor);
        if (!actor.IsInRole(role))
            throw new WorkflowException($"Kun {AppRoles.Label(role).ToLowerInvariant()} kan utføre dette.");
        return id;
    }

    private static string Begrunnelse(string? reason, string standard)
    {
        var tekst = string.IsNullOrWhiteSpace(reason) ? standard : reason.Trim();
        if (tekst.Length > 1000) throw new WorkflowException("Begrunnelsen kan ha høyst 1000 tegn.");
        return tekst;
    }

    /// <summary>Kjører endringen i en transaksjon. Ved samtidig endring rulles alt tilbake.</summary>
    private async Task Mutate(Func<Task> change, string? conflictMessage = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await change();
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw new WorkflowException(conflictMessage ?? "En annen bruker har endret opplysningene. Last siden på nytt.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw new WorkflowException(conflictMessage ?? "Endringen kunne ikke lagres. Last siden på nytt og prøv igjen.");
        }
        catch
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private void Logg(int utførtAv, string handling, string begrunnelse, int? ressursId = null,
        int? behovId = null, int? forespørselId = null, int? hendelseId = null) => db.Hendelseslogg.Add(new Hendelseslogg
        {
            UtførtAvID = utførtAv, Handling = handling,
            Begrunnelse = begrunnelse.Length > 1000 ? begrunnelse[..1000] : begrunnelse,
            Tidspunkt = DateTime.UtcNow, RessursID = ressursId, BehovID = behovId, ForespørselID = forespørselId,
            HendelseID = hendelseId
        });

    private async Task<Ressurs> HentRessurs(int id) =>
        await db.Ressurser.Include(r => r.Verifiseringer).Include(r => r.Ressurstype)
            .SingleOrDefaultAsync(r => r.RessursID == id) ?? throw new WorkflowException("Ressursen finnes ikke.");

    private async Task<Behov> HentBehov(int id) =>
        await db.Behov.Include(b => b.Behovstype!).ThenInclude(type => type.PassendeRessurstyper)
            .SingleOrDefaultAsync(b => b.BehovID == id) ?? throw new WorkflowException("Behovet finnes ikke.");

    private static void Aktivt(Behov behov)
    {
        if (!WorkflowTransitions.IsActive(behov.Status))
            throw new WorkflowException("Behovet er avsluttet.");
    }

    private static bool Opptatt(Ressurs ressurs) =>
        Statuser.Ressurs.Opptatt.Contains(ressurs.Status) || ressurs.ReservertAntall > 0;

    private static void OppdaterLagerstatus(Ressurs ressurs)
    {
        if (ressurs.TilgjengeligAntall > 0)
            ressurs.Status = Statuser.Ressurs.Tilgjengelig;
        else if (ressurs.ReservertAntall > 0)
            ressurs.Status = Statuser.Ressurs.Forespurt;
        else
            ressurs.Status = Statuser.Ressurs.Utilgjengelig;
    }

    /// <summary>
    /// Finner ressurstypen. Finnes den ikke, opprettes den som en ny rad i tabellen Ressurstype,
    /// slik at ressursleverandører kan registrere ressurser som ikke står i listen.
    /// </summary>
    /// <summary>
    /// Finner behovstypen med dette navnet, eller lager den hvis en offentlig aktør har skrevet inn en ny.
    /// En ny behovstype har ingen passende ressurstyper ennå, så dispatch ser alle ledige ressurser
    /// under «Andre tilgjengelige ressurser» for slike behov.
    /// </summary>
    private async Task<Behovstype> HentEllerOpprettBehovstype(string navn, int aktør)
    {
        navn = System.Text.RegularExpressions.Regex.Replace(navn.Trim(), @"\s+", " ");
        if (navn.Length is < 2 or > 100 || navn.Any(char.IsControl) || !navn.Any(char.IsLetter))
            throw new WorkflowException("Behovstypen må ha 2–100 tegn og inneholde bokstaver.");
        var eksisterende = await db.Behovstyper.FirstOrDefaultAsync(t => t.Navn == navn);
        if (eksisterende is not null) return eksisterende;

        var ny = new Behovstype { Navn = char.ToUpper(navn[0]) + navn[1..] };
        db.Behovstyper.Add(ny);
        await db.SaveChangesAsync();
        Logg(aktør, "BehovstypeOpprettet", ny.Navn);
        return ny;
    }

    private async Task<Ressurstype> HentEllerOpprettRessurstype(string navn, int aktør)
    {
        navn = System.Text.RegularExpressions.Regex.Replace(navn.Trim(), @"\s+", " ");
        if (navn.Length is < 2 or > 100 || navn.Any(char.IsControl) || !navn.Any(char.IsLetter))
            throw new WorkflowException("Ressurstypen må ha 2–100 tegn og inneholde bokstaver.");
        var eksisterende = await db.Ressurstyper.FirstOrDefaultAsync(t => t.Navn == navn);
        if (eksisterende is not null) return eksisterende;

        var ny = new Ressurstype { Navn = char.ToUpper(navn[0]) + navn[1..] };
        db.Ressurstyper.Add(ny);
        await db.SaveChangesAsync();
        Logg(aktør, "RessurstypeOpprettet", ny.Navn);
        return ny;
    }

    private static void Valider(RessursSkjemaViewModel v)
    {
        if (string.IsNullOrWhiteSpace(v.Navn) || v.Navn.Trim().Length is < 2 or > 150 ||
            string.IsNullOrWhiteSpace(v.Kontaktperson) || v.Kontaktperson.Length > 150 ||
            string.IsNullOrWhiteSpace(v.Kontakttelefon) || v.Kontakttelefon.Length > 20 ||
            v.Beskrivelse?.Length > 1000 || v.Antall is <= 0 or > 1000000 ||
            string.IsNullOrWhiteSpace(v.Enhet) || v.Enhet.Trim().Length > 20 || v.Stedsnavn.Length is 0 or > 200 ||
            v.TilgjengeligFra is null || (v.TilgjengeligTil is not null && v.TilgjengeligTil < v.TilgjengeligFra) ||
            !PlaceGeocoder.ValidPoint(v.Breddegrad, v.Lengdegrad))
            throw new WorkflowException("Ugyldige ressursopplysninger.");
    }

    private static decimal Koordinat(double verdi) => Math.Round((decimal)verdi, 6);

    private async Task BindAccountContactAsync(RessursSkjemaViewModel values, int actorId)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(current => current.Id == actorId)
            ?? throw new WorkflowException("Kontoen finnes ikke.");
        values.Kontaktperson = user.FulltNavn;
        values.Kontakttelefon = user.PhoneNumber ?? string.Empty;
    }

    private async Task BindAccountContactAsync(BehovSkjemaViewModel values, int actorId)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(current => current.Id == actorId)
            ?? throw new WorkflowException("Kontoen finnes ikke.");
        values.Kontaktperson = user.FulltNavn;
        values.Kontakttelefon = user.PhoneNumber ?? string.Empty;
    }

    // ---------- Ressurs ----------

    public async Task<int> RegisterResourceAsync(RessursSkjemaViewModel values, ClaimsPrincipal actor)
    {
        var id = 0;
        await Mutate(async () =>
        {
            var eier = Require(actor, AppRoles.Ressursleverandor);
            await BindAccountContactAsync(values, eier);
            Valider(values);
            var type = await HentEllerOpprettRessurstype(values.Ressurstype, eier);
            var ressurs = new Ressurs
            {
                RessurstypeID = type.RessurstypeID, EierID = eier, Navn = values.Navn.Trim(),
                Beskrivelse = string.IsNullOrWhiteSpace(values.Beskrivelse) ? null : values.Beskrivelse.Trim(),
                Antall = values.Antall, Enhet = values.Enhet.Trim(), ErForbruksvare = values.ErForbruksvare,
                TilgjengeligFra = values.TilgjengeligFra!.Value, TilgjengeligTil = values.TilgjengeligTil,
                Kontaktperson = values.Kontaktperson.Trim(), Kontakttelefon = values.Kontakttelefon.Trim(),
                Breddegrad = Koordinat(values.Breddegrad!.Value), Lengdegrad = Koordinat(values.Lengdegrad!.Value),
                Stedsnavn = values.Stedsnavn, Status = Statuser.Ressurs.VenterPåVerifisering, Registrert = DateTime.UtcNow
            };
            db.Ressurser.Add(ressurs);
            await db.SaveChangesAsync();
            id = ressurs.RessursID;
            Logg(eier, "RessursRegistrert", $"{type.Navn}: {ressurs.Navn}", id);
        });
        return id;
    }

    /// <summary>
    /// Oppdaterer en ressurs. Endres navn, type, beskrivelse eller plassering, må den verifiseres på nytt.
    /// Returnerer true hvis ny verifisering kreves.
    /// </summary>
    public async Task<bool> UpdateResourceAsync(int ressursId, RessursSkjemaViewModel values, ClaimsPrincipal actor)
    {
        var materiell = false;
        await Mutate(async () =>
        {
            var aktør = Actor(actor);
            var ressurs = await HentRessurs(ressursId);
            if (!ResourcePermissions.CanManage(actor, ressurs.EierID))
                throw new WorkflowException("Du kan bare endre egne ressurser.");
            if (Opptatt(ressurs))
                throw new WorkflowException("Ressursen er forespurt, tildelt eller i bruk. Avslutt forespørselen før du redigerer.");
            await BindAccountContactAsync(values, aktør);
            Valider(values);
            var type = await HentEllerOpprettRessurstype(values.Ressurstype, aktør);
            var beskrivelse = string.IsNullOrWhiteSpace(values.Beskrivelse) ? null : values.Beskrivelse.Trim();
            var lat = Koordinat(values.Breddegrad!.Value);
            var lng = Koordinat(values.Lengdegrad!.Value);
            materiell = ressurs.Navn != values.Navn.Trim() || ressurs.RessurstypeID != type.RessurstypeID ||
                        ressurs.Beskrivelse != beskrivelse || ressurs.Antall != values.Antall ||
                        ressurs.Enhet != values.Enhet.Trim() || ressurs.ErForbruksvare != values.ErForbruksvare ||
                        ressurs.Breddegrad != lat || ressurs.Lengdegrad != lng;
            ressurs.Navn = values.Navn.Trim();
            ressurs.RessurstypeID = type.RessurstypeID;
            ressurs.Beskrivelse = beskrivelse;
            ressurs.Antall = values.Antall;
            ressurs.Enhet = values.Enhet.Trim();
            ressurs.ErForbruksvare = values.ErForbruksvare;
            ressurs.TilgjengeligFra = values.TilgjengeligFra!.Value;
            ressurs.TilgjengeligTil = values.TilgjengeligTil;
            ressurs.Kontaktperson = values.Kontaktperson.Trim();
            ressurs.Kontakttelefon = values.Kontakttelefon.Trim();
            ressurs.Breddegrad = lat;
            ressurs.Lengdegrad = lng;
            ressurs.Stedsnavn = values.Stedsnavn;
            if (materiell) ressurs.Status = Statuser.Ressurs.VenterPåVerifisering;
            Logg(aktør, materiell ? "RessursEndret" : "TilgjengelighetEndret", "Eieren oppdaterte ressursopplysningene.", ressursId);
        });
        return materiell;
    }

    public Task UpdateResourceAvailabilityAsync(int ressursId, bool tilgjengelig, ClaimsPrincipal actor, string? reason) =>
        Mutate(async () =>
        {
            var aktør = Actor(actor);
            var ressurs = await HentRessurs(ressursId);
            if (!ResourcePermissions.CanManage(actor, ressurs.EierID))
                throw new WorkflowException("Du kan bare endre egne ressurser.");
            if (ressurs.Status is not (Statuser.Ressurs.Tilgjengelig or Statuser.Ressurs.Utilgjengelig))
                throw new WorkflowException("Tilgjengelighet kan bare endres for verifiserte ressurser som ikke er i et oppdrag.");
            if (ressurs.ReservertAntall > 0)
                throw new WorkflowException("Tilgjengeligheten kan ikke endres mens deler av beholdningen er reservert.");
            ressurs.Status = tilgjengelig ? Statuser.Ressurs.Tilgjengelig : Statuser.Ressurs.Utilgjengelig;
            Logg(aktør, "TilgjengelighetEndret", Begrunnelse(reason, $"Satt til {ressurs.Status}."), ressursId);
        });

    public Task DeleteResourceAsync(int ressursId, ClaimsPrincipal actor, string? reason) =>
        Mutate(async () =>
        {
            var aktør = Actor(actor);
            var ressurs = await HentRessurs(ressursId);
            if (!ResourcePermissions.CanManage(actor, ressurs.EierID))
                throw new WorkflowException("Du kan bare slette egne ressurser.");
            if (Opptatt(ressurs) || await db.Forespørsler.AnyAsync(f => f.RessursID == ressursId))
                throw new WorkflowException("Ressurser med forespørselshistorikk kan ikke slettes. Sett ressursen utilgjengelig.");
            db.Ressurser.Remove(ressurs);
            Logg(aktør, "RessursSlettet",
                $"{ressurs.Ressurstype?.Navn}: {ressurs.Navn}. {Begrunnelse(reason, "Slettet av eier.")}", ressursId);
        });

    // ---------- Verifisering ----------

    public Task VerifyAsync(int ressursId, bool approve, ClaimsPrincipal actor, string? reason) =>
        Mutate(async () =>
        {
            var aktør = Require(actor, AppRoles.DispatchTeam);
            var ressurs = await HentRessurs(ressursId);
            if (ressurs.EierID == aktør)
                throw new WorkflowException("Du kan ikke verifisere din egen ressurs. En annen dispatch-bruker må kontrollere den.");
            if (Opptatt(ressurs)) throw new WorkflowException("Forespurte eller tildelte ressurser må frigjøres først.");
            if (!approve && ressurs.Status == Statuser.Ressurs.Avvist)
                throw new WorkflowException("Ressursen er allerede avvist.");
            if (!approve && string.IsNullOrWhiteSpace(reason))
                throw new WorkflowException("Oppgi en begrunnelse når en ressurs avvises.");
            if (approve && GyldighetDager is < 1 or > 3650)
                throw new WorkflowException("Ugyldig konfigurasjon for godkjenningens varighet.");

            var begrunnelse = approve ? Begrunnelse(reason, "Godkjent av dispatch.") : Begrunnelse(reason, "");
            db.Verifiseringer.Add(new Verifisering
            {
                RessursID = ressursId, VerifisertAvID = aktør, Tidspunkt = DateTime.UtcNow,
                Resultat = approve ? Statuser.Resultat.Godkjent : Statuser.Resultat.Avvist,
                Begrunnelse = string.IsNullOrWhiteSpace(reason) ? null : begrunnelse
            });
            ressurs.Status = !approve ? Statuser.Ressurs.Avvist
                : ressurs.Status == Statuser.Ressurs.Utilgjengelig ? Statuser.Ressurs.Utilgjengelig
                : Statuser.Ressurs.Tilgjengelig;
            Logg(aktør, approve ? "RessursGodkjent" : "RessursAvvist", begrunnelse, ressursId);
        });

    // ---------- Hendelse ----------

    public async Task<int> CreateIncidentAsync(HendelseSkjemaViewModel values, ClaimsPrincipal actor)
    {
        var id = 0;
        await Mutate(async () =>
        {
            var aktør = Require(actor, AppRoles.Kriseledelse);
            if (string.IsNullOrWhiteSpace(values.Navn) || values.Navn.Trim().Length is < 3 or > 150 ||
                string.IsNullOrWhiteSpace(values.Område) || values.Område.Trim().Length > 150 ||
                values.Beskrivelse?.Length > 2000 || values.Starttidspunkt is null)
                throw new WorkflowException("Oppgi navn, område og starttidspunkt for hendelsen.");
            var hendelse = new Hendelse
            {
                OpprettetAvID = aktør, Navn = values.Navn.Trim(), Område = values.Område.Trim(),
                Beskrivelse = string.IsNullOrWhiteSpace(values.Beskrivelse) ? null : values.Beskrivelse.Trim(),
                Starttidspunkt = values.Starttidspunkt.Value.ToUniversalTime(),
                Status = values.Status == Statuser.Hendelse.Planlagt ? Statuser.Hendelse.Planlagt : Statuser.Hendelse.Aktiv,
                Prioritet = Statuser.HendelsePrioritet.Alle.Contains(values.Prioritet) ? values.Prioritet : Statuser.HendelsePrioritet.Medium,
                Breddegrad = values.Breddegrad, Lengdegrad = values.Lengdegrad
            };
            db.Hendelser.Add(hendelse);
            await db.SaveChangesAsync();
            id = hendelse.HendelseID;
            Logg(aktør, "HendelseOpprettet", hendelse.Navn);
        });
        return id;
    }

    public Task SetIncidentPriorityAsync(int hendelseId, string prioritet, ClaimsPrincipal actor) => Mutate(async () =>
    {
        var aktør = Require(actor, AppRoles.Kriseledelse);
        if (!Statuser.HendelsePrioritet.Alle.Contains(prioritet)) throw new WorkflowException("Ugyldig prioritet.");
        var hendelse = await db.Hendelser.SingleOrDefaultAsync(h => h.HendelseID == hendelseId)
            ?? throw new WorkflowException("Hendelsen finnes ikke.");
        if (hendelse.Prioritet == prioritet) return;
        hendelse.Prioritet = prioritet;
        Logg(aktør, "HendelsePrioritet", $"{hendelse.Navn}: prioritet satt til {prioritet}", hendelseId: hendelseId);
    });

    public Task SetIncidentStatusAsync(int hendelseId, string status, ClaimsPrincipal actor) => Mutate(async () =>
    {
        var aktør = Require(actor, AppRoles.Kriseledelse);
        if (!Statuser.Hendelse.Alle.Contains(status)) throw new WorkflowException("Ugyldig status.");
        var hendelse = await db.Hendelser.SingleOrDefaultAsync(h => h.HendelseID == hendelseId)
            ?? throw new WorkflowException("Hendelsen finnes ikke.");
        if (hendelse.Status == status) return;
        hendelse.Status = status;
        Logg(aktør, "HendelseStatus", $"{hendelse.Navn}: status satt til {Statuser.Hendelse.Etikett(status)}", hendelseId: hendelseId);
    });

    public Task CloseIncidentAsync(int hendelseId, ClaimsPrincipal actor) => Mutate(async () =>
    {
        var aktør = Require(actor, AppRoles.Kriseledelse);
        var hendelse = await db.Hendelser.SingleOrDefaultAsync(h => h.HendelseID == hendelseId)
            ?? throw new WorkflowException("Hendelsen finnes ikke.");
        if (hendelse.Status == Statuser.Hendelse.Avsluttet) throw new WorkflowException("Hendelsen er allerede avsluttet.");
        hendelse.Status = Statuser.Hendelse.Avsluttet;
        Logg(aktør, "HendelseAvsluttet", hendelse.Navn);
    });

    // ---------- Behov ----------

    public async Task<int> CreateNeedAsync(BehovSkjemaViewModel values, ClaimsPrincipal actor)
    {
        var id = 0;
        await Mutate(async () =>
        {
            var aktør = Require(actor, AppRoles.OffentligAktor);
            await BindAccountContactAsync(values, aktør);
            if (string.IsNullOrWhiteSpace(values.Beskrivelse) || values.Beskrivelse.Trim().Length is < 10 or > 2000 ||
                !Statuser.Prioritet.Alle.Contains(values.Prioritet) || values.Antall is < 1 or > 10000 ||
                string.IsNullOrWhiteSpace(values.Kontaktperson) || values.Kontaktperson.Length > 150 ||
                string.IsNullOrWhiteSpace(values.Kontakttelefon) || values.Kontakttelefon.Length > 20 ||
                values.Stedsnavn.Length is 0 or > 200 || !PlaceGeocoder.ValidPoint(values.Breddegrad, values.Lengdegrad))
                throw new WorkflowException("Oppgi beskrivelse, prioritet, antall, kontaktinformasjon og plassering.");
            if (values.BehovstypeID == BehovSkjemaViewModel.AnnenType)
                values.BehovstypeID = (await HentEllerOpprettBehovstype(values.NyBehovstype ?? string.Empty, aktør)).BehovstypeID;
            else if (!await db.Behovstyper.AnyAsync(t => t.BehovstypeID == values.BehovstypeID))
                throw new WorkflowException("Velg en gyldig behovstype.");
            if (values.HendelseID is int hendelseId &&
                !await db.Hendelser.AnyAsync(h => h.HendelseID == hendelseId && h.Status == Statuser.Hendelse.Aktiv))
                throw new WorkflowException("Velg en aktiv hendelse eller ingen hendelse.");
            var behov = new Behov
            {
                BehovstypeID = values.BehovstypeID, RegistrertAvID = aktør, HendelseID = values.HendelseID,
                Beskrivelse = values.Beskrivelse.Trim(), Prioritet = values.Prioritet, Antall = values.Antall,
                Frist = values.Frist?.ToUniversalTime(), Kontaktperson = values.Kontaktperson.Trim(),
                Kontakttelefon = values.Kontakttelefon.Trim(), Breddegrad = Koordinat(values.Breddegrad!.Value),
                Lengdegrad = Koordinat(values.Lengdegrad!.Value), Stedsnavn = values.Stedsnavn,
                Status = Statuser.Behov.Ny, Registrert = DateTime.UtcNow
            };
            db.Behov.Add(behov);
            await db.SaveChangesAsync();
            id = behov.BehovID;
            Logg(aktør, "BehovRegistrert", behov.Beskrivelse, behovId: id);
        });
        return id;
    }

    public Task ChangeNeedPriorityAsync(int behovId, string prioritet, ClaimsPrincipal actor, string? reason) =>
        Mutate(async () =>
        {
            var aktør = Require(actor, AppRoles.Kriseledelse);
            var behov = await HentBehov(behovId);
            Aktivt(behov);
            if (!Statuser.Prioritet.Alle.Contains(prioritet) || prioritet == behov.Prioritet)
                throw new WorkflowException("Velg en ny, gyldig prioritet.");
            behov.Prioritet = prioritet;
            Logg(aktør, "BehovPrioritet", Begrunnelse(reason, $"Prioritet satt til {prioritet}."), behovId: behovId);
        });

    public Task ChangeNeedStatusAsync(int behovId, string status, ClaimsPrincipal actor, string? reason) =>
        Mutate(async () =>
        {
            var aktør = Require(actor, AppRoles.Kriseledelse);
            var behov = await HentBehov(behovId);
            Aktivt(behov);
            if (!WorkflowTransitions.CanMoveNeed(behov.Status, status))
                throw new WorkflowException("Ugyldig statusovergang.");
            if (status == Statuser.Behov.Løst && await db.Forespørsler.AnyAsync(f => f.BehovID == behovId &&
                    (f.Svar == Statuser.Svar.Venter || (f.Svar == Statuser.Svar.Godtatt &&
                        (f.Tildeling == null || f.Tildeling.Fullført == null)))))
                throw new WorkflowException("Avslutt forespørsler og fullfør tildelinger først.");
            behov.Status = status;
            Logg(aktør, "BehovStatus", Begrunnelse(reason, $"Status satt til {status}."), behovId: behovId);
        });

    // ---------- Forespørsel og tildeling ----------

    /// <summary>Dispatch reserverer en mengde av en ressurs for et behov.</summary>
    public Task<int> CreateRequestAsync(int behovId, int ressursId, ClaimsPrincipal actor, string? reason = null) =>
        CreateRequestAsync(behovId, ressursId, 1, actor, reason);

    public async Task<int> CreateRequestAsync(int behovId, int ressursId, decimal antall, ClaimsPrincipal actor, string? reason = null)
    {
        var id = 0;
        await Mutate(async () =>
        {
            var aktør = Require(actor, AppRoles.DispatchTeam);
            var ressurs = await HentRessurs(ressursId);
            var behov = await HentBehov(behovId);
            Aktivt(behov);
            if (antall is <= 0 or > 1000000)
                throw new WorkflowException("Antallet må være større enn 0.");
            if (!WorkflowEligibility.IsEligible(ressurs, DateTime.UtcNow, GyldighetDager))
                throw new WorkflowException("Ressursen må være tilgjengelig og ha gyldig godkjenning.");
            if (!behov.Behovstype!.PassendeRessurstyper.Any(link => link.RessurstypeID == ressurs.RessurstypeID))
                throw new WorkflowException("Ressursen har ikke riktig type for dette behovet.");
            if (antall > ressurs.TilgjengeligAntall)
                throw new WorkflowException($"Bare {ressurs.TilgjengeligAntall:0.##} {ressurs.Enhet} er tilgjengelig.");
            var forespørsel = new Forespørsel
            {
                BehovID = behovId, RessursID = ressursId, SendtAvID = aktør, Sendt = DateTime.UtcNow,
                Svar = Statuser.Svar.Venter, Antall = antall
            };
            ressurs.ReservertAntall += antall;
            OppdaterLagerstatus(ressurs);
            if (behov.Status == Statuser.Behov.Ny) behov.Status = Statuser.Behov.Vurderes;
            db.Forespørsler.Add(forespørsel);
            await db.SaveChangesAsync();
            id = forespørsel.ForespørselID;
            Logg(aktør, "ForespørselSendt", Begrunnelse(reason, $"Forespurt til behov {behovId}."), ressursId, behovId, id);
        }, "Ressursen ble forespurt av en annen samtidig. Ingen endringer er lagret. Last siden på nytt.");
        return id;
    }

    private async Task<Forespørsel> HentForespørsel(int id) =>
        await db.Forespørsler
            .Include(f => f.Ressurs!).ThenInclude(r => r.Verifiseringer)
            .Include(f => f.Behov).Include(f => f.Tildeling)
            .SingleOrDefaultAsync(f => f.ForespørselID == id) ?? throw new WorkflowException("Forespørselen finnes ikke.");

    /// <summary>Ressursens eier godtar eller avslår forespørselen.</summary>
    public Task RespondAsync(int forespørselId, bool accept, ClaimsPrincipal actor, string? reason) => Mutate(async () =>
    {
        var aktør = Actor(actor);
        var f = await HentForespørsel(forespørselId);
        var ressurs = f.Ressurs!;
        if (!ResourcePermissions.CanManage(actor, ressurs.EierID))
            throw new WorkflowException("Bare ressursens eier kan svare på forespørselen.");
        if (f.Svar != Statuser.Svar.Venter) throw new WorkflowException("Forespørselen er allerede besvart.");
        if (accept)
        {
            Aktivt(f.Behov!);
            if (ressurs.ReservertAntall < f.Antall ||
                !WorkflowEligibility.HasCurrentApproval(ressurs, DateTime.UtcNow, GyldighetDager))
                throw new WorkflowException("Ressursen har ikke lenger gyldig godkjenning.");
        }
        else
        {
            ressurs.ReservertAntall -= f.Antall;
            OppdaterLagerstatus(ressurs);
        }
        f.Svar = accept ? Statuser.Svar.Godtatt : Statuser.Svar.Avslått;
        f.Svart = DateTime.UtcNow;
        Logg(aktør, accept ? "ForespørselGodtatt" : "ForespørselAvslått",
            Begrunnelse(reason, accept ? "Godtatt av eier." : "Avslått av eier."), ressurs.RessursID, f.BehovID, f.ForespørselID);
    });

    /// <summary>Dispatch gjør en godtatt forespørsel om til en tildeling.</summary>
    public Task AllocateAsync(int forespørselId, ClaimsPrincipal actor, string? reason = null) => Mutate(async () =>
    {
        var aktør = Require(actor, AppRoles.DispatchTeam);
        var f = await HentForespørsel(forespørselId);
        var ressurs = f.Ressurs!;
        var behov = f.Behov!;
        Aktivt(behov);
        if (f.Tildeling is not null) throw new WorkflowException("Forespørselen er allerede tildelt.");
        if (f.Svar != Statuser.Svar.Godtatt || ressurs.ReservertAntall < f.Antall ||
            !WorkflowEligibility.HasCurrentApproval(ressurs, DateTime.UtcNow, GyldighetDager))
            throw new WorkflowException("Tildeling krever at eieren har godtatt og at ressursen har gyldig godkjenning.");
        if (ressurs.TilgjengeligAntall <= 0) ressurs.Status = Statuser.Ressurs.Tildelt;
        behov.Status = Statuser.Behov.Tildelt;
        db.Tildelinger.Add(new Tildeling
        {
            ForespørselID = f.ForespørselID, Tildelt = DateTime.UtcNow, SistOppdatert = DateTime.UtcNow,
            Fremdrift = Statuser.Fremdrift.Bekreftet
        });
        Logg(aktør, "Tildelt", Begrunnelse(reason, "Tildelt av dispatch."), ressurs.RessursID, f.BehovID, f.ForespørselID);
    }, "Ressursen er allerede tildelt eller ble endret av en annen bruker. Ingen endringer er lagret. Last siden på nytt.");

    /// <summary>Dispatch trekker tilbake en forespørsel som ikke er tildelt ennå.</summary>
    public Task CancelAsync(int forespørselId, ClaimsPrincipal actor, string? reason) => Mutate(async () =>
    {
        var aktør = Require(actor, AppRoles.DispatchTeam);
        var f = await HentForespørsel(forespørselId);
        if (f.Tildeling is not null || f.Svar is not (Statuser.Svar.Venter or Statuser.Svar.Godtatt))
            throw new WorkflowException("Kun ventende eller godtatte forespørsler uten tildeling kan avbrytes.");
        f.Ressurs!.ReservertAntall -= f.Antall;
        OppdaterLagerstatus(f.Ressurs);
        f.Svar = Statuser.Svar.Avbrutt;
        f.Svart ??= DateTime.UtcNow;
        Logg(aktør, "ForespørselAvbrutt", Begrunnelse(reason, "Avbrutt av dispatch."), f.RessursID, f.BehovID, f.ForespørselID);
    });

    /// <summary>Fremdrift ett steg om gangen. «fremme» gir ressursen «i bruk», «fullført» gir «returnert».</summary>
    public Task UpdateAssignmentAsync(int tildelingId, string neste, ClaimsPrincipal actor, string? reason) =>
        UpdateAssignmentAsync(tildelingId, neste, actor, reason, null, null, null, null);

    public Task UpdateAssignmentAsync(int tildelingId, string neste, ClaimsPrincipal actor, string? reason,
        DateTime? forventetFremme, string? mottakskontakt, decimal? levertAntall, string? avvik) => Mutate(async () =>
    {
        var aktør = Require(actor, AppRoles.DispatchTeam);
        var t = await db.Tildelinger.Include(x => x.Forespørsel!).ThenInclude(f => f.Ressurs)
            .SingleOrDefaultAsync(x => x.TildelingID == tildelingId) ?? throw new WorkflowException("Tildelingen finnes ikke.");
        if (!WorkflowTransitions.CanMoveAssignment(t.Fremdrift, neste))
            throw new WorkflowException("Ugyldig statusovergang.");
        var ressurs = t.Forespørsel!.Ressurs!;
        if (ressurs.ReservertAntall < t.Forespørsel.Antall)
            throw new WorkflowException("Tildelingen er ikke lenger aktiv.");
        if (mottakskontakt?.Trim().Length > 150)
            throw new WorkflowException("Mottakskontakten kan ha høyst 150 tegn.");
        if (avvik?.Trim().Length > 1000)
            throw new WorkflowException("Avviket kan ha høyst 1000 tegn.");
        if (levertAntall is <= 0 or > 1000000)
            throw new WorkflowException("Levert antall må være større enn 0.");
        if (levertAntall > t.Forespørsel.Antall)
            throw new WorkflowException($"Levert antall kan ikke overstige {t.Forespørsel.Antall:0.##} {ressurs.Enhet}.");
        t.Fremdrift = neste;
        t.SistOppdatert = DateTime.UtcNow;
        if (forventetFremme is not null) t.ForventetFremme = forventetFremme;
        if (!string.IsNullOrWhiteSpace(mottakskontakt)) t.Mottakskontakt = mottakskontakt.Trim();
        if (!string.IsNullOrWhiteSpace(avvik)) t.Avvik = avvik.Trim();
        if (neste == Statuser.Fremdrift.Fremme && ressurs.TilgjengeligAntall <= 0) ressurs.Status = Statuser.Ressurs.IBruk;
        if (neste == Statuser.Fremdrift.Fullført)
        {
            t.Fullført = DateTime.UtcNow;
            t.OverleveringBekreftet = DateTime.UtcNow;
            t.LevertAntall ??= levertAntall ?? t.Forespørsel.Antall;
            ressurs.ReservertAntall -= t.Forespørsel.Antall;
            if (ressurs.ErForbruksvare)
            {
                ressurs.Antall -= t.LevertAntall.Value;
                OppdaterLagerstatus(ressurs);
            }
            else
            {
                // Etter bruk må utstyr kontrolleres på nytt før det kan brukes igjen.
                ressurs.Status = Statuser.Ressurs.Returnert;
            }
        }
        var handling = "Fremdrift" + string.Concat(neste.Split(' ').Select(Statuser.Visning));
        var standard = neste == Statuser.Fremdrift.Fullført
            ? $"Overlevering bekreftet: {t.LevertAntall:0.##} {ressurs.Enhet}."
            : $"Fremdrift: {neste}.";
        Logg(aktør, handling, Begrunnelse(reason, standard), ressurs.RessursID, t.Forespørsel.BehovID, t.ForespørselID);
    });
}
