using Microsoft.AspNetCore.Identity;

namespace HeimevernetInnlevering1.Models;

/// <summary>
/// Tabellen Bruker i ER-diagrammet. Arver fra IdentityUser slik at ASP.NET Core Identity
/// håndterer passord-hash, innlogging og utestenging. Epost, Telefon og PassordHash er
/// Identity sine egenskaper Email, PhoneNumber og PasswordHash med norske kolonnenavn (se AppDbContext).
/// TofaktorNøkkel lagres av Identity i hjelpetabellen BrukerToken.
/// </summary>
public class Bruker : IdentityUser<int>
{
    public string Fornavn { get; set; } = string.Empty;
    public string Etternavn { get; set; } = string.Empty;
    public string? Organisasjon { get; set; }

    public string FulltNavn => $"{Fornavn} {Etternavn}".Trim();
}

/// <summary>Tabellen Rolle. Navn er Identity sin egenskap Name.</summary>
public class Rolle : IdentityRole<int>
{
    public Rolle() { }
    public Rolle(string navn) : base(navn) { }
}

/// <summary>Koblingstabellen BrukerRolle (m:n). Primærnøkkel (BrukerID, RolleID).</summary>
public class BrukerRolle : IdentityUserRole<int>
{
    public DateTime Tildelt { get; set; } = DateTime.UtcNow;
}
