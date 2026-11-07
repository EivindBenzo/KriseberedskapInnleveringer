using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>
/// Databasekonteksten. Her blir ER-diagrammet fra rapporten oversatt til tabeller i MariaDB:
/// tabell- og kolonnenavn, lengder, primærnøkler, fremmednøkler og kardinaliteter.
///
/// Klassen arver fra IdentityDbContext slik at innloggingen (ASP.NET Core Identity) bruker
/// tabellene Bruker, Rolle og BrukerRolle i stedet for standardtabellene AspNetUsers osv.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<Bruker, Rolle, int,
        IdentityUserClaim<int>, BrukerRolle, IdentityUserLogin<int>,
        IdentityRoleClaim<int>, IdentityUserToken<int>>(options)
{
    public DbSet<Hendelse> Hendelser => Set<Hendelse>();
    public DbSet<Behovstype> Behovstyper => Set<Behovstype>();
    public DbSet<Ressurstype> Ressurstyper => Set<Ressurstype>();
    public DbSet<BehovstypeRessurstype> BehovstypeRessurstyper => Set<BehovstypeRessurstype>();
    public DbSet<Behov> Behov => Set<Behov>();
    public DbSet<Ressurs> Ressurser => Set<Ressurs>();
    public DbSet<Verifisering> Verifiseringer => Set<Verifisering>();
    public DbSet<Forespørsel> Forespørsler => Set<Forespørsel>();
    public DbSet<Tildeling> Tildelinger => Set<Tildeling>();
    public DbSet<Hendelseslogg> Hendelseslogg => Set<Hendelseslogg>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity setter opp sine tabeller først; navnene overstyres under.
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasCharSet("utf8mb4");

        KonfigurerBrukerOgRoller(modelBuilder);
        KonfigurerOppslagstabeller(modelBuilder);
        KonfigurerHendelseOgBehov(modelBuilder);
        KonfigurerRessursOgVerifisering(modelBuilder);
        KonfigurerForespørselOgTildeling(modelBuilder);
        KonfigurerHendelseslogg(modelBuilder);

        SeedData.LeggTilOppslagsdata(modelBuilder);
    }

    private static void KonfigurerBrukerOgRoller(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bruker>(e =>
        {
            e.ToTable("Bruker");
            e.Property(b => b.Id).HasColumnName("BrukerID");
            e.Property(b => b.Fornavn).HasMaxLength(100).IsRequired();
            e.Property(b => b.Etternavn).HasMaxLength(100).IsRequired();
            e.Property(b => b.Email).HasColumnName("Epost").HasMaxLength(255);
            e.Property(b => b.PhoneNumber).HasColumnName("Telefon").HasMaxLength(20);
            e.Property(b => b.Organisasjon).HasMaxLength(150);
            e.Property(b => b.PasswordHash).HasColumnName("PassordHash").HasMaxLength(255);
            e.Ignore(b => b.FulltNavn);
        });

        modelBuilder.Entity<Rolle>(e =>
        {
            e.ToTable("Rolle");
            e.Property(r => r.Id).HasColumnName("RolleID");
            e.Property(r => r.Name).HasColumnName("Navn").HasMaxLength(50);
        });

        // Bruker 0..* – 1..* Rolle via koblingstabellen BrukerRolle.
        modelBuilder.Entity<BrukerRolle>(e =>
        {
            e.ToTable("BrukerRolle");
            e.Property(br => br.UserId).HasColumnName("BrukerID");
            e.Property(br => br.RoleId).HasColumnName("RolleID");
        });

        // Identity sine hjelpetabeller (tokens for tofaktor osv.). De står ikke i ER-diagrammet,
        // men trengs av Identity. Vi gir dem norske navn.
        modelBuilder.Entity<IdentityUserClaim<int>>().ToTable("BrukerClaim");
        modelBuilder.Entity<IdentityUserLogin<int>>().ToTable("BrukerInnlogging");
        modelBuilder.Entity<IdentityUserToken<int>>().ToTable("BrukerToken");
        modelBuilder.Entity<IdentityRoleClaim<int>>().ToTable("RolleClaim");
    }

    private static void KonfigurerOppslagstabeller(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Behovstype>(e =>
        {
            e.ToTable("Behovstype");
            e.HasKey(t => t.BehovstypeID);
            e.Property(t => t.Navn).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Ressurstype>(e =>
        {
            e.ToTable("Ressurstype");
            e.HasKey(t => t.RessurstypeID);
            e.Property(t => t.Navn).HasMaxLength(100).IsRequired();
            e.HasIndex(t => t.Navn).IsUnique();
        });

        // Behovstype 0..* – 1..* Ressurstype
        modelBuilder.Entity<BehovstypeRessurstype>(e =>
        {
            e.ToTable("BehovstypeRessurstype");
            e.HasKey(k => new { k.BehovstypeID, k.RessurstypeID });
            e.HasOne(k => k.Behovstype).WithMany(t => t.PassendeRessurstyper)
                .HasForeignKey(k => k.BehovstypeID).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(k => k.Ressurstype).WithMany(t => t.PassendeBehovstyper)
                .HasForeignKey(k => k.RessurstypeID).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void KonfigurerHendelseOgBehov(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hendelse>(e =>
        {
            e.ToTable("Hendelse");
            e.HasKey(h => h.HendelseID);
            e.Property(h => h.OpprettetAvID).HasColumnName("OpprettetAv");
            e.Property(h => h.Navn).HasMaxLength(150).IsRequired();
            e.Property(h => h.Beskrivelse).HasColumnType("text");
            e.Property(h => h.Område).HasMaxLength(150).IsRequired();
            e.Property(h => h.Status).HasMaxLength(15).IsRequired();
            // Bruker 1 – 0..* Hendelse. Restrict: en bruker med data kan ikke slettes ved et uhell.
            e.HasOne(h => h.OpprettetAv).WithMany()
                .HasForeignKey(h => h.OpprettetAvID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Behov>(e =>
        {
            e.ToTable("Behov");
            e.HasKey(b => b.BehovID);
            e.Property(b => b.RegistrertAvID).HasColumnName("RegistrertAv");
            e.Property(b => b.Beskrivelse).HasColumnType("text").IsRequired();
            e.Property(b => b.Prioritet).HasMaxLength(10).IsRequired();
            e.Property(b => b.Kontaktperson).HasMaxLength(150).IsRequired();
            e.Property(b => b.Kontakttelefon).HasMaxLength(20).IsRequired();
            e.Property(b => b.Breddegrad).HasPrecision(9, 6);
            e.Property(b => b.Lengdegrad).HasPrecision(9, 6);
            e.Property(b => b.Stedsnavn).HasMaxLength(200).IsRequired();
            // Status brukes også som samtidighetskontroll: to brukere kan ikke endre samme behov samtidig.
            e.Property(b => b.Status).HasMaxLength(15).IsRequired().IsConcurrencyToken();

            // Behovstype 1 – 0..* Behov
            e.HasOne(b => b.Behovstype).WithMany()
                .HasForeignKey(b => b.BehovstypeID).OnDelete(DeleteBehavior.Restrict);
            // Bruker 1 – 0..* Behov
            e.HasOne(b => b.RegistrertAv).WithMany()
                .HasForeignKey(b => b.RegistrertAvID).OnDelete(DeleteBehavior.Restrict);
            // Hendelse 0..1 – 0..* Behov. Slettes hendelsen, blir behovet stående uten hendelse.
            e.HasOne(b => b.Hendelse).WithMany(h => h.Behov)
                .HasForeignKey(b => b.HendelseID).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void KonfigurerRessursOgVerifisering(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ressurs>(e =>
        {
            e.ToTable("Ressurs");
            e.HasKey(r => r.RessursID);
            e.Property(r => r.Navn).HasMaxLength(150).IsRequired();
            e.Property(r => r.Beskrivelse).HasColumnType("text");
            e.Property(r => r.Antall).HasPrecision(12, 2).IsRequired();
            e.Property(r => r.ReservertAntall).HasPrecision(12, 2).IsRequired().IsConcurrencyToken();
            e.Property(r => r.Enhet).HasMaxLength(20).IsRequired();
            e.Property(r => r.Kontaktperson).HasMaxLength(150).IsRequired();
            e.Property(r => r.Kontakttelefon).HasMaxLength(20).IsRequired();
            e.Property(r => r.Breddegrad).HasPrecision(9, 6);
            e.Property(r => r.Lengdegrad).HasPrecision(9, 6);
            e.Property(r => r.Stedsnavn).HasMaxLength(200).IsRequired();
            // Status er samtidighetskontroll: to dispatchere kan ikke tildele samme ressurs samtidig.
            e.Property(r => r.Status).HasMaxLength(30).IsRequired().IsConcurrencyToken();
            e.HasIndex(r => r.Status);

            // Ressurstype 1 – 0..* Ressurs
            e.HasOne(r => r.Ressurstype).WithMany()
                .HasForeignKey(r => r.RessurstypeID).OnDelete(DeleteBehavior.Restrict);
            // Bruker (eier) 1 – 0..* Ressurs
            e.HasOne(r => r.Eier).WithMany()
                .HasForeignKey(r => r.EierID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Verifisering>(e =>
        {
            e.ToTable("Verifisering");
            e.HasKey(v => v.VerifiseringID);
            e.Property(v => v.VerifisertAvID).HasColumnName("VerifisertAv");
            e.Property(v => v.Resultat).HasMaxLength(10).IsRequired();
            e.Property(v => v.Begrunnelse).HasColumnType("text");
            // Ressurs 1 – 0..* Verifisering. Slettes ressursen, slettes historikken med den.
            e.HasOne(v => v.Ressurs).WithMany(r => r.Verifiseringer)
                .HasForeignKey(v => v.RessursID).OnDelete(DeleteBehavior.Cascade);
            // Bruker 1 – 0..* Verifisering
            e.HasOne(v => v.VerifisertAv).WithMany()
                .HasForeignKey(v => v.VerifisertAvID).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.RessursID, v.Tidspunkt });
        });
    }

    private static void KonfigurerForespørselOgTildeling(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Forespørsel>(e =>
        {
            e.ToTable("Forespørsel");
            e.HasKey(f => f.ForespørselID);
            e.Property(f => f.SendtAvID).HasColumnName("SendtAv");
            e.Property(f => f.Antall).HasPrecision(12, 2).IsRequired();
            e.Property(f => f.Svar).HasMaxLength(10).IsRequired().IsConcurrencyToken();
            // Behov 1 – 0..* Forespørsel og Ressurs 1 – 0..* Forespørsel.
            // Restrict: historikken skal ikke forsvinne når noen sletter et behov eller en ressurs.
            e.HasOne(f => f.Behov).WithMany(b => b.Forespørsler)
                .HasForeignKey(f => f.BehovID).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.Ressurs).WithMany(r => r.Forespørsler)
                .HasForeignKey(f => f.RessursID).OnDelete(DeleteBehavior.Restrict);
            // Bruker 1 – 0..* Forespørsel
            e.HasOne(f => f.SendtAv).WithMany()
                .HasForeignKey(f => f.SendtAvID).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(f => new { f.RessursID, f.Svar });
        });

        modelBuilder.Entity<Tildeling>(e =>
        {
            e.ToTable("Tildeling");
            e.HasKey(t => t.TildelingID);
            e.Property(t => t.Fremdrift).HasMaxLength(15).IsRequired().IsConcurrencyToken();
            // Forespørsel 1 – 0..1 Tildeling. HasOne/WithOne og unik indeks: én tildeling per forespørsel.
            e.HasOne(t => t.Forespørsel).WithOne(f => f.Tildeling)
                .HasForeignKey<Tildeling>(t => t.ForespørselID).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.ForespørselID).IsUnique();
        });
    }

    private static void KonfigurerHendelseslogg(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hendelseslogg>(e =>
        {
            e.ToTable("Hendelseslogg");
            e.HasKey(l => l.LoggID);
            e.Property(l => l.UtførtAvID).HasColumnName("UtførtAv");
            e.Property(l => l.Handling).HasMaxLength(60).IsRequired();
            e.Property(l => l.Begrunnelse).HasMaxLength(1000).IsRequired();
            e.HasIndex(l => new { l.RessursID, l.Tidspunkt });
            e.HasIndex(l => l.ForespørselID);
        });
    }
}
