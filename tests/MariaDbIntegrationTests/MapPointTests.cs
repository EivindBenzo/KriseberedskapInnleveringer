using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MariaDbIntegrationTests;

public class MapPointTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 0d)]
    [InlineData(0d, null)]
    [InlineData(-91d, 0d)]
    [InlineData(91d, 0d)]
    [InlineData(0d, -181d)]
    [InlineData(0d, 181d)]
    [InlineData(double.NaN, 0d)]
    [InlineData(0d, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 0d)]
    public async Task DirectMapPostRejectsMissingOutOfRangeAndNonfinitePoints(double? lat, double? lng)
    {
        using var db = new TestContext();
        var geocoder = new StubGeocoder();
        var controller = CreateController(db, geocoder);
        var model = Model(lat, lng);
        var result = Assert.IsType<ViewResult>(await controller.Map(model));
        Assert.Same(model, result.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, geocoder.Calls);
        Assert.Equal(0, db.Saves);
        Assert.Empty(db.ChangeTracker.Entries<MapSubmission>());
        Assert.IsType<BadRequestResult>(await CreateController(db, geocoder).Place(lat, lng));
    }

    [Fact]
    public void ViewModelRequiresExplicitSelectionAndDoesNotValidatePostedLabel()
    {
        var model = Model(null, null);
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(model.Latitude)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(model.Longitude)));
        model.Latitude = 0;
        model.Longitude = 0;
        model.LocationName = new string('x', 10000);
        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), [], true));
    }

    [Fact]
    public async Task SaveUsesAuthoritativeLabelAndIgnoresPostedLabelModelState()
    {
        using var db = new TestContext();
        var geocoder = new StubGeocoder();
        var controller = CreateController(db, geocoder);
        controller.ModelState.SetModelValue("LocationName", "forged", "forged");
        controller.ModelState.AddModelError("LocationName", "posted junk");
        var model = Model(0, 0);
        var result = Assert.IsType<RedirectToActionResult>(await controller.Map(model));
        Assert.Equal("Map", result.ActionName);
        var resource = Assert.Single(db.ChangeTracker.Entries<MapSubmission>()).Entity;
        Assert.Equal("Authoritative place", resource.LocationName);
        Assert.Equal(0, resource.Latitude);
        Assert.Equal("owner", resource.OwnerId);
        Assert.False(controller.ModelState.ContainsKey("LocationName"));
        Assert.Equal(1, db.Saves);
    }

    [Fact]
    public async Task GetMapHasNoSelection()
    {
        using var db = new TestContext();
        var controller = CreateController(db, new StubGeocoder());
        var result = Assert.IsType<ViewResult>(await controller.Map());
        var model = Assert.IsType<MapSubmissionViewModel>(result.Model);
        Assert.Null(model.Latitude);
        Assert.Null(model.Longitude);
        Assert.Empty(model.LocationName);
    }

    [Fact]
    public async Task EditRetainsSelectionOnErrorAndSuccessfulEditAndDeleteReturnMap()
    {
        using var db = new TestContext();
        var resource = new MapSubmission { Id = 7, OwnerId = "owner", ResourceName = "Old", ResourceType = "Utstyr" };
        db.MapSubmissions.Add(resource);
        var controller = CreateController(db, new StubGeocoder());
        controller.ModelState.SetModelValue("LocationName", "forged", "forged");
        controller.ModelState.AddModelError("LocationName", "posted junk");
        var invalid = Model(59, 10);
        invalid.ResourceType = "forged";
        var error = Assert.IsType<ViewResult>(await controller.Edit(7, invalid));
        Assert.Equal("Map", error.ViewName);
        Assert.Same(invalid, error.Model);
        Assert.Equal(7, (int)controller.ViewBag.EditId);
        Assert.Equal(59, invalid.Latitude);
        Assert.Equal(10, invalid.Longitude);
        Assert.Equal("Authoritative place", invalid.LocationName);
        Assert.False(controller.ModelState.ContainsKey("LocationName"));
        Assert.Equal(0, db.Saves);
        controller = CreateController(db, new StubGeocoder());
        var edited = Assert.IsType<RedirectToActionResult>(await controller.Edit(7, Model(58, 9)));
        Assert.Equal("Map", edited.ActionName);
        Assert.Equal("Authoritative place", resource.LocationName);
        Assert.Equal(58, resource.Latitude);
        var deleted = Assert.IsType<RedirectToActionResult>(await controller.DeleteConfirmed(7));
        Assert.Equal("Map", deleted.ActionName);
    }

    [Fact]
    public async Task NonOwnerCannotEditOrDelete()
    {
        using var db = new TestContext();
        db.MapSubmissions.Add(new MapSubmission { Id = 7, OwnerId = "other" });
        var geocoder = new StubGeocoder();
        var controller = CreateController(db, geocoder);
        Assert.IsType<ForbidResult>(await controller.Edit(7, Model(58, 9)));
        Assert.IsType<ForbidResult>(await controller.DeleteConfirmed(7));
        Assert.Equal(0, db.Saves);
        Assert.Equal(0, geocoder.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("Oslo\n")]
    public async Task SearchRejectsInvalidQuery(string? query)
    {
        using var db = new TestContext();
        Assert.IsType<BadRequestResult>(await CreateController(db, new StubGeocoder()).SearchPlaces(query));
    }

    private static MapSubmissionViewModel Model(double? lat, double? lng) => new()
    {
        ResourceName = "Resource", ResourceType = "Utstyr", LocationName = "Untrusted", Latitude = lat, Longitude = lng
    };

    private static TestController CreateController(TestContext db, IPlaceGeocoder geocoder)
    {
        var controller = new TestController(db, geocoder)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider())
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        return controller;
    }

    private sealed class TestController(AppDbContext db, IPlaceGeocoder geocoder) : HomeController(db, geocoder)
    {
        protected override Task LoadResourcesAsync()
        {
            ViewBag.Resources = Array.Empty<MapSubmission>();
            return Task.CompletedTask;
        }
    }

    private sealed class TestContext() : AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql("Server=unused;Database=unused;User=unused", new MariaDbServerVersion(new Version(11, 4, 0))).Options)
    {
        public int Saves { get; private set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class StubGeocoder : IPlaceGeocoder
    {
        public int Calls { get; private set; }
        public Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult("Authoritative place");
        }
        public Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceResult>>([]);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
