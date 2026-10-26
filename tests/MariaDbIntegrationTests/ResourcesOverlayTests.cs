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
            var other = Resource("Annen bil", "Kjøretøy", "other");
            var unavailable = Resource("Utilgjengelig drone", "Drone", "owner");
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
            Assert.Equal(3, all.MapSubmissions.Count);
            Assert.Equal("owner", Assert.Single(all.FormSubmissions).OwnerId);
            Assert.True((bool)controller.ViewBag.ResourcesOverlay);
            Assert.False(ResourcePermissions.CanManage(controller.User, other.OwnerId));
            Assert.True(ResourcePermissions.CanManage(controller.User, own.OwnerId));

            partial = Assert.IsType<PartialViewResult>(
                await controller.Resources("Drone", "Egen", mine: true, eligible: true, overlay: true));
            Assert.Equal(own.Id, Assert.Single(Assert.IsType<DataPageViewModel>(partial.Model).MapSubmissions).Id);
            partial = Assert.IsType<PartialViewResult>(
                await controller.Resources("Kjøretøy", null, mine: true, overlay: true));
            Assert.Empty(Assert.IsType<DataPageViewModel>(partial.Model).MapSubmissions);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
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
