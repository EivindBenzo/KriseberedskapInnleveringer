using System.Security.Claims;
using System.Text.Json;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace MariaDbIntegrationTests;

public class MapResetTests
{
    [Fact]
    public void ResetRequiresKriseledelsePostAntiforgeryAndExplicitConfirmation()
    {
        var method = typeof(HomeController).GetMethod(nameof(HomeController.ResetMap))!;
        Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(HttpPostAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(ValidateAntiForgeryTokenAttribute)));
        Assert.Equal(AppRoles.Kriseledelse, Assert.IsType<AuthorizeAttribute>(
            Attribute.GetCustomAttribute(method, typeof(AuthorizeAttribute))).Roles);
        Assert.False(Assert.IsType<bool>(method.GetParameters().Single().DefaultValue));
        Assert.True(new MapSubmission().IsOnMap);
    }

    [Fact]
    public async Task MigrationPreservesExistingPlacementsAndDefaultsNewResourcesToMapped()
    {
        await using var test = await Scenario.Create(migrate: false);
        var migrator = test.Db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261025233918_OperationalWorkflow");
        await test.Db.Database.ExecuteSqlRawAsync("""
            INSERT INTO MapSubmissions
                (ResourceName, ResourceType, LocationName, Latitude, Longitude, CreatedAtUtc, ConcurrencyToken)
            VALUES ('Existing', 'Utstyr', 'Oslo', 59.91, 10.75, UTC_TIMESTAMP(), 'existing-token')
            """);
        await migrator.MigrateAsync();
        Assert.True((await test.Db.MapSubmissions.SingleAsync()).IsOnMap);
        Assert.False(test.Db.Database.HasPendingModelChanges());
        test.Db.MapSubmissions.Add(Resource("New"));
        test.Db.MapSubmissions.Add(new MapSubmission
        {
            ResourceName = "Explicitly off map", ResourceType = "Utstyr", LocationName = "Oslo", IsOnMap = false
        });
        await test.Db.SaveChangesAsync();
        test.Db.ChangeTracker.Clear();
        Assert.True((await test.Db.MapSubmissions.SingleAsync(r => r.ResourceName == "New")).IsOnMap);
        Assert.False((await test.Db.MapSubmissions.SingleAsync(r => r.ResourceName == "Explicitly off map")).IsOnMap);
    }

    [Fact]
    public async Task UnconfirmedResetAndUnauthorizedActorsNeverChangeData()
    {
        await using var test = await Scenario.Create();
        await test.Seed();
        foreach (var actor in new[]
                 {
                     Principal("owner"), new ClaimsPrincipal(new ClaimsIdentity()),
                     new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "dispatch"), new Claim(ClaimTypes.Role, AppRoles.DispatchTeam)], "test"))
                 })
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ResetMapAsync(actor, true));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ResetMapAsync(Principal("admin", true), false));
        test.Db.ChangeTracker.Clear();
        Assert.Equal(2, await test.Db.MapSubmissions.CountAsync(r => r.IsOnMap));
        Assert.Single(await test.Db.WorkflowAudits.ToListAsync());
        var controller = test.Controller();
        Assert.Equal("Resources", Assert.IsType<RedirectToActionResult>(await controller.ResetMap()).ActionName);
        Assert.NotNull(controller.TempData["MapError"]);
        Assert.Null(controller.TempData["MapSuccess"]);
        Assert.Equal(2, await test.Db.MapSubmissions.CountAsync(r => r.IsOnMap));
    }

    [Fact]
    public async Task ResetOnlyRemovesPlacementsAndPreservesWorkflowCoordinatesAndHistory()
    {
        await using var test = await Scenario.Create();
        await test.Seed();
        var before = await test.Db.MapSubmissions.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        var needs = JsonSerializer.Serialize(await test.Db.OperationalNeeds.AsNoTracking().ToListAsync());
        var requests = JsonSerializer.Serialize(await test.Db.ResourceRequests.AsNoTracking().ToListAsync());
        var reports = JsonSerializer.Serialize(await test.Db.FormSubmissions.AsNoTracking().ToListAsync());
        var history = JsonSerializer.Serialize(await test.Db.WorkflowAudits.AsNoTracking().SingleAsync());
        var controller = test.Controller();
        Assert.Equal("Resources", Assert.IsType<RedirectToActionResult>(await controller.ResetMap(true)).ActionName);
        Assert.StartsWith("2 ", Assert.IsType<string>(controller.TempData["MapSuccess"]));
        Assert.Null(controller.TempData["MapError"]);
        await using var persisted = new AppDbContext(test.Options);
        var after = await persisted.MapSubmissions.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.False(after[i].IsOnMap);
            if (before[i].IsOnMap)
            {
                Assert.NotEqual(before[i].ConcurrencyToken, after[i].ConcurrencyToken);
                Assert.NotNull(after[i].UpdatedAtUtc);
            }
            else
            {
                Assert.Equal(before[i].ConcurrencyToken, after[i].ConcurrencyToken);
                Assert.Equal(before[i].UpdatedAtUtc, after[i].UpdatedAtUtc);
            }
            before[i].IsOnMap = after[i].IsOnMap;
            before[i].ConcurrencyToken = after[i].ConcurrencyToken;
            before[i].UpdatedAtUtc = after[i].UpdatedAtUtc;
            Assert.Equal(JsonSerializer.Serialize(before[i]), JsonSerializer.Serialize(after[i]));
        }
        Assert.Equal(needs, JsonSerializer.Serialize(await persisted.OperationalNeeds.AsNoTracking().ToListAsync()));
        Assert.Equal(requests, JsonSerializer.Serialize(await persisted.ResourceRequests.AsNoTracking().ToListAsync()));
        Assert.Equal(reports, JsonSerializer.Serialize(await persisted.FormSubmissions.AsNoTracking().ToListAsync()));
        Assert.Equal(history, JsonSerializer.Serialize(await persisted.WorkflowAudits.AsNoTracking().SingleAsync(a => a.Action == "Existing")));
        var audits = await persisted.WorkflowAudits.Where(a => a.Action == "ResourceRemovedFromMap").ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a =>
        {
            Assert.Equal("admin", a.ActorId);
            Assert.False(string.IsNullOrWhiteSpace(a.Reason));
            Assert.NotEqual(default, a.OccurredAtUtc);
            Assert.Contains(before, r => r.Id == a.ResourceId && r.ResourceName != "Off map");
        });
        Assert.Equal(0, await test.Service.ResetMapAsync(Principal("admin", true), true));
        Assert.Equal(3, await persisted.WorkflowAudits.CountAsync());
    }

    [Fact]
    public async Task EmptyResetSucceedsWithoutAudits()
    {
        await using var test = await Scenario.Create();
        Assert.Equal(0, await test.Service.ResetMapAsync(Principal("admin", true), true));
        Assert.Empty(await test.Db.WorkflowAudits.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentResourceChangeRollsBackEveryPlacementAndAudit()
    {
        await using var test = await Scenario.Create();
        await test.Seed();
        var stale = await test.Db.MapSubmissions.SingleAsync(r => r.ResourceName == "Allocated");
        var other = await test.Db.MapSubmissions.SingleAsync(r => r.ResourceName == "Reserved");
        var otherToken = other.ConcurrencyToken;
        await using (var writer = new AppDbContext(test.Options))
        {
            var current = await writer.MapSubmissions.SingleAsync(r => r.Id == stale.Id);
            current.Capability = "Concurrent update";
            await writer.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ResetMapAsync(Principal("admin", true), true));
        Assert.Empty(test.Db.ChangeTracker.Entries());
        await using var persisted = new AppDbContext(test.Options);
        Assert.Equal(2, await persisted.MapSubmissions.CountAsync(r => r.IsOnMap));
        Assert.Equal(otherToken, (await persisted.MapSubmissions.SingleAsync(r => r.Id == other.Id)).ConcurrencyToken);
        Assert.Equal("Concurrent update", (await persisted.MapSubmissions.SingleAsync(r => r.Id == stale.Id)).Capability);
        Assert.Single(await persisted.WorkflowAudits.ToListAsync());
    }

    [Fact]
    public async Task MapHidesOffMapResourcesButListingRetainsThemAndEditPlacesThemAgain()
    {
        await using var test = await Scenario.Create();
        test.Db.Users.Add(new IdentityUser { Id = "owner", UserName = "owner" });
        var editable = Resource("Editable");
        editable.OwnerId = "owner";
        test.Db.MapSubmissions.Add(editable);
        await test.Db.SaveChangesAsync();
        var resource = await test.Db.MapSubmissions.SingleAsync();
        await test.Service.ResetMapAsync(Principal("admin", true), true);
        var controller = test.Controller(Provider("owner"));
        await controller.Map();
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<MapSubmission>>(controller.ViewBag.Resources));
        var listing = Assert.IsType<DataPageViewModel>(
            Assert.IsType<ViewResult>(await controller.Resources(null, null)).Model);
        Assert.Single(listing.MapSubmissions);
        var edit = new MapSubmissionViewModel
        {
            ResourceName = resource.ResourceName, ResourceType = resource.ResourceType,
            Latitude = resource.Latitude, Longitude = resource.Longitude,
            Availability = resource.Availability, Version = resource.ConcurrencyToken
        };
        Assert.Equal("Map", Assert.IsType<RedirectToActionResult>(await controller.Edit(resource.Id, edit)).ActionName);
        test.Db.ChangeTracker.Clear();
        resource = await test.Db.MapSubmissions.SingleAsync();
        Assert.True(resource.IsOnMap);
        Assert.Equal(ResourceVerificationStatus.Pending, resource.VerificationStatus);
        Assert.Null(resource.VerifiedAtUtc);
        Assert.Null(resource.VerificationExpiresAtUtc);
        Assert.Contains(await test.Db.WorkflowAudits.ToListAsync(), a => a.Action == "ResourceEditedReverificationRequired");
        await controller.Map();
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<MapSubmission>>(controller.ViewBag.Resources));
    }

    [Fact]
    public async Task ServiceEditAlsoRestoresPlacementAndRequiresReverification()
    {
        await using var test = await Scenario.Create();
        test.Db.Users.Add(new IdentityUser { Id = "owner", UserName = "owner" });
        var resource = Resource("Editable");
        resource.OwnerId = "owner";
        resource.IsOnMap = false;
        test.Db.MapSubmissions.Add(resource);
        await test.Db.SaveChangesAsync();
        await test.Service.UpdateResourceAsync(resource.Id, Resource("Editable"), ResourceAvailability.Available,
            resource.ConcurrencyToken, Provider("owner"), "Place again");
        test.Db.ChangeTracker.Clear();
        resource = await test.Db.MapSubmissions.SingleAsync();
        Assert.True(resource.IsOnMap);
        Assert.Equal(ResourceVerificationStatus.Pending, resource.VerificationStatus);
        Assert.Null(resource.VerificationExpiresAtUtc);
    }

    private static MapSubmission Resource(string name) => new()
    {
        ResourceName = name, ResourceType = "Utstyr", LocationName = "Oslo",
        Latitude = 59.91, Longitude = 10.75, CreatedAtUtc = DateTime.UtcNow,
        VerificationStatus = ResourceVerificationStatus.Approved,
        VerifiedAtUtc = DateTime.UtcNow.AddDays(-1), VerificationExpiresAtUtc = DateTime.UtcNow.AddDays(10)
    };

    private static ClaimsPrincipal Provider(string id) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, AppRoles.Ressursleverandor)], "test"));

    private static ClaimsPrincipal Principal(string id, bool admin = false) => new(new ClaimsIdentity(
        admin
            ? [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, AppRoles.Kriseledelse)]
            : [new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    private sealed class Scenario : IAsyncDisposable
    {
        public required DbContextOptions<AppDbContext> Options { get; init; }
        public required AppDbContext Db { get; init; }
        public WorkflowService Service => new(Db, Microsoft.Extensions.Options.Options.Create(new WorkflowOptions()));

        public static async Task<Scenario> Create(bool migrate = true)
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var connection = new MySqlConnectionStringBuilder(DatabaseProviderConfiguration.GetRequiredConnectionString(configuration))
            {
                Database = $"map_reset_{Guid.NewGuid():N}"
            };
            var builder = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProviderConfiguration.Configure(builder, connection.ConnectionString);
            var scenario = new Scenario { Options = builder.Options, Db = new AppDbContext(builder.Options) };
            if (migrate) await scenario.Db.Database.MigrateAsync();
            return scenario;
        }

        public async Task Seed()
        {
            Db.Users.Add(new IdentityUser { Id = "owner", UserName = "owner" });
            var reserved = Resource("Reserved");
            reserved.OwnerId = "owner";
            reserved.Availability = ResourceAvailability.Reserved;
            var allocated = Resource("Allocated");
            allocated.OwnerId = "owner";
            allocated.Availability = ResourceAvailability.Allocated;
            var offMap = Resource("Off map");
            offMap.IsOnMap = false;
            Db.MapSubmissions.AddRange(reserved, allocated, offMap);
            var need = new OperationalNeed { Description = "Keep need", Place = "Oslo", CreatedById = "admin" };
            Db.OperationalNeeds.Add(need);
            await Db.SaveChangesAsync();
            Db.ResourceRequests.AddRange(
                new ResourceRequest { NeedId = need.Id, ResourceId = reserved.Id, CreatedById = "admin", Reason = "Keep reservation", Status = ResourceRequestStatus.Accepted },
                new ResourceRequest { NeedId = need.Id, ResourceId = allocated.Id, CreatedById = "admin", Reason = "Keep allocation", Status = ResourceRequestStatus.Allocated });
            Db.FormSubmissions.Add(new FormSubmission { FirstName = "Keep", LastName = "Report", Email = "report@example.test", Description = "Keep report" });
            Db.WorkflowAudits.Add(new WorkflowAudit { ActorId = "admin", Action = "Existing", Reason = "Keep audit", ResourceId = reserved.Id, OccurredAtUtc = DateTime.UtcNow });
            await Db.SaveChangesAsync();
        }

        public HomeController Controller(ClaimsPrincipal? user = null) => new(Db, new StubGeocoder())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user ?? Principal("admin", true) } },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataProvider())
        };

        public async ValueTask DisposeAsync()
        {
            await Db.Database.EnsureDeletedAsync();
            await Db.DisposeAsync();
        }
    }

    private sealed class StubGeocoder : IPlaceGeocoder
    {
        public Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default) =>
            Task.FromResult("Oslo");
        public Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceResult>>([]);
    }

    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
