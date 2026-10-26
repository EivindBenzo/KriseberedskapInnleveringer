using System.Data;
using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeimevernetInnlevering1.Services;

public class WorkflowException(string message) : Exception(message);

public class WorkflowService(AppDbContext db, IOptions<WorkflowOptions> options)
{
    private static string Actor(ClaimsPrincipal actor)
    {
        if (actor.Identity?.IsAuthenticated != true ||
            string.IsNullOrEmpty(actor.FindFirstValue(ClaimTypes.NameIdentifier)))
            throw new WorkflowException("Du må være innlogget.");
        return actor.FindFirstValue(ClaimTypes.NameIdentifier)!;
    }

    private static string Admin(ClaimsPrincipal actor)
    {
        var id = Actor(actor);
        if (!actor.IsInRole("Admin")) throw new WorkflowException("Kun administrator kan utføre dette.");
        return id;
    }

    private static string Reason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new WorkflowException("Oppgi en begrunnelse på 1–1000 tegn.");
        return reason.Trim();
    }

    private static void Version(string current, string? expected)
    {
        if (string.IsNullOrEmpty(expected) || current != expected)
            throw new WorkflowException("Opplysningene er endret. Last siden på nytt.");
    }

    private async Task Mutate(Func<Task> change)
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
            throw new WorkflowException("En annen bruker har endret opplysningene. Last siden på nytt.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw new WorkflowException("Endringen kunne ikke lagres. Last siden på nytt og prøv igjen.");
        }
        catch
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private void Audit(string actorId, string action, string reason, int? resourceId = null,
        int? needId = null, int? requestId = null) => db.WorkflowAudits.Add(new WorkflowAudit
        {
            ActorId = actorId, Action = action, Reason = Reason(reason), OccurredAtUtc = DateTime.UtcNow,
            ResourceId = resourceId, NeedId = needId, RequestId = requestId
        });

    private void Touch(MapSubmission resource)
    {
        resource.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(resource).Property(r => r.ConcurrencyToken).IsModified = true;
    }
    private void Touch(OperationalNeed need) =>
        db.Entry(need).Property(n => n.ConcurrencyToken).IsModified = true;

    private static bool Busy(MapSubmission resource) =>
        resource.Availability is ResourceAvailability.Reserved or ResourceAvailability.Allocated;

    private async Task<MapSubmission> Resource(int id) =>
        await db.MapSubmissions.SingleOrDefaultAsync(r => r.Id == id) ??
        throw new WorkflowException("Ressursen finnes ikke.");

    private async Task<OperationalNeed> Need(int id) =>
        await db.OperationalNeeds.SingleOrDefaultAsync(n => n.Id == id) ??
        throw new WorkflowException("Behovet finnes ikke.");

    private static void Open(OperationalNeed need)
    {
        if (need.Status is not (NeedStatus.Open or NeedStatus.InProgress))
            throw new WorkflowException("Behovet er avsluttet.");
    }

    public async Task<int> ResetMapAsync(ClaimsPrincipal actor, bool confirmed)
    {
        var actorId = Admin(actor);
        if (!confirmed) throw new WorkflowException("Bekreft at alle ressursplasseringer skal fjernes fra kartet.");
        var count = 0;
        await Mutate(async () =>
        {
            var resources = await db.MapSubmissions.Where(r => r.IsOnMap).ToListAsync();
            foreach (var resource in resources)
            {
                resource.IsOnMap = false;
                Touch(resource);
                Audit(actorId, "ResourceRemovedFromMap", "Administrator bekreftet nullstilling av kartet.", resource.Id);
            }
            count = resources.Count;
        });
        return count;
    }

    public Task VerifyAsync(int resourceId, bool approve, string expectedToken, ClaimsPrincipal actor, string reason) =>
        Mutate(async () =>
        {
            var actorId = Admin(actor);
            var resource = await Resource(resourceId);
            Version(resource.ConcurrencyToken, expectedToken);
            if (approve && resource.OwnerId == actorId)
                throw new WorkflowException("Du kan ikke godkjenne din egen ressurs. En annen administrator må kontrollere den.");
            if (Busy(resource)) throw new WorkflowException("Reserverte eller tildelte ressurser må frigjøres først.");
            var days = options.Value.VerificationValidityDays;
            if (approve && (days is < 1 or > 3650))
                throw new WorkflowException("Ugyldig konfigurasjon for godkjenningens varighet.");
            resource.VerificationStatus = approve ? ResourceVerificationStatus.Approved : ResourceVerificationStatus.Rejected;
            resource.VerifiedAtUtc = approve ? DateTime.UtcNow : null;
            resource.VerificationExpiresAtUtc = approve ? resource.VerifiedAtUtc!.Value.AddDays(days) : null;
            Touch(resource);
            Audit(actorId, approve ? "ResourceApproved" : "ResourceRejected", reason, resourceId);
        });

    public Task UpdateResourceAvailabilityAsync(int resourceId, ResourceAvailability availability,
        string expectedToken, ClaimsPrincipal actor, string reason) => Mutate(async () =>
        {
            var actorId = Actor(actor);
            var resource = await Resource(resourceId);
            if (!ResourcePermissions.CanManage(actor, resource.OwnerId))
                throw new WorkflowException("Du kan bare endre egne ressurser.");
            Version(resource.ConcurrencyToken, expectedToken);
            if (Busy(resource) || availability is not (ResourceAvailability.Available or ResourceAvailability.Unavailable))
                throw new WorkflowException("En reservert eller tildelt ressurs kan ikke endres manuelt.");
            resource.Availability = availability;
            Touch(resource);
            Audit(actorId, "AvailabilityChanged", reason, resourceId);
        });

    public Task UpdateResourceAsync(int resourceId, MapSubmission values, ResourceAvailability availability,
        string expectedToken, ClaimsPrincipal actor, string reason) => Mutate(async () =>
        {
            var actorId = Actor(actor);
            var resource = await Resource(resourceId);
            if (!ResourcePermissions.CanManage(actor, resource.OwnerId))
                throw new WorkflowException("Du kan bare endre egne ressurser.");
            Version(resource.ConcurrencyToken, expectedToken);
            if (Busy(resource) || availability is not (ResourceAvailability.Available or ResourceAvailability.Unavailable))
                throw new WorkflowException("En reservert eller tildelt ressurs må frigjøres først.");
            if (string.IsNullOrWhiteSpace(values.ResourceName) || values.ResourceName.Length > 100 ||
                string.IsNullOrWhiteSpace(values.ResourceType) || values.ResourceType.Length > 50 ||
                string.IsNullOrWhiteSpace(values.LocationName) || values.LocationName.Length > 200 ||
                values.Capability?.Length > 1000 || values.TransportRequirements?.Length > 1000 ||
                !PlaceGeocoder.ValidPoint(values.Latitude, values.Longitude))
                throw new WorkflowException("Ugyldige ressursopplysninger.");
            var changed = !resource.IsOnMap || resource.ResourceName != values.ResourceName || resource.ResourceType != values.ResourceType ||
                          resource.LocationName != values.LocationName || resource.Latitude != values.Latitude ||
                          resource.Longitude != values.Longitude || resource.Capability != values.Capability ||
                          resource.TransportRequirements != values.TransportRequirements;
            resource.ResourceName = values.ResourceName;
            resource.ResourceType = values.ResourceType;
            resource.LocationName = values.LocationName;
            resource.Latitude = values.Latitude;
            resource.Longitude = values.Longitude;
            resource.Capability = values.Capability;
            resource.TransportRequirements = values.TransportRequirements;
            resource.Availability = availability;
            resource.IsOnMap = true;
            if (changed) Invalidate(resource);
            Touch(resource);
            Audit(actorId, changed ? "ResourceEditedReverificationRequired" : "AvailabilityChanged", reason, resourceId);
        });

    public Task DeleteResourceAsync(int resourceId, string expectedToken, ClaimsPrincipal actor, string reason) =>
        Mutate(async () =>
        {
            var actorId = Actor(actor);
            var resource = await Resource(resourceId);
            if (!ResourcePermissions.CanManage(actor, resource.OwnerId))
                throw new WorkflowException("Du kan bare slette egne ressurser.");
            Version(resource.ConcurrencyToken, expectedToken);
            if (Busy(resource) || await db.ResourceRequests.AnyAsync(r => r.ResourceId == resourceId))
                throw new WorkflowException("Ressurser med forespørselshistorikk kan ikke slettes. Sett ressursen utilgjengelig.");
            db.MapSubmissions.Remove(resource);
            Audit(actorId, "ResourceDeleted", reason, resourceId);
        });

    public async Task<int> CreateNeedAsync(string description, string place, NeedPriority priority,
        ClaimsPrincipal actor)
    {
        var id = 0;
        await Mutate(async () =>
        {
            var actorId = Admin(actor);
            if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 2000 ||
                string.IsNullOrWhiteSpace(place) || place.Trim().Length > 200 || !Enum.IsDefined(priority))
                throw new WorkflowException("Oppgi beskrivelse, sted og gyldig prioritet.");
            var need = new OperationalNeed
            {
                Description = description.Trim(), Place = place.Trim(), Priority = priority,
                CreatedAtUtc = DateTime.UtcNow, CreatedById = actorId
            };
            db.OperationalNeeds.Add(need);
            await db.SaveChangesAsync();
            id = need.Id;
            Audit(actorId, "NeedCreated", need.Description[..Math.Min(1000, need.Description.Length)], needId: id);
        });
        return id;
    }

    public Task ChangeNeedStatusAsync(int needId, NeedStatus status, string expectedToken,
        ClaimsPrincipal actor, string reason) => Mutate(async () =>
        {
            var actorId = Admin(actor);
            var need = await Need(needId);
            Version(need.ConcurrencyToken, expectedToken);
            Open(need);
            if (!Enum.IsDefined(status) || status == need.Status || status == NeedStatus.Open)
                throw new WorkflowException("Ugyldig statusovergang.");
            if (status is NeedStatus.Completed or NeedStatus.Cancelled &&
                await db.ResourceRequests.AnyAsync(r => r.NeedId == needId &&
                    (r.Status == ResourceRequestStatus.Pending || r.Status == ResourceRequestStatus.Accepted ||
                     r.Status == ResourceRequestStatus.Allocated)))
                throw new WorkflowException("Avslutt forespørsler og returner tildelte ressurser først.");
            need.Status = status;
            Audit(actorId, $"Need{status}", reason, needId: needId);
        });

    public async Task<int> CreateRequestAsync(int needId, int resourceId, string expectedResourceToken,
        string expectedNeedToken, ClaimsPrincipal actor, string reason)
    {
        var id = 0;
        await Mutate(async () =>
        {
            var actorId = Admin(actor);
            var resource = await Resource(resourceId);
            var need = await Need(needId);
            Version(resource.ConcurrencyToken, expectedResourceToken);
            Version(need.ConcurrencyToken, expectedNeedToken);
            Open(need);
            if (string.IsNullOrEmpty(resource.OwnerId) || !WorkflowEligibility.IsEligible(resource, DateTime.UtcNow))
                throw new WorkflowException("Ressursen må ha eier, være tilgjengelig og ha gyldig godkjenning.");
            if (await db.ResourceRequests.AnyAsync(r => r.NeedId == needId && r.ResourceId == resourceId &&
                    (r.Status == ResourceRequestStatus.Pending || r.Status == ResourceRequestStatus.Accepted ||
                     r.Status == ResourceRequestStatus.Allocated)))
                throw new WorkflowException("Det finnes allerede en aktiv forespørsel for dette behovet og ressursen.");
            var request = new ResourceRequest
            {
                NeedId = needId, ResourceId = resourceId, CreatedAtUtc = DateTime.UtcNow,
                CreatedById = actorId, Reason = Reason(reason)
            };
            Touch(resource);
            Touch(need);
            db.ResourceRequests.Add(request);
            await db.SaveChangesAsync();
            id = request.Id;
            Audit(actorId, "RequestCreated", reason, resourceId, needId, id);
        });
        return id;
    }

    public Task RespondAsync(int requestId, bool accept, string expectedToken, ClaimsPrincipal actor, string reason) =>
        ChangeRequest(requestId, expectedToken, actor, reason, accept ? "Accept" : "Reject");

    public Task AllocateAsync(int requestId, string expectedToken, ClaimsPrincipal actor, string reason) =>
        ChangeRequest(requestId, expectedToken, actor, reason, "Allocate");

    public Task CancelAsync(int requestId, string expectedToken, ClaimsPrincipal actor, string reason) =>
        ChangeRequest(requestId, expectedToken, actor, reason, "Cancel");

    public Task ReturnAsync(int requestId, string expectedToken, ClaimsPrincipal actor, string reason) =>
        ChangeRequest(requestId, expectedToken, actor, reason, "Return");

    private Task ChangeRequest(int requestId, string expectedToken, ClaimsPrincipal actor, string reason, string action) =>
        Mutate(async () =>
        {
            var actorId = Actor(actor);
            var request = await db.ResourceRequests.Include(r => r.Resource).Include(r => r.Need)
                .SingleOrDefaultAsync(r => r.Id == requestId) ?? throw new WorkflowException("Forespørselen finnes ikke.");
            Version(request.ConcurrencyToken, expectedToken);
            var resource = request.Resource;
            if (action is "Accept" or "Reject")
            {
                if (resource.OwnerId != actorId)
                    throw new WorkflowException("Bare ressursens eier kan svare på forespørselen.");
                if (request.Status != ResourceRequestStatus.Pending)
                    throw new WorkflowException("Forespørselen er allerede besvart.");
                if (action == "Accept")
                {
                    Open(request.Need);
                    if (!WorkflowEligibility.IsEligible(resource, DateTime.UtcNow))
                        throw new WorkflowException("Ressursen er ikke lenger tilgjengelig med gyldig godkjenning.");
                    resource.Availability = ResourceAvailability.Reserved;
                    request.Status = ResourceRequestStatus.Accepted;
                }
                else request.Status = ResourceRequestStatus.Rejected;
            }
            else if (action == "Allocate")
            {
                Admin(actor);
                Open(request.Need);
                if (request.Status != ResourceRequestStatus.Accepted || resource.Availability != ResourceAvailability.Reserved ||
                    !WorkflowEligibility.HasCurrentApproval(resource, DateTime.UtcNow))
                    throw new WorkflowException("Tildeling krever eiers aksept, reservasjon og gyldig godkjenning.");
                resource.Availability = ResourceAvailability.Allocated;
                request.Status = ResourceRequestStatus.Allocated;
                request.Need.Status = NeedStatus.InProgress;
            }
            else if (action == "Cancel")
            {
                Admin(actor);
                if (request.Status is not (ResourceRequestStatus.Pending or ResourceRequestStatus.Accepted))
                    throw new WorkflowException("Kun ventende eller aksepterte forespørsler kan avbrytes.");
                if (request.Status == ResourceRequestStatus.Accepted)
                {
                    if (resource.Availability != ResourceAvailability.Reserved)
                        throw new WorkflowException("Reservasjonen er endret.");
                    resource.Availability = ResourceAvailability.Available;
                }
                request.Status = ResourceRequestStatus.Cancelled;
            }
            else if (action == "Return")
            {
                if (resource.OwnerId != actorId) Admin(actor);
                if (request.Status != ResourceRequestStatus.Allocated || resource.Availability != ResourceAvailability.Allocated)
                    throw new WorkflowException("Bare en tildelt ressurs kan returneres.");
                request.Status = ResourceRequestStatus.Returned;
                resource.Availability = ResourceAvailability.Available;
                Invalidate(resource);
            }
            else throw new WorkflowException("Ugyldig handling.");
            Touch(resource);
            Touch(request.Need);
            Audit(actorId, $"Request{action}", reason, resource.Id, request.NeedId, request.Id);
        });

    private static void Invalidate(MapSubmission resource)
    {
        resource.VerificationStatus = ResourceVerificationStatus.Pending;
        resource.VerifiedAtUtc = null;
        resource.VerificationExpiresAtUtc = null;
    }
}
