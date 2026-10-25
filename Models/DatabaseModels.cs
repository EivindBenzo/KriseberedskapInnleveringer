using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace HeimevernetInnlevering1.Models;

public class FormSubmission
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? OwnerId { get; set; }
    public IdentityUser? Owner { get; set; }
}

/// <summary>
/// A resource placed on the map (based on Marius' Ressurs entity).
/// </summary>
public class MapSubmission
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string ResourceName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ResourceType { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string LocationName { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? OwnerId { get; set; }
    public IdentityUser? Owner { get; set; }

    public ResourceAvailability Availability { get; set; } = ResourceAvailability.Available;
    public ResourceVerificationStatus VerificationStatus { get; set; } = ResourceVerificationStatus.Pending;
    [MaxLength(1000)]
    public string? Capability { get; set; }
    [MaxLength(1000)]
    public string? TransportRequirements { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? VerificationExpiresAtUtc { get; set; }

    [ConcurrencyCheck, MaxLength(36)]
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
}
