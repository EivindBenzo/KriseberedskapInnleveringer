using System.ComponentModel.DataAnnotations;

namespace HeimevernetInnlevering1.Models;

public enum ResourceAvailability { Available, Unavailable, Reserved, Allocated }
public enum ResourceVerificationStatus { Pending, Approved, Rejected }
public enum NeedPriority { Low, Normal, High, Critical }
public enum NeedStatus { Open, InProgress, Completed, Cancelled }
public enum ResourceRequestStatus { Pending, Accepted, Allocated, Rejected, Cancelled, Returned }

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

public class OperationsViewModel
{
    public IReadOnlyList<MapSubmission> Resources { get; set; } = [];
    public IReadOnlyList<OperationalNeed> Needs { get; set; } = [];
    public IReadOnlyList<ResourceRequest> Requests { get; set; } = [];
    public IReadOnlyList<WorkflowAudit> Audit { get; set; } = [];
}
