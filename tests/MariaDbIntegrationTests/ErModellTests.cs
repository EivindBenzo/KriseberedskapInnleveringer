using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MariaDbIntegrationTests;

/// <summary>Sjekker at databasen som migrasjonen lager i MariaDB, stemmer med ER-diagrammet i rapporten.</summary>
public class ErModellTests
{
    private static async Task<List<string>> Kolonner(AppDbContext db, string tabell)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION";
        var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = tabell; cmd.Parameters.Add(p);
        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    [Theory]
    [InlineData("Bruker", "BrukerID", "Fornavn", "Etternavn", "Epost", "Telefon", "Organisasjon", "PassordHash")]
    [InlineData("Rolle", "RolleID", "Navn")]
    [InlineData("BrukerRolle", "BrukerID", "RolleID", "Tildelt")]
    [InlineData("Hendelse", "HendelseID", "OpprettetAv", "Navn", "Beskrivelse", "Område", "Starttidspunkt", "Status")]
    [InlineData("Behovstype", "BehovstypeID", "Navn")]
    [InlineData("Ressurstype", "RessurstypeID", "Navn")]
    [InlineData("BehovstypeRessurstype", "BehovstypeID", "RessurstypeID")]
    [InlineData("Behov", "BehovID", "BehovstypeID", "RegistrertAv", "HendelseID", "Beskrivelse", "Prioritet", "Antall", "Frist",
        "Kontaktperson", "Kontakttelefon", "Breddegrad", "Lengdegrad", "Status", "Registrert")]
    [InlineData("Ressurs", "RessursID", "RessurstypeID", "EierID", "Navn", "Beskrivelse", "TilgjengeligFra", "TilgjengeligTil",
        "Kontaktperson", "Kontakttelefon", "Breddegrad", "Lengdegrad", "Status", "Registrert")]
    [InlineData("Verifisering", "VerifiseringID", "RessursID", "VerifisertAv", "Tidspunkt", "Resultat", "Begrunnelse")]
    [InlineData("Forespørsel", "ForespørselID", "BehovID", "RessursID", "SendtAv", "Sendt", "Svar", "Svart")]
    [InlineData("Tildeling", "TildelingID", "ForespørselID", "Tildelt", "Fremdrift", "SistOppdatert", "Fullført")]
    public async Task TabellenHarKolonneneFraErDiagrammet(string tabell, params string[] kolonner)
    {
        await using var test = await TestDatabase.CreateAsync();
        var faktiske = await Kolonner(test.Db, tabell);
        Assert.NotEmpty(faktiske);
        foreach (var kolonne in kolonner) Assert.Contains(kolonne, faktiske);
    }

    [Fact]
    public async Task OppslagsdataErLagtInnOgHverBehovstypeHarMinstEnRessurstype()
    {
        await using var test = await TestDatabase.CreateAsync();
        Assert.Equal(SeedData.Ressurstyper.Length, await test.Db.Ressurstyper.CountAsync());
        Assert.Equal(SeedData.Behovstyper.Length, await test.Db.Behovstyper.CountAsync());
        foreach (var behovstype in await test.Db.Behovstyper.Include(b => b.PassendeRessurstyper).ToListAsync())
            Assert.NotEmpty(behovstype.PassendeRessurstyper);
        Assert.Equal(AppRoles.All.Count, await test.Db.Roles.CountAsync());
    }

    [Fact]
    public async Task TildelingTillaterBareEnTildelingPerForespørsel()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (eier, _) = await test.BrukerAsync(AppRoles.Ressursleverandor);
        var (offentlig, _) = await test.BrukerAsync(AppRoles.OffentligAktor);
        var (dispatch, _) = await test.BrukerAsync(AppRoles.DispatchTeam);
        var ressurs = new Ressurs { RessurstypeID = 1, EierID = eier.Id, Navn = "Bil", Kontaktperson = "A", Kontakttelefon = "1", Stedsnavn = "Hamar" };
        var behov = new Behov { BehovstypeID = 1, RegistrertAvID = offentlig.Id, Beskrivelse = "Transport", Kontaktperson = "B", Kontakttelefon = "2", Stedsnavn = "Hamar" };
        test.Db.AddRange(ressurs, behov);
        await test.Db.SaveChangesAsync();
        var forespørsel = new Forespørsel { BehovID = behov.BehovID, RessursID = ressurs.RessursID, SendtAvID = dispatch.Id };
        test.Db.Add(forespørsel);
        await test.Db.SaveChangesAsync();
        test.Db.Add(new Tildeling { ForespørselID = forespørsel.ForespørselID });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();
        test.Db.Add(new Tildeling { ForespørselID = forespørsel.ForespørselID });
        await Assert.ThrowsAsync<DbUpdateException>(() => test.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task NorskeTegnOgKoordinaterLagresUendret()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (eier, _) = await test.BrukerAsync(AppRoles.Ressursleverandor, "Åse");
        test.Db.Add(new Ressurs
        {
            RessurstypeID = 4, EierID = eier.Id, Navn = "Aggregat Ø", Beskrivelse = "æ, ø og å",
            Kontaktperson = "Åse Østby", Kontakttelefon = "40000000", Stedsnavn = "Kristiansand sør",
            Breddegrad = 58.146712m, Lengdegrad = 7.995634m
        });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();
        var lagret = await test.Db.Ressurser.Include(r => r.Ressurstype).Include(r => r.Eier).SingleAsync();
        Assert.Equal("Aggregat Ø", lagret.Navn);
        Assert.Equal("æ, ø og å", lagret.Beskrivelse);
        Assert.Equal(58.146712m, lagret.Breddegrad);
        Assert.Equal("Generator", lagret.Ressurstype!.Navn);
        Assert.Equal("Åse", lagret.Eier!.Fornavn);
        Assert.Equal(Statuser.Ressurs.VenterPåVerifisering, lagret.Status);
    }

    [Fact]
    public async Task EnBrukerMedDataKanIkkeSlettesVedEtUhell()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (eier, _) = await test.BrukerAsync(AppRoles.Ressursleverandor);
        test.Db.Add(new Ressurs { RessurstypeID = 1, EierID = eier.Id, Navn = "Bil", Kontaktperson = "A", Kontakttelefon = "1", Stedsnavn = "X" });
        await test.Db.SaveChangesAsync();
        // Sletter direkte i databasen, slik at det er fremmednøkkelen i MariaDB (ON DELETE RESTRICT) som stopper det.
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() =>
            test.Db.Users.Where(u => u.Id == eier.Id).ExecuteDeleteAsync());
    }

    // ---------- Integritetsregler lagt til i migrasjonen DatabaseIntegritet ----------

    private static async Task<int> Sql(AppDbContext db, string sql) => await db.Database.ExecuteSqlRawAsync(sql);

    private static async Task<(int EierId, int RessursId)> RessursMedSql(TestDatabase test)
    {
        var (eier, _) = await test.BrukerAsync(AppRoles.Ressursleverandor);
        // Uten ReservertAntall i kolonnelisten, slik testdata ofte skrives for hånd.
        await Sql(test.Db, $"""
            INSERT INTO Ressurs (RessurstypeID, EierID, Navn, TilgjengeligFra, Kontaktperson, Kontakttelefon,
                                 Breddegrad, Lengdegrad, Stedsnavn, Status, Registrert)
            VALUES (1, {eier.Id}, 'Traktor', '2026-01-01', 'Ola', '40000000', 58.1, 8.0, 'Kristiansand',
                    'tilgjengelig', NOW())
            """);
        return (eier.Id, await test.Db.Ressurser.MaxAsync(r => r.RessursID));
    }

    [Fact]
    public async Task NyRessursLagtInnMedSqlHarIngentingReservert()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (_, id) = await RessursMedSql(test);
        var ressurs = await test.Db.Ressurser.SingleAsync(r => r.RessursID == id);
        Assert.Equal(0m, ressurs.ReservertAntall);
        Assert.Equal(1m, ressurs.Antall);
    }

    [Theory]
    [InlineData("UPDATE Ressurs SET Status = 'ødelagt'")]
    [InlineData("UPDATE Ressurs SET Antall = -1")]
    [InlineData("UPDATE Ressurs SET TilgjengeligTil = '2025-12-31'")]
    [InlineData("UPDATE Ressurs SET Breddegrad = 91")]
    public async Task UgyldigeVerdierStoppesAvCheckConstraints(string sql)
    {
        await using var test = await TestDatabase.CreateAsync();
        await RessursMedSql(test);
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => Sql(test.Db, sql));
    }

    [Fact]
    public async Task SammeBehovstypeKanIkkeLeggesInnToGanger()
    {
        await using var test = await TestDatabase.CreateAsync();
        var navn = await test.Db.Behovstyper.Select(b => b.Navn).FirstAsync();
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() =>
            Sql(test.Db, $"INSERT INTO Behovstype (Navn) VALUES ('{navn}')"));
    }

    [Fact]
    public async Task LoggenKreverEnEksisterendeBrukerOgBeholderRaderNårEtBehovSlettes()
    {
        await using var test = await TestDatabase.CreateAsync();
        var (offentlig, _) = await test.BrukerAsync(AppRoles.OffentligAktor);
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => Sql(test.Db,
            "INSERT INTO Hendelseslogg (UtførtAv, Tidspunkt, Handling, Begrunnelse) VALUES (999999, NOW(), 'Test', 'Finnes ikke')"));

        await Sql(test.Db, $"""
            INSERT INTO Behov (BehovstypeID, RegistrertAv, Beskrivelse, Prioritet, Antall, Kontaktperson, Kontakttelefon,
                               Breddegrad, Lengdegrad, Stedsnavn, Status, Registrert)
            VALUES (1, {offentlig.Id}, 'Transport', 'akutt', 2, 'Kari', '40000001', 58.1, 8.0, 'Vennesla', 'ny', NOW())
            """);
        var behovId = await test.Db.Behov.MaxAsync(b => b.BehovID);
        await Sql(test.Db, $"INSERT INTO Hendelseslogg (UtførtAv, BehovID, Tidspunkt, Handling, Begrunnelse) VALUES ({offentlig.Id}, {behovId}, NOW(), 'BehovMeldt', 'Test')");
        await Sql(test.Db, $"DELETE FROM Behov WHERE BehovID = {behovId}");

        var logg = await test.Db.Hendelseslogg.AsNoTracking().SingleAsync(l => l.Handling == "BehovMeldt");
        Assert.Null(logg.BehovID);
    }
}
