using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MariaDbIntegrationTests;

public class OperationsTests(OperationsTests.Site site) : IClassFixture<OperationsTests.Site>
{
    [Theory]
    [InlineData("Bruker")]
    [InlineData("Admin")]
    public async Task ResourcesOverlayRendersExistingPermissionAwareViewsWithoutApplicationChrome(string role)
    {
        using var client = site.Client(role == "Admin" ? site.AdminId : site.OwnerId, role);
        var response = await client.GetAsync("/Home/Resources?overlay=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(site.OwnResource.ResourceName, html);
        Assert.Contains(site.OtherResource.ResourceName, html);
        Assert.Contains("Detaljer om ressursen", html);
        Assert.Contains("data-resources-link", html);
        Assert.Contains("name=\"overlay\" value=\"true\"", html);
        Assert.Contains("data-map-focus=", html);
        Assert.DoesNotContain("<!DOCTYPE", html);
        Assert.DoesNotContain("class=\"navbar\"", html);
        Assert.DoesNotContain("<script", html);
        Assert.Contains($"href=\"/Home/Edit/{site.OwnResource.Id}\"", html);
        Assert.Contains($"href=\"/Home/Delete/{site.OwnResource.Id}\"", html);
        Assert.Contains("target=\"_blank\" rel=\"noopener\"", html);
        if (role == "Admin")
        {
            Assert.Contains($"href=\"/Home/Edit/{site.OtherResource.Id}\"", html);
            Assert.Contains("action=\"/Home/ResetMap\"", html);
            Assert.Contains("name=\"__RequestVerificationToken\"", html);
        }
        else
        {
            Assert.DoesNotContain($"href=\"/Home/Edit/{site.OtherResource.Id}\"", html);
            Assert.DoesNotContain($"href=\"/Home/Delete/{site.OtherResource.Id}\"", html);
            Assert.DoesNotContain("action=\"/Home/ResetMap\"", html);
            Assert.DoesNotContain("Koordiner", html);
        }
        var mine = await client.GetStringAsync("/Home/Resources?overlay=true&mine=true");
        Assert.DoesNotContain(site.OtherResource.ResourceName, mine);
    }

    [Fact]
    public void AuthorizationMetadataMatchesSupportedRolesAndIndexRoute()
    {
        var controller = typeof(OperationsController);
        var authorization = Assert.IsType<AuthorizeAttribute>(
            Attribute.GetCustomAttribute(controller, typeof(AuthorizeAttribute)));
        Assert.Equal(new[] { "Admin", "Bruker" }, authorization.Roles!.Split(',').OrderBy(role => role));
        Assert.NotNull(Attribute.GetCustomAttribute(controller, typeof(AutoValidateAntiforgeryTokenAttribute)));
        var index = controller.GetMethod(nameof(OperationsController.Index))!;
        Assert.NotNull(Attribute.GetCustomAttribute(index, typeof(HttpGetAttribute)));
        Assert.Null(Attribute.GetCustomAttribute(index, typeof(AllowAnonymousAttribute)));
    }

    [Theory]
    [InlineData("/Operations")]
    [InlineData("/Operations/Index")]
    public async Task ProviderRouteRendersOnlyOwnedRequestsAndRelatedNeeds(string route)
    {
        using var client = site.Client(site.OwnerId, "Bruker");
        var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("<section class=\"operations-workspace\" aria-labelledby=\"operations-heading\">", html);
        Assert.Contains(site.OwnResource.ResourceName, html);
        Assert.Contains(site.OwnNeed.Description, html);
        Assert.Contains(site.OwnRequest.Reason, html);
        Assert.DoesNotContain(site.OtherResource.ResourceName, html);
        Assert.DoesNotContain(site.OtherNeed.Description, html);
        Assert.DoesNotContain(site.OtherRequest.Reason, html);
        Assert.DoesNotContain(site.UnrelatedNeed.Description, html);
        Assert.DoesNotContain(site.Audit.Reason, html);
        Assert.DoesNotContain("Beslutningslogg", html);
        Assert.DoesNotContain("action=\"/Operations/CreateNeed\"", html);
        Assert.DoesNotContain("action=\"/Operations/Allocate\"", html);
        Assert.DoesNotContain("action=\"/Operations/Verify\"", html);
        Assert.Contains("href=\"/Operations\"", html);
        Assert.DoesNotContain("href=\"/Operations#resources\"", html);
        Assert.DoesNotContain("href=\"/Operations#needs\"", html);
        Assert.DoesNotContain("/Operations/Requests", html);
        Assert.DoesNotContain("/Operations/Queue", html);
        Assert.DoesNotContain("/Operations/Needs", html);
    }

    [Fact]
    public async Task AdminSeesGlobalCoordinationAndNavigationTargetsRealSections()
    {
        using var client = site.Client(site.AdminId, "Admin");
        var response = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        foreach (var text in new[] { site.OwnResource.ResourceName, site.OtherResource.ResourceName,
                     site.OwnNeed.Description, site.OtherNeed.Description, site.UnrelatedNeed.Description,
                     site.OwnRequest.Reason, site.OtherRequest.Reason, site.Audit.Reason })
            Assert.Contains(text, html);
        Assert.Contains("href=\"/Operations#resources\"", html);
        Assert.Contains("href=\"/Operations#needs\"", html);
        Assert.Contains("id=\"resources\"", html);
        Assert.Contains("id=\"needs\"", html);
        Assert.Contains("action=\"/Operations/CreateNeed\"", html);
        Assert.Contains("action=\"/Operations/Verify\"", html);
        Assert.Contains("<div class=\"audit-scroll\" tabindex=\"0\" role=\"region\" aria-label=\"Beslutningslogg\">", html);
        Assert.DoesNotContain("action=\"/Operations/Respond\"", html);
    }

    [Fact]
    public async Task UnrelatedProviderGetsMeaningfulEmptyStateWithoutDisclosure()
    {
        using var client = site.Client("unrelated-provider", "Bruker");
        var response = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Du har ingen innkommende forespørsler.", html);
        Assert.Contains("Du har ingen registrerte ressurser.", html);
        Assert.Contains("Ingen behov er knyttet til dine forespørsler.", html);
        Assert.DoesNotContain(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain(site.OtherResource.ResourceName, html);
        Assert.DoesNotContain(site.OwnNeed.Description, html);
        Assert.DoesNotContain(site.OtherNeed.Description, html);
        Assert.DoesNotContain(site.Audit.Reason, html);
        Assert.DoesNotContain("action=\"/Operations/Respond\"", html);
    }

    [Theory]
    [InlineData("Bruker", 1)]
    [InlineData("Admin", 1)]
    public async Task HomeCardsLinkToAuthorizedOperationsRoutes(string role, int expectedCards)
    {
        using var client = site.Client(role == "Admin" ? site.AdminId : site.OwnerId, role);
        var html = await client.GetStringAsync("/");
        var cards = Regex.Matches(html, "<a\\b[^>]*class=\"feature-card\"[^>]*>")
            .Select(m => m.Value).Where(a => a.Contains("href=\"/Operations")).ToList();
        Assert.Equal(expectedCards, cards.Count);
        Assert.DoesNotContain("/Operations/Requests", html);
        Assert.DoesNotContain("/Operations/Queue", html);
        Assert.DoesNotContain("/Operations/Needs", html);
        Assert.Contains(cards, a => a.Contains("href=\"/Operations\""));
        Assert.DoesNotContain("href=\"/Operations#resources\"", html);
        Assert.DoesNotContain("href=\"/Operations#needs\"", html);
        foreach (var card in cards)
        {
            var href = Regex.Match(card, "href=\"([^\"]+)\"").Groups[1].Value;
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(href)).StatusCode);
        }
    }

    [Theory]
    [InlineData("/Home/Resources", false)]
    [InlineData("/Home/Resources?query=no-resource-reset-rendering-fixture", true)]
    [InlineData("/Home/Resources?mine=true", true)]
    public async Task AdminResourceWorkspaceHasOneGlobalResetEvenWhenListIsEmpty(string route, bool empty)
    {
        using var client = site.Client(site.AdminId, "Admin");
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var reset = Assert.Single(Regex.Matches(html,
            "<form\\b[^>]*action=\"/Home/ResetMap\"[^>]*>.*?</form>", RegexOptions.Singleline).Cast<Match>()).Value;
        Assert.Contains("method=\"post\"", reset);
        Assert.Contains("name=\"__RequestVerificationToken\"", reset);
        var confirmation = Assert.Single(Regex.Matches(reset, "<input\\b[^>]*name=\"confirmed\"[^>]*>").Cast<Match>()).Value;
        Assert.Contains("type=\"checkbox\"", confirmation);
        Assert.Contains("value=\"true\"", confirmation);
        Assert.Contains("required", confirmation);
        Assert.DoesNotContain("checked", confirmation);
        if (empty)
        {
            Assert.Contains("Ingen ressurser passer valget.", html);
            Assert.DoesNotContain(site.OwnResource.ResourceName, html);
            Assert.DoesNotContain(site.OtherResource.ResourceName, html);
        }
        else
        {
            Assert.Contains(site.OwnResource.ResourceName, html);
            Assert.Contains(site.OtherResource.ResourceName, html);
        }
    }

    [Fact]
    public async Task ProviderResourceWorkspaceHasNoGlobalReset()
    {
        using var client = site.Client(site.OwnerId, "Bruker");
        var html = await client.GetStringAsync("/Home/Resources");
        Assert.Contains(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain("action=\"/Home/ResetMap\"", html);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("roleless", null, HttpStatusCode.Forbidden)]
    [InlineData("unknown-role", "Observer", HttpStatusCode.Forbidden)]
    public async Task UnauthorizedUsersCannotLoadPageOrSeeNavigation(string? id, string? role, HttpStatusCode status)
    {
        using var client = site.Client(id, role);
        var response = await client.GetAsync("/Operations");
        Assert.Equal(status, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain(site.OwnRequest.Reason, html);
        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.DoesNotContain("href=\"/Operations", await home.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RoleWithoutActorIdIsForbiddenWithoutLoadingData()
    {
        using var client = site.Client("", "Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Operations")).StatusCode);
        Assert.DoesNotContain("href=\"/Operations", await client.GetStringAsync("/"));
    }

    [Theory]
    [InlineData("Verify")]
    [InlineData("CreateNeed")]
    [InlineData("NeedStatus")]
    [InlineData("CreateRequest")]
    [InlineData("Allocate")]
    [InlineData("Cancel")]
    public async Task CoordinatorActionsRejectProvidersAndUnauthenticatedUsers(string action)
    {
        using var provider = site.Client(site.OwnerId, "Bruker");
        using var anonymous = site.Client();
        using var roleless = site.Client("roleless");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await provider.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await roleless.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Theory]
    [InlineData("Respond")]
    [InlineData("Return")]
    [InlineData("Availability")]
    public async Task OwnerActionsRequireAuthenticationRoleAndAntiforgery(string action)
    {
        using var provider = site.Client(site.OwnerId, "Bruker");
        using var anonymous = site.Client();
        using var roleless = site.Client("roleless");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await provider.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await roleless.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task TamperedOwnerActionsCannotChangeAnotherProvidersResourceOrRequest()
    {
        using var provider = site.Client(site.OwnerId, "Bruker");
        await using var db = site.Db();
        var auditsBefore = await db.WorkflowAudits.CountAsync(a => a.ResourceId == site.OtherResource.Id);
        foreach (var action in new[] { "Respond", "Return", "Availability" })
        {
            var response = await Post(provider, action, new()
            {
                ["id"] = (action == "Availability" ? site.OtherResource.Id : site.OtherRequest.Id).ToString(),
                ["token"] = action == "Availability" ? site.OtherResource.ConcurrencyToken : site.OtherRequest.ConcurrencyToken,
                ["reason"] = "Forsøk fra feil eier", ["accept"] = "true", ["availability"] = "Unavailable"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var page = WebUtility.HtmlDecode(await provider.GetStringAsync(response.Headers.Location));
            Assert.Contains("role=\"alert\"", page);
            Assert.DoesNotContain(site.OtherResource.ResourceName, page);
            Assert.DoesNotContain(site.OtherNeed.Description, page);
            Assert.DoesNotContain(site.OtherRequest.Reason, page);
        }
        Assert.Equal(ResourceRequestStatus.Pending,
            (await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == site.OtherRequest.Id)).Status);
        Assert.Equal(ResourceAvailability.Available,
            (await db.MapSubmissions.AsNoTracking().SingleAsync(r => r.Id == site.OtherResource.Id)).Availability);
        Assert.Equal(auditsBefore, await db.WorkflowAudits.CountAsync(a => a.ResourceId == site.OtherResource.Id));
    }

    [Fact]
    public async Task OwnerAcceptsAdminAllocatesAndOwnerReturnsThroughRealRoutes()
    {
        using var owner = site.Client(site.OwnerId, "Bruker");
        using var admin = site.Client(site.AdminId, "Admin");
        await using var db = site.Db();
        var request = await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == site.OwnRequest.Id);
        var forbiddenResponse = await Post(admin, "Respond", new()
        {
            ["id"] = request.Id.ToString(), ["token"] = request.ConcurrencyToken,
            ["accept"] = "true", ["reason"] = "Administrator er ikke eier"
        });
        Assert.Equal(HttpStatusCode.Redirect, forbiddenResponse.StatusCode);
        Assert.Equal(ResourceRequestStatus.Pending,
            (await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);

        foreach (var step in new[]
                 {
                     (Client: owner, Action: "Respond", Status: ResourceRequestStatus.Accepted),
                     (Client: admin, Action: "Allocate", Status: ResourceRequestStatus.Allocated),
                     (Client: owner, Action: "Return", Status: ResourceRequestStatus.Returned)
                 })
        {
            request = await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
            var response = await Post(step.Client, step.Action, new()
            {
                ["id"] = request.Id.ToString(), ["token"] = request.ConcurrencyToken,
                ["accept"] = "true", ["reason"] = "Bekreftet i rutetest"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Operations", response.Headers.Location?.OriginalString);
            Assert.Equal(step.Status,
                (await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        }
        var resource = await db.MapSubmissions.AsNoTracking().SingleAsync(r => r.Id == site.OwnResource.Id);
        Assert.Equal(ResourceAvailability.Available, resource.Availability);
        Assert.Equal(ResourceVerificationStatus.Pending, resource.VerificationStatus);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string action, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync("/Operations");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Operations forms must include an antiforgery token.");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync($"/Operations/{action}", new FormUrlEncodedContent(fields));
    }

    public sealed class Site : IAsyncLifetime
    {
        private WebApplication app = null!;
        private DbContextOptions<AppDbContext> options = null!;
        private readonly string marker = Guid.NewGuid().ToString("N");
        public string OwnerId => $"operations-owner-{marker}";
        public string OtherId => $"operations-other-{marker}";
        public string AdminId => $"operations-admin-{marker}";
        public MapSubmission OwnResource { get; private set; } = null!;
        public MapSubmission OtherResource { get; private set; } = null!;
        public OperationalNeed OwnNeed { get; private set; } = null!;
        public OperationalNeed OtherNeed { get; private set; } = null!;
        public OperationalNeed UnrelatedNeed { get; private set; } = null!;
        public ResourceRequest OwnRequest { get; private set; } = null!;
        public ResourceRequest OtherRequest { get; private set; } = null!;
        public WorkflowAudit Audit { get; private set; } = null!;
        public AppDbContext Db() => new(options);

        public HttpClient Client(string? id = null, string? role = null)
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };
            if (id != null) client.DefaultRequestHeaders.Add("X-Test-Actor", id.Length == 0 ? "missing-id" : id);
            if (role != null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
            return client;
        }

        public async Task InitializeAsync()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProviderConfiguration.Configure(dbOptions,
                DatabaseProviderConfiguration.GetRequiredConnectionString(configuration));
            options = dbOptions.Options;
            await using (var db = Db())
            {
                await db.Database.MigrateAsync();
                db.Users.AddRange(new IdentityUser { Id = OwnerId, UserName = $"{OwnerId}@example.test" },
                    new IdentityUser { Id = OtherId, UserName = $"{OtherId}@example.test" },
                    new IdentityUser { Id = AdminId, UserName = $"{AdminId}@example.test" });
                OwnResource = Resource(OwnerId);
                OtherResource = Resource(OtherId);
                OwnNeed = Need("Eiers behov");
                OtherNeed = Need("Annen eiers behov");
                UnrelatedNeed = Need("Behov uten forespørsler");
                db.MapSubmissions.AddRange(OwnResource, OtherResource);
                db.OperationalNeeds.AddRange(OwnNeed, OtherNeed, UnrelatedNeed);
                await db.SaveChangesAsync();
                OwnRequest = Request(OwnResource, OwnNeed);
                OtherRequest = Request(OtherResource, OtherNeed);
                Audit = new WorkflowAudit
                {
                    ActorId = AdminId, Action = "Test", Reason = $"Intern beslutning {marker}",
                    OccurredAtUtc = DateTime.UtcNow, ResourceId = OtherResource.Id
                };
                db.ResourceRequests.AddRange(OwnRequest, OtherRequest);
                db.WorkflowAudits.Add(Audit);
                await db.SaveChangesAsync();
            }
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "HeimevernetInnlevering1.csproj")))
                root = root.Parent;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(OperationsController).Assembly.GetName().Name,
                ContentRootPath = root?.FullName ?? throw new InvalidOperationException("Project root not found."),
                EnvironmentName = "Development"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllersWithViews();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddAuthentication("OperationsTest")
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("OperationsTest", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddDbContext<AppDbContext>(o =>
                DatabaseProviderConfiguration.Configure(o, DatabaseProviderConfiguration.GetRequiredConnectionString(configuration)));
            builder.Services.Configure<WorkflowOptions>(_ => { });
            builder.Services.AddScoped<WorkflowService>();
            builder.Services.AddSingleton<IPlaceGeocoder>(new PlaceGeocoder(new HttpClient(), Options.Create(new GeocodingOptions())));
            app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            await app.StartAsync();
        }

        private MapSubmission Resource(string owner) => new()
        {
            OwnerId = owner, ResourceName = $"Ressurs {owner}", ResourceType = "Utstyr", LocationName = "Oslo",
            Latitude = 59.91, Longitude = 10.75, CreatedAtUtc = DateTime.UtcNow,
            VerificationStatus = ResourceVerificationStatus.Approved, VerifiedAtUtc = DateTime.UtcNow.AddDays(-1),
            VerificationExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };

        private OperationalNeed Need(string description) => new()
        {
            Description = $"{description} {marker}", Place = "Oslo", CreatedById = AdminId, CreatedAtUtc = DateTime.UtcNow
        };

        private ResourceRequest Request(MapSubmission resource, OperationalNeed need) => new()
        {
            ResourceId = resource.Id, NeedId = need.Id, CreatedById = AdminId,
            CreatedAtUtc = DateTime.UtcNow, Reason = $"Privat forespørsel {resource.OwnerId}"
        };

        public async Task DisposeAsync()
        {
            if (app != null) await app.DisposeAsync();
            await using var db = Db();
            await db.WorkflowAudits.Where(a => a.ActorId == AdminId || a.ActorId == OwnerId).ExecuteDeleteAsync();
            await db.ResourceRequests.Where(r => r.CreatedById == AdminId).ExecuteDeleteAsync();
            await db.OperationalNeeds.Where(n => n.CreatedById == AdminId).ExecuteDeleteAsync();
            await db.MapSubmissions.Where(r => r.OwnerId == OwnerId || r.OwnerId == OtherId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == OwnerId || u.Id == OtherId || u.Id == AdminId).ExecuteDeleteAsync();
        }
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Actor", out var actor))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>();
            if (actor != "missing-id") claims.Add(new Claim(ClaimTypes.NameIdentifier, actor.ToString()));
            if (Request.Headers.TryGetValue("X-Test-Role", out var role))
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
