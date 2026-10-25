using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Form() => View(new FormSubmissionViewModel
    {
        Email = User.Identity?.Name ?? string.Empty
    });

    [HttpPost]
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
        return RedirectToAction(nameof(Data));
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
        return View(new MapSubmissionViewModel
        {
            LocationName = "Kristiansand",
            Latitude = 58.1467,
            Longitude = 7.9956
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Map(MapSubmissionViewModel model)
    {
        ValidateResource(model);

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
            Latitude = model.Latitude,
            Longitude = model.Longitude,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
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
            Longitude = resource.Longitude
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MapSubmissionViewModel model)
    {
        var resource = await _db.MapSubmissions.FindAsync(id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.OwnerId)) return Forbid();
        ValidateResource(model);
        if (!ModelState.IsValid)
        {
            ViewBag.EditId = id;
            await LoadResourcesAsync();
            return View("Map", model);
        }
        resource.ResourceName = model.ResourceName;
        resource.ResourceType = model.ResourceType;
        resource.LocationName = model.LocationName;
        resource.Latitude = model.Latitude;
        resource.Longitude = model.Longitude;
        resource.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["MapSuccess"] = "Ressursen er oppdatert.";
        return RedirectToAction(nameof(Data));
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
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var resource = await _db.MapSubmissions.FindAsync(id);
        if (resource is null) return NotFound();
        if (!ResourcePermissions.CanManage(User, resource.OwnerId)) return Forbid();
        _db.MapSubmissions.Remove(resource);
        await _db.SaveChangesAsync();
        TempData["MapSuccess"] = "Ressursen er slettet.";
        return RedirectToAction(nameof(Data));
    }

    [HttpGet]
    public async Task<IActionResult> Data(string? type)
    {
        ViewBag.SelectedType = type;
        return View(await BuildDataModelAsync(type));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Verify() => View(await BuildDataModelAsync());

    private async Task LoadResourcesAsync()
    {
        ViewBag.Resources = await _db.MapSubmissions
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    private void ValidateResource(MapSubmissionViewModel model)
    {
        if (!MapSubmissionViewModel.ResourceTypes.Contains(model.ResourceType))
            ModelState.AddModelError(nameof(model.ResourceType), "Velg en gyldig ressurstype");
        if (!double.IsFinite(model.Latitude) || !double.IsFinite(model.Longitude))
            ModelState.AddModelError(string.Empty, "Velg gyldige koordinater.");
    }

    private async Task<DataPageViewModel> BuildDataModelAsync(string? type = null)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reports = _db.FormSubmissions.AsNoTracking()
            .Where(s => User.IsInRole("Admin") || s.OwnerId == ownerId);
        var resources = _db.MapSubmissions.AsNoTracking().Include(r => r.Owner).AsQueryable();
        if (!string.IsNullOrEmpty(type))
            resources = resources.Where(r => r.ResourceType == type);
        return new DataPageViewModel
        {
            FormSubmissions = await reports.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(),
            MapSubmissions = await resources.OrderByDescending(x => x.CreatedAtUtc).ToListAsync()
        };
    }
}
