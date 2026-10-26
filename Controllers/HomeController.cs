using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPlaceGeocoder _geocoder;

    public HomeController(AppDbContext db, IPlaceGeocoder geocoder)
    {
        _db = db;
        _geocoder = geocoder;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View();
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult Form() => View(new FormSubmissionViewModel
    {
        Email = User.Identity?.Name ?? string.Empty
    });

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(FormSubmissionViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var submission = new FormSubmission
        {
            FirstName = model.FirstName,
            LastName = model.LastName,
            Email = model.Email,
            Description = model.Message,
            LocationName = model.LocationName,
            Latitude = model.Latitude,
            Longitude = model.Longitude,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };

        _db.FormSubmissions.Add(submission);
        await _db.SaveChangesAsync();

        TempData["MapSuccess"] = "Behovet er meldt inn.";
        return RedirectToAction(nameof(Verify));
    }

    [HttpGet]
    public IActionResult Result(string name, string email, string message)
    {
        ViewBag.Name = name;
        ViewBag.Email = email;
        ViewBag.Message = message;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Map()
    {
        await LoadResourcesAsync();
        return View(new MapSubmissionViewModel());
    }

    [HttpGet]
    public async Task<IActionResult> Place(double? latitude, double? longitude)
    {
        if (!ModelState.IsValid || !PlaceGeocoder.ValidPoint(latitude, longitude))
            return BadRequest();
        return Json(new { label = await _geocoder.ReverseAsync(latitude!.Value, longitude!.Value,
            HttpContext?.RequestAborted ?? default) });
    }

    [HttpGet]
    public async Task<IActionResult> SearchPlaces(string? query)
    {
        if (!ModelState.IsValid || !PlaceGeocoder.ValidQuery(query)) return BadRequest();
        return Json(await _geocoder.SearchAsync(query!, HttpContext?.RequestAborted ?? default));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Map(MapSubmissionViewModel model)
    {
        await ValidateResourceAsync(model);

        if (!ModelState.IsValid)
        {
            await LoadResourcesAsync();
            return View(model);
        }

        _db.MapSubmissions.Add(new MapSubmission
        {
            ResourceName = model.ResourceName,
            ResourceType = model.ResourceType,
            LocationName = model.LocationName,
            Latitude = model.Latitude!.Value,
            Longitude = model.Longitude!.Value,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Availability = model.Availability,
            Capability = model.Capability?.Trim(),
            TransportRequirements = model.TransportRequirements?.Trim(),
            OwnerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        });
        _db.WorkflowAudits.Add(new WorkflowAudit
        {
            ActorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            Action = "ResourceRegistered",
            Reason = $"{model.ResourceType}: {model.ResourceName}",
            OccurredAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["MapSuccess"] = $"{model.ResourceType} «{model.ResourceName}» ble registrert ved {model.LocationName}.";

        // Back to the map so the new marker is shown together with the others.
        return RedirectToAction(nameof(Map));
    }

    [HttpGet]
    public IActionResult MapResult(string locationName, double latitude, double longitude)
    {
        ViewBag.LocationName = locationName;
        ViewBag.Latitude = latitude;
        ViewBag.Longitude = longitude;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var resource = await _db.MapSubmissions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.OwnerId)) return Forbid();
        ViewBag.EditId = id;
        await LoadResourcesAsync();
        return View("Map", new MapSubmissionViewModel
        {
            ResourceName = resource.ResourceName,
            ResourceType = resource.ResourceType,
            LocationName = resource.LocationName,
            Latitude = resource.Latitude,
            Longitude = resource.Longitude,
            Availability = resource.Availability,
            Capability = resource.Capability,
            TransportRequirements = resource.TransportRequirements,
            Version = resource.ConcurrencyToken
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MapSubmissionViewModel model)
    {
        var resource = await _db.MapSubmissions.FindAsync(id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.OwnerId)) return Forbid();
        await ValidateResourceAsync(model);
        if (resource.Availability is ResourceAvailability.Reserved or ResourceAvailability.Allocated)
            ModelState.AddModelError("", "Ressursen er reservert eller tildelt. Avslutt forespørselen før du redigerer.");
        if (string.IsNullOrEmpty(model.Version) || model.Version != resource.ConcurrencyToken)
        {
            ModelState.AddModelError("", "Ressursen er endret av en annen bruker. Dine felt er beholdt. Kontroller gjeldende status før du lagrer på nytt.");
            model.Version = resource.ConcurrencyToken;
            ModelState.Remove(nameof(model.Version));
        }
        if (!ModelState.IsValid)
        {
            ViewBag.EditId = id;
            await LoadResourcesAsync();
            return View("Map", model);
        }
        var materialChange = resource.ResourceName != model.ResourceName ||
            resource.ResourceType != model.ResourceType || resource.LocationName != model.LocationName ||
            resource.Latitude != model.Latitude || resource.Longitude != model.Longitude ||
            resource.Capability != model.Capability?.Trim() ||
            resource.TransportRequirements != model.TransportRequirements?.Trim();
        resource.ResourceName = model.ResourceName;
        resource.ResourceType = model.ResourceType;
        resource.LocationName = model.LocationName;
        resource.Latitude = model.Latitude!.Value;
        resource.Longitude = model.Longitude!.Value;
        resource.Capability = model.Capability?.Trim();
        resource.TransportRequirements = model.TransportRequirements?.Trim();
        resource.Availability = model.Availability;
        if (materialChange)
        {
            resource.VerificationStatus = ResourceVerificationStatus.Pending;
            resource.VerifiedAtUtc = null;
            resource.VerificationExpiresAtUtc = null;
        }
        resource.UpdatedAtUtc = DateTime.UtcNow;
        _db.WorkflowAudits.Add(new WorkflowAudit
        {
            ActorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            Action = materialChange ? "ResourceEditedReverificationRequired" : "AvailabilityChanged",
            Reason = "Eier eller administrator oppdaterte ressursopplysningene.",
            ResourceId = resource.Id,
            OccurredAtUtc = DateTime.UtcNow
        });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            var current = await _db.MapSubmissions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
            model.Version = current?.ConcurrencyToken;
            ModelState.Remove(nameof(model.Version));
            ModelState.AddModelError("", "Endringen ble ikke lagret: ressursen ble endret samtidig. Dine felt er beholdt; kontroller gjeldende status før du prøver igjen.");
            ViewBag.EditId = id;
            await LoadResourcesAsync();
            return View("Map", model);
        }
        TempData["MapSuccess"] = materialChange
            ? "Ressursen er oppdatert og venter på ny verifisering."
            : "Ressursens tilgjengelighet er oppdatert.";
        return RedirectToAction(nameof(Map));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var resource = await _db.MapSubmissions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.OwnerId)) return Forbid();
        return View(resource);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, string? version = null)
    {
        var resource = await _db.MapSubmissions.FindAsync(id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.OwnerId)) return Forbid();
        try
        {
            var workflow = new WorkflowService(_db,
                Microsoft.Extensions.Options.Options.Create(new WorkflowOptions()));
            await workflow.DeleteResourceAsync(id, version ?? "", User, "Bekreftet sletting fra ressursoversikten.");
        }
        catch (WorkflowException exception)
        {
            ModelState.AddModelError("", exception.Message);
            var current = await _db.MapSubmissions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
            return current is null ? NotFound() : View("Delete", current);
        }
        TempData["MapSuccess"] = "Ressursen er slettet.";
        return RedirectToAction(nameof(Map));
    }

    [HttpGet]
    public IActionResult Data(string? type) => RedirectToAction(nameof(Resources), new { type });

    [HttpGet]
    public IActionResult Verify(string? type) => RedirectToAction(nameof(Resources), new { type });

    [HttpGet]
    public Task<IActionResult> Mine(string? type, string? query, bool eligible = false) =>
        Resources(type, query, true, eligible);

    [HttpGet]
    public async Task<IActionResult> Resources(string? type, string? query, bool mine = false, bool eligible = false)
    {
        ViewBag.SelectedType = type;
        ViewBag.Query = query;
        ViewBag.Mine = mine;
        ViewBag.Eligible = eligible;
        return View("Verify", await BuildDataModelAsync(type, query, mine, eligible));
    }

    protected virtual async Task LoadResourcesAsync()
    {
        ViewBag.Resources = await _db.MapSubmissions
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    private async Task ValidateResourceAsync(MapSubmissionViewModel model)
    {
        ModelState.Remove(nameof(model.LocationName));
        model.LocationName = string.Empty;
        if (!MapSubmissionViewModel.ResourceTypes.Contains(model.ResourceType))
            ModelState.AddModelError(nameof(model.ResourceType), "Velg en gyldig ressurstype");
        if (model.Availability is not (ResourceAvailability.Available or ResourceAvailability.Unavailable))
            ModelState.AddModelError(nameof(model.Availability), "Velg tilgjengelig eller utilgjengelig. Reservasjon og tildeling håndteres gjennom forespørsler.");
        if (model.Capability?.Length > 500 || model.TransportRequirements?.Length > 500)
            ModelState.AddModelError("", "Kapasitet og transportbehov kan ha høyst 500 tegn hver.");
        if (string.IsNullOrWhiteSpace(model.ResourceName) || model.ResourceName.Length is < 2 or > 100)
            ModelState.AddModelError(nameof(model.ResourceName), "Ressursnavn må være mellom 2 og 100 tegn.");
        if (model.Latitude is not double lat || !double.IsFinite(lat) || lat is < -90 or > 90)
            ModelState.AddModelError(nameof(model.Latitude), "Velg en gyldig breddegrad.");
        if (model.Longitude is not double lng || !double.IsFinite(lng) || lng is < -180 or > 180)
            ModelState.AddModelError(nameof(model.Longitude), "Velg en gyldig lengdegrad.");
        if (PlaceGeocoder.ValidPoint(model.Latitude, model.Longitude))
            model.LocationName = await _geocoder.ReverseAsync(model.Latitude!.Value, model.Longitude!.Value,
                HttpContext?.RequestAborted ?? default);
    }

    private async Task<DataPageViewModel> BuildDataModelAsync(string? type, string? query, bool mine, bool eligible)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reports = _db.FormSubmissions.AsNoTracking()
            .Where(s => User.IsInRole("Admin") || s.OwnerId == ownerId);
        var resources = _db.MapSubmissions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(type))
            resources = resources.Where(r => r.ResourceType == type);
        if (mine) resources = resources.Where(r => r.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(query))
            resources = resources.Where(r => r.ResourceName.Contains(query) || r.LocationName.Contains(query));
        if (eligible)
        {
            var now = DateTime.UtcNow;
            resources = resources.Where(r => r.Availability == ResourceAvailability.Available &&
                r.VerificationStatus == ResourceVerificationStatus.Approved &&
                r.VerifiedAtUtc <= now && r.VerificationExpiresAtUtc > now &&
                r.OwnerId != null && r.OwnerId.Trim() != "" &&
                MapSubmissionViewModel.ResourceTypes.Contains(r.ResourceType));
        }
        return new DataPageViewModel
        {
            FormSubmissions = await reports.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(),
            MapSubmissions = await resources.OrderByDescending(x => x.CreatedAtUtc).ToListAsync()
        };
    }
}
