using System.Text.Json.Serialization;
using HeimevernetInnlevering1.Controllers;

namespace HeimevernetInnlevering1.Models;

public sealed record ResourceOperationalStatus(
    [property: JsonPropertyName("isEligible")] bool IsEligible,
    [property: JsonPropertyName("availability")] string Availability,
    [property: JsonPropertyName("verification")] string Verification,
    [property: JsonPropertyName("verifiedAtUtc")] DateTime? VerifiedAtUtc,
    [property: JsonPropertyName("verificationExpiresAtUtc")] DateTime? VerificationExpiresAtUtc,
    [property: JsonPropertyName("statusText")] string StatusText)
{
    [JsonPropertyName("lifecycle")]
    public string Lifecycle { get; init; } = string.Empty;

    public static ResourceOperationalStatus From(MapSubmission resource, DateTime nowUtc)
    {
        var knownType = MapSubmissionViewModel.ResourceTypes.Contains(resource.ResourceType);
        var hasOwner = !string.IsNullOrWhiteSpace(resource.OwnerId);
        var eligible = knownType && hasOwner && WorkflowEligibility.IsEligible(resource, nowUtc);
        var availability = resource.Availability switch
        {
            ResourceAvailability.Available => "Tilgjengelig",
            ResourceAvailability.Unavailable => "Utilgjengelig",
            ResourceAvailability.Reserved => "Reservert",
            ResourceAvailability.Allocated => "Tildelt",
            ResourceAvailability.InUse => "I bruk",
            ResourceAvailability.Returned => "Returnert",
            _ => "Ukjent tilgjengelighet"
        };
        var verification = resource.VerificationStatus switch
        {
            ResourceVerificationStatus.Pending => "Venter på verifisering",
            ResourceVerificationStatus.Rejected => "Avvist",
            ResourceVerificationStatus.Approved when WorkflowEligibility.HasCurrentApproval(resource, nowUtc) => "Godkjent",
            ResourceVerificationStatus.Approved when !resource.VerifiedAtUtc.HasValue ||
                !resource.VerificationExpiresAtUtc.HasValue => "Godkjenning mangler gyldighetsdata",
            ResourceVerificationStatus.Approved when resource.VerifiedAtUtc > nowUtc => "Godkjenning ikke gyldig ennå",
            ResourceVerificationStatus.Approved => "Godkjenning utløpt",
            _ => "Ukjent verifiseringsstatus"
        };
        var statusText = eligible ? "Kan forespørres nå · Tilgjengelig · Godkjent" :
            $"Kan ikke forespørres nå · {availability} · {verification}" +
            (!hasOwner ? " · Mangler eier" : "") + (!knownType ? " · Ukjent ressurstype" : "");
        return new(eligible, availability, verification, AsUtc(resource.VerifiedAtUtc),
            AsUtc(resource.VerificationExpiresAtUtc), statusText)
        { Lifecycle = WorkflowTransitions.Lifecycle(resource, nowUtc) };
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
