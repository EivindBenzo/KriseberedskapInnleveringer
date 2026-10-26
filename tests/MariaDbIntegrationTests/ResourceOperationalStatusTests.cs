using System.Text.Json;
using HeimevernetInnlevering1.Models;
using Xunit;

namespace MariaDbIntegrationTests;

public class ResourceOperationalStatusTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static MapSubmission Available() => new()
    {
        Id = 1, ResourceType = "Drone", OwnerId = "owner",
        Availability = ResourceAvailability.Available,
        VerificationStatus = ResourceVerificationStatus.Approved,
        VerifiedAtUtc = Now.AddDays(-1), VerificationExpiresAtUtc = Now.AddDays(1)
    };

    [Fact]
    public void OnlyOwnedKnownAvailableAndCurrentlyApprovedResourcesAreEligible()
    {
        Assert.True(ResourceOperationalStatus.From(Available(), Now).IsEligible);
        foreach (var availability in new[] { ResourceAvailability.Unavailable, ResourceAvailability.Reserved,
            ResourceAvailability.Allocated, (ResourceAvailability)99 })
        {
            var resource = Available();
            resource.Availability = availability;
            var status = ResourceOperationalStatus.From(resource, Now);
            Assert.False(status.IsEligible);
            Assert.Contains("Kan ikke forespørres nå", status.StatusText);
        }
        foreach (var type in new[] { "", "Ukjent", "<script>" })
        {
            var resource = Available();
            resource.ResourceType = type;
            Assert.False(ResourceOperationalStatus.From(resource, Now).IsEligible);
            Assert.Contains("Ukjent ressurstype", ResourceOperationalStatus.From(resource, Now).StatusText);
        }
        foreach (var owner in new string?[] { null, "", " " })
        {
            var resource = Available();
            resource.OwnerId = owner;
            Assert.False(ResourceOperationalStatus.From(resource, Now).IsEligible);
            Assert.Contains("Mangler eier", ResourceOperationalStatus.From(resource, Now).StatusText);
        }
    }

    [Theory]
    [InlineData(ResourceVerificationStatus.Pending, "Venter på verifisering")]
    [InlineData(ResourceVerificationStatus.Rejected, "Avvist")]
    [InlineData((ResourceVerificationStatus)99, "Ukjent verifiseringsstatus")]
    public void UnavailableReasonsAreDistinct(ResourceVerificationStatus verification, string reason)
    {
        var resource = Available();
        resource.VerificationStatus = verification;
        var status = ResourceOperationalStatus.From(resource, Now);
        Assert.False(status.IsEligible);
        Assert.Contains(reason, status.StatusText);
    }

    [Theory]
    [InlineData(ResourceAvailability.Unavailable, "Utilgjengelig")]
    [InlineData(ResourceAvailability.Reserved, "Reservert")]
    [InlineData(ResourceAvailability.Allocated, "Tildelt")]
    [InlineData((ResourceAvailability)99, "Ukjent tilgjengelighet")]
    public void AvailabilityReasonsRemainVisible(ResourceAvailability availability, string reason)
    {
        var resource = Available();
        resource.Availability = availability;
        var status = ResourceOperationalStatus.From(resource, Now);
        Assert.False(status.IsEligible);
        Assert.Equal(reason, status.Availability);
        Assert.Contains(reason, status.StatusText);
    }

    [Fact]
    public void ApprovalExpiryIsExclusiveAndMissingOrFutureDatesAreNotGreen()
    {
        var resource = Available();
        resource.VerificationExpiresAtUtc = Now;
        Assert.False(ResourceOperationalStatus.From(resource, Now).IsEligible);
        Assert.Contains("Godkjenning utløpt", ResourceOperationalStatus.From(resource, Now).StatusText);
        resource.VerificationExpiresAtUtc = Now.AddDays(1);
        resource.VerifiedAtUtc = Now.AddSeconds(1);
        Assert.False(ResourceOperationalStatus.From(resource, Now).IsEligible);
        Assert.Contains("ikke gyldig ennå", ResourceOperationalStatus.From(resource, Now).StatusText);
        resource.VerifiedAtUtc = null;
        Assert.Contains("mangler gyldighetsdata", ResourceOperationalStatus.From(resource, Now).StatusText);
        resource.VerifiedAtUtc = Now.AddDays(-1);
        resource.VerificationExpiresAtUtc = null;
        Assert.False(ResourceOperationalStatus.From(resource, Now).IsEligible);
    }

    [Fact]
    public void SerializedStatusHasExplicitContractAndUtcDatesEvenAfterDatabaseMaterialization()
    {
        var resource = Available();
        resource.VerifiedAtUtc = DateTime.SpecifyKind(resource.VerifiedAtUtc!.Value, DateTimeKind.Unspecified);
        resource.VerificationExpiresAtUtc = DateTime.SpecifyKind(resource.VerificationExpiresAtUtc!.Value, DateTimeKind.Unspecified);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ResourceOperationalStatus.From(resource, Now)));
        Assert.True(json.RootElement.GetProperty("isEligible").GetBoolean());
        Assert.Equal("Tilgjengelig", json.RootElement.GetProperty("availability").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("verifiedAtUtc").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("verificationExpiresAtUtc").GetString());
        Assert.Contains("Kan forespørres nå", json.RootElement.GetProperty("statusText").GetString());
    }
}
