using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Models;

/// <summary>Filter som deles av ressurslisten og kartet, slik at begge alltid viser de samme ressursene.</summary>
public sealed record ResourceFilter(string? Type = null, string? Query = null, string? City = null,
    bool Mine = false, bool Eligible = false, int? OwnerId = null)
{
    public static ResourceFilter For(ClaimsPrincipal user, string? type, string? query, string? city, bool mine, bool eligible) =>
        new(type, query, city, mine, eligible, ResourcePermissions.BrukerId(user));
}

public static class ResourceQueries
{
    /// <summary>Ressurser med type, eier og verifiseringer lastet, slik visningene trenger.</summary>
    public static IQueryable<Ressurs> WithDetails(IQueryable<Ressurs> ressurser) =>
        ressurser.Include(r => r.Ressurstype).Include(r => r.Eier).Include(r => r.Verifiseringer);

    public static IQueryable<Ressurs> Scope(IQueryable<Ressurs> resources, ResourceFilter filter, DateTime nowUtc)
    {
        if (filter.Mine) resources = resources.Where(r => r.EierID == filter.OwnerId);
        if (filter.Type == RessursIkon.Annet)
        {
            // «Annet» er alle typer brukerne har lagt til selv, altså alle utenom de seks faste.
            var faste = RessursIkon.FasteTyper.ToList();
            resources = resources.Where(r => !faste.Contains(r.Ressurstype!.Navn));
        }
        else if (!string.IsNullOrWhiteSpace(filter.Type)) resources = resources.Where(r => r.Ressurstype!.Navn == filter.Type);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            // Hvert ord må treffe navn, sted, type eller eier, så «rita ressurs» finner Ritas ressurser.
            foreach (var ord in filter.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6))
            {
                var w = ord;
                resources = resources.Where(r => r.Navn.Contains(w) || r.Stedsnavn.Contains(w) || r.Ressurstype!.Navn.Contains(w)
                    || r.Eier!.Fornavn.Contains(w) || r.Eier!.Etternavn.Contains(w));
            }
        }
        if (filter.Eligible) resources = resources.Where(WorkflowEligibility.EligibleExpression(nowUtc));
        return resources;
    }

    public static bool MatchesCity(Ressurs resource, string? city)
    {
        var wanted = ResourceCity.Normalize(city);
        return wanted.Length == 0 || string.Equals(ResourceCity.FromResource(resource), wanted,
            StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<List<Ressurs>> LoadAsync(AppDbContext db, ResourceFilter filter, DateTime nowUtc)
    {
        var resources = Scope(WithDetails(db.Ressurser.AsNoTracking()), filter, nowUtc);
        var loaded = await resources.OrderByDescending(r => r.Registrert).AsSplitQuery().ToListAsync();
        return loaded.Where(r => MatchesCity(r, filter.City)).ToList();
    }
}
