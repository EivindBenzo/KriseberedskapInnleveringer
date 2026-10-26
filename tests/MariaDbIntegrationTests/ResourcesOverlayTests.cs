using System.Security.Claims;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace MariaDbIntegrationTests;

public class ResourcesOverlayTests
{
    [Fact]
    public async Task OverlayUsesSameAuthorizedDataAndFiltersAsStandaloneResources()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(HomeController), typeof(AuthorizeAttribute)));
        var connection = new MySqlConnectionStringBuilder(DatabaseProviderConfiguration.GetRequiredConnectionString(
            new ConfigurationBuilder().AddEnvironmentVariables().Build()))
        {
            Database = $"resources_overlay_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProviderConfiguration.Configure(options, connection.ConnectionString);
        await using var db = new AppDbContext(options.Options);
        try
        {
            await db.Database.MigrateAsync();
            db.Users.AddRange(new IdentityUser { Id = "owner" }, new IdentityUser { Id = "other" });
            var own = Resource("Egen drone", "Drone", "owner");
            own.LocationName = "Gate 1, 0456 Oslo";
            var other = Resource("Annen bil", "Kjøretøy", "other");
            other.LocationName = "Bergen";
            var unavailable = Resource("Utilgjengelig drone", "Drone", "owner");
            unavailable.LocationName = "  oslo  ";
            unavailable.Availability = ResourceAvailability.Unavailable;
            db.MapSubmissions.AddRange(own, other, unavailable);
            db.FormSubmissions.AddRange(Report("owner"), Report("other"));
            await db.SaveChangesAsync();
            var controller = new HomeController(db, null!)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, "Bruker")], "test"))
                    }
                }
            };
            var standalone = Assert.IsType<ViewResult>(await controller.Resources(null, null));
            var full = Assert.IsType<DataPageViewModel>(standalone.Model);
            var partial = Assert.IsType<PartialViewResult>(await controller.Resources(null, null, overlay: true));
            Assert.Equal("Verify", partial.ViewName);
            var all = Assert.IsType<DataPageViewModel>(partial.Model);
            Assert.Equal(full.MapSubmissions.Select(r => r.Id), all.MapSubmissions.Select(r => r.Id));
            var fragment = Assert.IsType<PartialViewResult>(await controller.Resources(null, null, fragment: true));
            Assert.False((bool)controller.ViewBag.ResourcesOverlay);
            Assert.True((bool)controller.ViewBag.ResourcesPartial);
            Assert.Equal(full.MapSubmissions.Count, Assert.IsType<DataPageViewModel>(fragment.Model).MapSubmissions.Count);
            await controller.Resources(null, null, overlay: true);
            Assert.Equal(3, all.MapSubmissions.Count);
            Assert.Collection(all.Cities,
                city => { Assert.Equal("Oslo", city.Name); Assert.Equal(2, city.Count); },
                city => { Assert.Equal("Bergen", city.Name); Assert.Equal(1, city.Count); });
            Assert.Equal("owner", Assert.Single(all.FormSubmissions).OwnerId);
            Assert.True((bool)controller.ViewBag.ResourcesOverlay);
            Assert.False(ResourcePermissions.CanManage(controller.User, other.OwnerId));
            Assert.True(ResourcePermissions.CanManage(controller.User, own.OwnerId));
            Assert.IsType<ForbidResult>(await controller.Resources(null, null, history: true));
            Assert.IsType<ForbidResult>(await controller.Resources(null, null, overlay: true, history: true));

            partial = Assert.IsType<PartialViewResult>(
                await controller.Resources("Drone", "Egen", mine: true, eligible: true, overlay: true));
            Assert.Equal(own.Id, Assert.Single(Assert.IsType<DataPageViewModel>(partial.Model).MapSubmissions).Id);
            var mineCities = Assert.IsType<DataPageViewModel>(partial.Model).Cities;
            Assert.Equal(2, Assert.Single(mineCities).Count);
            partial = Assert.IsType<PartialViewResult>(
                await controller.Resources(null, null, overlay: true, city: "  OSLO "));
            Assert.Equal(2, Assert.IsType<DataPageViewModel>(partial.Model).MapSubmissions.Count);
            partial = Assert.IsType<PartialViewResult>(
                await controller.Resources(null, null, overlay: true, city: "Osl"));
            Assert.Empty(Assert.IsType<DataPageViewModel>(partial.Model).MapSubmissions);
            partial = Assert.IsType<PartialViewResult>(
                await controller.Resources("Kjøretøy", null, mine: true, overlay: true));
            Assert.Empty(Assert.IsType<DataPageViewModel>(partial.Model).MapSubmissions);

            Assert.NotNull(Attribute.GetCustomAttribute(
                typeof(HomeController).GetMethod(nameof(HomeController.DeleteConfirmed))!,
                typeof(ValidateAntiForgeryTokenAttribute)));
            controller.Request.Headers.Accept = "application/json";
            Assert.IsType<ForbidResult>(await controller.DeleteConfirmed(other.Id, other.ConcurrencyToken));
            Assert.IsType<ConflictObjectResult>(await controller.DeleteConfirmed(own.Id, "stale"));
            Assert.IsType<ConflictObjectResult>(await controller.DeleteConfirmed(own.Id));
            var busy = (await db.MapSubmissions.FindAsync(unavailable.Id))!;
            busy.Availability = ResourceAvailability.Reserved;
            await db.SaveChangesAsync();
            Assert.IsType<ConflictObjectResult>(await controller.DeleteConfirmed(busy.Id, busy.ConcurrencyToken));
            Assert.Equal(3, await db.MapSubmissions.CountAsync());
            controller.Request.Headers.Accept = "text/html";
            Assert.IsType<ViewResult>(await controller.DeleteConfirmed(own.Id, "stale"));
            Assert.False(controller.ModelState.IsValid);
            controller.Request.Headers.Accept = "application/json";
            Assert.IsType<JsonResult>(await controller.DeleteConfirmed(own.Id, own.ConcurrencyToken));
            Assert.False(await db.MapSubmissions.AnyAsync(r => r.Id == own.Id));
            partial = Assert.IsType<PartialViewResult>(
                await controller.Resources(null, null, mine: true, overlay: true));
            Assert.Equal(1, Assert.Single(Assert.IsType<DataPageViewModel>(partial.Model).Cities).Count);
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "other"), new Claim(ClaimTypes.Role, "Admin")], "test"));
            Assert.True(ResourcePermissions.CanManage(controller.User, unavailable.OwnerId));
            Assert.IsType<ViewResult>(await controller.Edit(unavailable.Id));
            var history = Assert.IsType<ViewResult>(await controller.Resources(null, null, history: true));
            var entry = Assert.Single(Assert.IsType<DataPageViewModel>(history.Model).History);
            Assert.Equal("ResourceDeleted", entry.Action);
            Assert.Equal(own.Id, entry.ResourceId);
            Assert.Equal("owner", entry.Actor);
            Assert.Contains(own.ResourceName, entry.Description);
            Assert.True(entry.OccurredAtUtc > DateTime.UtcNow.AddMinutes(-5));
            Assert.True((bool)controller.ViewBag.ResourceHistory);
            var historyOverlay = Assert.IsType<PartialViewResult>(
                await controller.Resources(null, null, overlay: true, history: true));
            Assert.Single(Assert.IsType<DataPageViewModel>(historyOverlay.Model).History);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public void CitiesNormalizeStoredAddressesExcludeCoordinatesAndSortNorwegianTies()
    {
        var labels = new[] { "Gate 1, 0456 Oslo", "  OSLO  ", "Ålesund", "Ørsta", "Ænes",
            "Bergen", "  Vestre   Toten ", "58.14670, 7.99560", "", "<script>", "0, 1" };
        var cities = ResourceCity.Aggregate(labels.Select(label => new MapSubmission { LocationName = label }));
        Assert.Equal(new[] { "Oslo", "Bergen", "Vestre Toten", "Ænes", "Ørsta", "Ålesund" }, cities.Select(c => c.Name));
        Assert.Equal(2, cities[0].Count);
    }

    private static MapSubmission Resource(string name, string type, string owner) => new()
    {
        ResourceName = name, ResourceType = type, OwnerId = owner, LocationName = "Oslo",
        Latitude = 59.91, Longitude = 10.75, Availability = ResourceAvailability.Available,
        VerificationStatus = ResourceVerificationStatus.Approved,
        VerifiedAtUtc = DateTime.UtcNow.AddDays(-1), VerificationExpiresAtUtc = DateTime.UtcNow.AddDays(1)
    };

    private static FormSubmission Report(string owner) => new()
    {
        OwnerId = owner, FirstName = "Test", LastName = "Bruker",
        Email = "test@example.test", Description = "Eldre melding"
    };
}
