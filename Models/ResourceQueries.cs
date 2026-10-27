using System.Linq.Expressions;
using System.Security.Claims;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Models;

/// <summary>Filter shared by the resource list and the map so both always show the same resources.</summary>
public sealed record ResourceFilter(string? Type = null, string? Query = null, string? City = null,
    bool Mine = false, bool Eligible = false, string? OwnerId = null)
{
    public static ResourceFilter For(ClaimsPrincipal user, string? type, string? query, string? city, bool mine, bool eligible) =>
        new(type, query, city, mine, eligible || AppRoles.DispatchOnly(user),
            user.FindFirstValue(ClaimTypes.NameIdentifier));
}

public static class ResourceQueries
{
    public static Expression<Func<MapSubmission, bool>> EligibleNow(DateTime nowUtc) => r =>
        r.Availability == ResourceAvailability.Available &&
        r.VerificationStatus == ResourceVerificationStatus.Approved &&
        r.VerifiedAtUtc <= nowUtc && r.VerificationExpiresAtUtc > nowUtc &&
        r.OwnerId != null && r.OwnerId.Trim() != "" &&
        MapSubmissionViewModel.ResourceTypes.Contains(r.ResourceType);

    public static IQueryable<MapSubmission> Scope(IQueryable<MapSubmission> resources, ResourceFilter filter, DateTime nowUtc)
    {
        if (filter.Mine) resources = resources.Where(r => r.OwnerId == filter.OwnerId);
        if (!string.IsNullOrWhiteSpace(filter.Type)) resources = resources.Where(r => r.ResourceType == filter.Type);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var query = filter.Query.Trim();
            resources = resources.Where(r => r.ResourceName.Contains(query) || r.LocationName.Contains(query));
        }
        if (filter.Eligible) resources = resources.Where(EligibleNow(nowUtc));
        return resources;
    }

    public static bool MatchesCity(MapSubmission resource, string? city)
    {
        var wanted = ResourceCity.Normalize(city);
        return wanted.Length == 0 || string.Equals(ResourceCity.FromLocation(resource.LocationName), wanted,
            StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<List<MapSubmission>> LoadAsync(AppDbContext db, ResourceFilter filter, DateTime nowUtc,
        bool onMapOnly = false)
    {
        var resources = Scope(db.MapSubmissions.AsNoTracking(), filter, nowUtc);
        if (onMapOnly) resources = resources.Where(r => r.IsOnMap);
        var loaded = await resources.OrderByDescending(r => r.CreatedAtUtc).ToListAsync();
        return loaded.Where(r => MatchesCity(r, filter.City)).ToList();
    }
}
