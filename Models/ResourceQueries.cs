using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Models;

/// <summary>Filter som deles av ressurslisten og kartet, slik at begge alltid viser de samme ressursene.</summary>
public sealed record ResourceFilter(string? Type = null, string? Query = null, string? City = null,
    bool Mine = false, bool Eligible = false, int? OwnerId = null)
{
    public static ResourceFilter For(ClaimsPrincipal user, string? type, string? query, string? city, bool mine, bool eligible) =>
        new(type, query, city, mine, eligible || AppRoles.DispatchOnly(user), ResourcePermissions.BrukerId(user));
}

public static class ResourceQueries
{
    /// <summary>Ressurser med type, eier og verifiseringer lastet, slik visningene trenger.</summary>
    public static IQueryable<Ressurs> WithDetails(IQueryable<Ressurs> ressurser) =>
        ressurser.Include(r => r.Ressurstype).Include(r => r.Eier).Include(r => r.Verifiseringer);

    public static IQueryable<Ressurs> Scope(IQueryable<Ressurs> resources, ResourceFilter filter, DateTime nowUtc)
    {
        if (filter.Mine) resources = resources.Where(r => r.EierID == filter.OwnerId);
        if (!string.IsNullOrWhiteSpace(filter.Type)) resources = resources.Where(r => r.Ressurstype!.Navn == filter.Type);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var query = filter.Query.Trim();
            resources = resources.Where(r => r.Navn.Contains(query) || r.Stedsnavn.Contains(query));
        }
        if (filter.Eligible) resources = resources.Where(WorkflowEligibility.EligibleExpression(nowUtc));
        return resources;
    }

    public static bool MatchesCity(Ressurs resource, string? city)
    {
        var wanted = ResourceCity.Normalize(city);
        return wanted.Length == 0 || string.Equals(ResourceCity.FromLocation(resource.Stedsnavn), wanted,
            StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<List<Ressurs>> LoadAsync(AppDbContext db, ResourceFilter filter, DateTime nowUtc)
    {
        var resources = Scope(WithDetails(db.Ressurser.AsNoTracking()), filter, nowUtc);
        var loaded = await resources.OrderByDescending(r => r.Registrert).AsSplitQuery().ToListAsync();
        return loaded.Where(r => MatchesCity(r, filter.City)).ToList();
    }
}
