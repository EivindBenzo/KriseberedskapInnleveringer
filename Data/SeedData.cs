using HeimevernetInnlevering1.Models;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>
/// Faste oppslagsdata som legges inn gjennom migrasjonen (HasData).
/// Endres listene, må det lages en ny migrasjon.
/// </summary>
public static class SeedData
{
    // Ressurstypene fra prototypen. Kartikonene i wwwroot/images/resources er knyttet til disse navnene.
    public static readonly (int Id, string Navn)[] Ressurstyper =
    [
        (1, "Kjøretøy"), (2, "Maskin"), (3, "Drone"), (4, "Generator"), (5, "Personell"), (6, "Utstyr")
    ];

    public static readonly (int Id, string Navn)[] Behovstyper =
    [
        (1, "Transport"), (2, "Strøm"), (3, "Søk og overvåking"), (4, "Mannskap"), (5, "Graving og rydding"), (6, "Utstyr")
    ];

    // Hvilke ressurstyper som kan dekke hvilke behovstyper. Hver behovstype har minst én (1..*).
    public static readonly (int BehovstypeID, int RessurstypeID)[] Koblinger =
    [
        (1, 1),          // Transport – Kjøretøy
        (2, 4),          // Strøm – Generator
        (3, 3), (3, 5),  // Søk og overvåking – Drone, Personell
        (4, 5),          // Mannskap – Personell
        (5, 2),          // Graving og rydding – Maskin
        (6, 6)           // Utstyr – Utstyr
    ];

    public static IReadOnlyList<string> RessurstypeNavn => Ressurstyper.Select(t => t.Navn).ToList();

    public static void LeggTilOppslagsdata(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ressurstype>().HasData(
            Ressurstyper.Select(t => new { RessurstypeID = t.Id, t.Navn }).ToArray<object>());
        modelBuilder.Entity<Behovstype>().HasData(
            Behovstyper.Select(t => new { BehovstypeID = t.Id, t.Navn }).ToArray<object>());
        modelBuilder.Entity<BehovstypeRessurstype>().HasData(
            Koblinger.Select(k => new { k.BehovstypeID, k.RessurstypeID }).ToArray<object>());
    }
}
