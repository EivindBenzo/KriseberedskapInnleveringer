using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>Small demo data set so every role has something to work with. Runs once; see <see cref="Marker"/>.</summary>
public static class DemoDataSeeder
{
    public const string Marker = "DemoDataSeeded";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.WorkflowAudits.AnyAsync(a => a.Action == Marker)) return;

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        async Task<string> Id(string role) =>
            (await users.FindByEmailAsync(IdentitySeeder.DemoAccounts.First(a => a.Role == role).Email))?.Id
            ?? throw new InvalidOperationException("Demokontoene mangler.");
        var publicActor = await Id(AppRoles.OffentligAktor);
        var provider = await Id(AppRoles.Ressursleverandor);
        var dispatch = await Id(AppRoles.DispatchTeam);
        var verifier = await Id(AppRoles.Verifiseringsansvarlig);
        var leader = await Id(AppRoles.Kriseledelse);

        var now = DateTime.UtcNow;
        var validity = TimeSpan.FromDays(30);
        MapSubmission Resource(string name, string type, string place, double lat, double lng, string capability,
            bool approved, ResourceAvailability availability, int createdHoursAgo) => new()
        {
            ResourceName = name, ResourceType = type, LocationName = place, Latitude = lat, Longitude = lng,
            Capability = capability, OwnerId = provider, Availability = availability,
            CreatedAtUtc = now.AddHours(-createdHoursAgo), UpdatedAtUtc = now.AddHours(-createdHoursAgo),
            VerificationStatus = approved ? ResourceVerificationStatus.Approved : ResourceVerificationStatus.Pending,
            VerifiedAtUtc = approved ? now.AddHours(-createdHoursAgo + 1) : null,
            VerificationExpiresAtUtc = approved ? now.AddHours(-createdHoursAgo + 1).Add(validity) : null
        };

        var truck = Resource("Terrengbil 4x4", "Kjøretøy", "Hamar", 60.7945, 11.0680,
            "Plass til 8 personer, vinterutstyr", true, ResourceAvailability.Available, 30);
        var drone = Resource("Drone med varmekamera", "Drone", "Lillehammer", 61.1153, 10.4662,
            "Rekkevidde 6 km, 40 min flytid", true, ResourceAvailability.Available, 28);
        var generator = Resource("Aggregat 20 kVA", "Generator", "Elverum", 60.8819, 11.5623,
            "230/400 V, tilhenger", true, ResourceAvailability.Allocated, 26);
        var excavator = Resource("Beltegraver 14 tonn", "Maskin", "Kongsvinger", 60.1904, 12.0014,
            "Transport med trailer", false, ResourceAvailability.Available, 2);
        db.MapSubmissions.AddRange(truck, drone, generator, excavator);

        var flood = new OperationalNeed
        {
            Description = "Evakuering av 12 beboere fra flomutsatt område. Trenger kjøretøy med god bakkeklaring.",
            Place = "Elverum", Priority = NeedPriority.High, Status = NeedStatus.Open,
            CreatedAtUtc = now.AddHours(-5), CreatedById = publicActor
        };
        var power = new OperationalNeed
        {
            Description = "Strøm til midlertidig mottak i idrettshallen.",
            Place = "Hamar", Priority = NeedPriority.Critical, Status = NeedStatus.Assigned,
            CreatedAtUtc = now.AddHours(-24), CreatedById = publicActor
        };
        var search = new OperationalNeed
        {
            Description = "Søk etter savnet person i skogsområde nord for tettstedet.",
            Place = "Kongsvinger", Priority = NeedPriority.Normal, Status = NeedStatus.InProgress,
            CreatedAtUtc = now.AddHours(-8), CreatedById = publicActor
        };
        db.OperationalNeeds.AddRange(flood, power, search);
        await db.SaveChangesAsync();

        var allocated = new ResourceRequest
        {
            NeedId = power.Id, ResourceId = generator.Id, Status = ResourceRequestStatus.Allocated,
            CreatedAtUtc = now.AddHours(-22), CreatedById = dispatch, Reason = "Aggregatet dekker behovet i hallen."
        };
        var pending = new ResourceRequest
        {
            NeedId = search.Id, ResourceId = drone.Id, Status = ResourceRequestStatus.Pending,
            CreatedAtUtc = now.AddHours(-1), CreatedById = dispatch, Reason = "Varmekamera trengs til søk i skogen."
        };
        db.ResourceRequests.AddRange(allocated, pending);
        await db.SaveChangesAsync();

        db.ResourceAssignments.Add(new ResourceAssignment
        {
            RequestId = allocated.Id, ResourceId = generator.Id, NeedId = power.Id,
            Status = AssignmentStatus.EnRoute, ActiveResourceId = generator.Id,
            CreatedAtUtc = now.AddHours(-20), UpdatedAtUtc = now.AddHours(-19), CreatedById = dispatch
        });

        void Audit(string actor, string action, string reason, double hoursAgo, int? resourceId = null,
            int? needId = null, int? requestId = null) =>
            db.WorkflowAudits.Add(new WorkflowAudit
            {
                ActorId = actor, Action = action, Reason = reason, OccurredAtUtc = now.AddHours(-hoursAgo),
                ResourceId = resourceId, NeedId = needId, RequestId = requestId
            });
        Audit(provider, "ResourceRegistered", "Kjøretøy: Terrengbil 4x4", 30, truck.Id);
        Audit(verifier, "ResourceApproved", "Godkjent av verifiseringsansvarlig.", 29, truck.Id);
        Audit(provider, "ResourceRegistered", "Drone: Drone med varmekamera", 28, drone.Id);
        Audit(verifier, "ResourceApproved", "Godkjent av verifiseringsansvarlig.", 27, drone.Id);
        Audit(provider, "ResourceRegistered", "Generator: Aggregat 20 kVA", 26, generator.Id);
        Audit(verifier, "ResourceApproved", "Godkjent av verifiseringsansvarlig.", 25, generator.Id);
        Audit(provider, "ResourceRegistered", "Maskin: Beltegraver 14 tonn", 2, excavator.Id);
        Audit(publicActor, "NeedCreated", power.Description, 24, needId: power.Id);
        Audit(publicActor, "NeedCreated", search.Description, 8, needId: search.Id);
        Audit(publicActor, "NeedCreated", flood.Description, 5, needId: flood.Id);
        Audit(dispatch, "RequestCreated", allocated.Reason, 22, generator.Id, power.Id, allocated.Id);
        Audit(provider, "RequestAccept", "Aggregatet er klart.", 21, generator.Id, power.Id, allocated.Id);
        Audit(dispatch, "RequestAllocate", "Tildelt til mottaket.", 20, generator.Id, power.Id, allocated.Id);
        Audit(dispatch, "AssignmentEnRoute", "Aggregatet er på vei.", 19, generator.Id, power.Id, allocated.Id);
        Audit(dispatch, "RequestCreated", pending.Reason, 1, drone.Id, search.Id, pending.Id);
        Audit(leader, Marker, "Demodata opprettet.", 0);
        await db.SaveChangesAsync();
    }
}
