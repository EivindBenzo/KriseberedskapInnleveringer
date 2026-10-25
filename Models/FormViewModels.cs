using System.ComponentModel.DataAnnotations;

namespace HeimevernetInnlevering1.Controllers;

public class FormSubmissionViewModel
{
    [Required(ErrorMessage = "Fornavn er påkrevd")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Fornavn må være mellom 2 og 100 tegn")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Etternavn er påkrevd")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Etternavn må være mellom 2 og 100 tegn")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-post er påkrevd")]
    [EmailAddress(ErrorMessage = "E-postadressen er ikke gyldig")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Beskrivelse er påkrevd")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Beskrivelsen må være mellom 10 og 1000 tegn")]
    public string Message { get; set; } = string.Empty;

    [StringLength(200)]
    public string? LocationName { get; set; }
    [Range(-90, 90)]
    public double? Latitude { get; set; }
    [Range(-180, 180)]
    public double? Longitude { get; set; }
}

public class MapSubmissionViewModel
{
    // Resource types taken from the Marius branch.
    public static readonly string[] ResourceTypes =
    {
        "Kjøretøy", "Maskin", "Drone", "Generator", "Personell", "Utstyr"
    };

    [Required(ErrorMessage = "Ressursnavn er påkrevd")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ressursnavn må være mellom 2 og 100 tegn")]
    public string ResourceName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Velg en ressurstype")]
    public string ResourceType { get; set; } = string.Empty;

    public string LocationName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Breddegrad er påkrevd")]
    [Range(-90, 90, ErrorMessage = "Breddegrad må være mellom -90 og 90")]
    public double? Latitude { get; set; }

    [Required(ErrorMessage = "Lengdegrad er påkrevd")]
    [Range(-180, 180, ErrorMessage = "Lengdegrad må være mellom -180 og 180")]
    public double? Longitude { get; set; }
}
