using System.Text.Json.Serialization;

namespace HeimevernetInnlevering1.Models;

/// <summary>
/// Statusen til en ressurs slik kartet og listene viser den. JSON-navnene brukes av
/// wwwroot/js/resource-icons.js (grønt ikon når availability er «Tilgjengelig»).
/// Krever at Ressurstype og Verifiseringer er lastet.
/// </summary>
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

    public static ResourceOperationalStatus From(Ressurs ressurs, DateTime nowUtc, int dager = WorkflowEligibility.StandardGyldighetDager)
    {
        var eligible = WorkflowEligibility.IsEligible(ressurs, nowUtc, dager);
        var godkjent = WorkflowEligibility.SisteGodkjenning(ressurs);
        var gyldigTil = godkjent?.AddDays(dager);
        var availability = Statuser.Visning(ressurs.Status);
        var verification = ressurs.Status switch
        {
            Statuser.Ressurs.Avvist => "Avvist",
            Statuser.Ressurs.VenterPåVerifisering or Statuser.Ressurs.Returnert => "Venter på verifisering",
            _ when godkjent is null => "Venter på verifisering",
            _ when gyldigTil <= nowUtc => "Godkjenning utløpt",
            _ => "Godkjent"
        };
        var statusText = eligible
            ? "Kan forespørres nå · Tilgjengelig · Godkjent"
            : $"Kan ikke forespørres nå · {availability} · {verification}";
        return new(eligible, availability, verification, AsUtc(godkjent), AsUtc(gyldigTil), statusText)
        { Lifecycle = availability };
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
