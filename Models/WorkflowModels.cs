using System.ComponentModel.DataAnnotations;

namespace HeimevernetInnlevering1.Models;

public enum ResourceAvailability { Available, Unavailable, Reserved, Allocated, InUse, Returned }
public enum ResourceVerificationStatus { Pending, Approved, Rejected }
public enum NeedPriority { Low, Normal, High, Critical }
public enum NeedStatus { Open, InProgress, Completed, Cancelled, Assigned }
public enum ResourceRequestStatus { Pending, Accepted, Allocated, Rejected, Cancelled, Returned }
public enum AssignmentStatus { Confirmed, EnRoute, Arrived, Completed }

public class OperationalNeed
{
    public int Id { get; set; }
    [Required, MaxLength(2000)]
    public string Description { get; set; } = string.Empty;
    [Required, MaxLength(200)]
    public string Place { get; set; } = string.Empty;
    public NeedPriority Priority { get; set; } = NeedPriority.Normal;
    public NeedStatus Status { get; set; } = NeedStatus.Open;
    public DateTime CreatedAtUtc { get; set; }
    [Required, MaxLength(127)]
    public string CreatedById { get; set; } = string.Empty;
    [ConcurrencyCheck, MaxLength(36)]
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
}

public class ResourceRequest
{
    public int Id { get; set; }
    public int NeedId { get; set; }
    public OperationalNeed Need { get; set; } = null!;
    public int ResourceId { get; set; }
    public MapSubmission Resource { get; set; } = null!;
    public ResourceRequestStatus Status { get; set; } = ResourceRequestStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }
    [Required, MaxLength(127)]
    public string CreatedById { get; set; } = string.Empty;
    [Required, MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
    [ConcurrencyCheck, MaxLength(36)]
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
}

public class ResourceAssignment
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public ResourceRequest Request { get; set; } = null!;
    public int ResourceId { get; set; }
    public MapSubmission Resource { get; set; } = null!;
    public int NeedId { get; set; }
    public OperationalNeed Need { get; set; } = null!;
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Confirmed;
    /// <summary>Equals ResourceId while the assignment is active; a unique index keeps a resource from being assigned twice.</summary>
    public int? ActiveResourceId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    [Required, MaxLength(127)]
    public string CreatedById { get; set; } = string.Empty;
    [ConcurrencyCheck, MaxLength(36)]
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
}

public class WorkflowAudit
{
    public long Id { get; set; }
    public int? ResourceId { get; set; }
    public int? NeedId { get; set; }
    public int? RequestId { get; set; }
    [Required, MaxLength(127)]
    public string ActorId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    [Required, MaxLength(60)]
    public string Action { get; set; } = string.Empty;
    [Required, MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public class WorkflowOptions
{
    public int VerificationValidityDays { get; set; } = 30;
}

public static class WorkflowEligibility
{
    public static bool HasCurrentApproval(MapSubmission resource, DateTime nowUtc) =>
        resource.VerificationStatus == ResourceVerificationStatus.Approved &&
        resource.VerifiedAtUtc.HasValue && resource.VerifiedAtUtc <= nowUtc &&
        resource.VerificationExpiresAtUtc > nowUtc;

    public static bool IsEligible(MapSubmission resource, DateTime nowUtc) =>
        resource.Availability == ResourceAvailability.Available && HasCurrentApproval(resource, nowUtc);
}

public static class WorkflowTransitions
{
    public static bool CanMoveNeed(NeedStatus from, NeedStatus to) => (from, to) switch
    {
        (NeedStatus.Open, NeedStatus.InProgress) => true,
        (NeedStatus.Assigned, NeedStatus.Completed) => true,
        (NeedStatus.Open or NeedStatus.InProgress or NeedStatus.Assigned, NeedStatus.Cancelled) => true,
        _ => false
    };

    public static bool IsActive(NeedStatus status) =>
        status is NeedStatus.Open or NeedStatus.InProgress or NeedStatus.Assigned;

    public static bool CanMoveAssignment(AssignmentStatus from, AssignmentStatus to) =>
        from != AssignmentStatus.Completed && (int)to == (int)from + 1;

    public static string Label(NeedStatus status) => status switch
    {
        NeedStatus.Open => "Ny", NeedStatus.InProgress => "Vurderes", NeedStatus.Assigned => "Tildelt",
        NeedStatus.Completed => "Løst", _ => "Avbrutt"
    };

    public static string Label(AssignmentStatus status) => status switch
    {
        AssignmentStatus.Confirmed => "Bekreftet", AssignmentStatus.EnRoute => "På vei",
        AssignmentStatus.Arrived => "Fremme", _ => "Fullført"
    };

    public static string Label(ResourceRequestStatus status) => status switch
    {
        ResourceRequestStatus.Pending => "Venter", ResourceRequestStatus.Accepted => "Godtatt",
        ResourceRequestStatus.Allocated => "Tildelt", ResourceRequestStatus.Rejected => "Avslått",
        ResourceRequestStatus.Cancelled => "Avbrutt", _ => "Returnert"
    };

    public static string Label(NeedPriority priority) => priority switch
    {
        NeedPriority.Low => "Lav", NeedPriority.Normal => "Normal", NeedPriority.High => "Høy", _ => "Kritisk"
    };

    public static string Lifecycle(MapSubmission resource, DateTime nowUtc) => resource.VerificationStatus switch
    {
        ResourceVerificationStatus.Rejected => "Avvist",
        _ when resource.Availability == ResourceAvailability.Reserved => "Forespurt",
        _ when resource.Availability == ResourceAvailability.Allocated => "Tildelt",
        _ when resource.Availability == ResourceAvailability.InUse => "I bruk",
        _ when !WorkflowEligibility.HasCurrentApproval(resource, nowUtc) =>
            resource.Availability == ResourceAvailability.Returned ? "Returnert" : "Venter på verifisering",
        _ when resource.Availability == ResourceAvailability.Unavailable => "Utilgjengelig",
        _ => "Tilgjengelig"
    };
}

public class OperationsViewModel
{
    public IReadOnlyList<MapSubmission> Resources { get; set; } = [];
    public IReadOnlyList<OperationalNeed> Needs { get; set; } = [];
    public IReadOnlyList<ResourceRequest> Requests { get; set; } = [];
    public IReadOnlyList<ResourceAssignment> Assignments { get; set; } = [];
    public IReadOnlyList<WorkflowAudit> Audit { get; set; } = [];
}
