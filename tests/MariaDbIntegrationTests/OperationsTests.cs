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
    [InlineData(AppRoles.Ressursleverandor)]
    [InlineData(AppRoles.Kriseledelse)]
    public async Task ResourcesOverlayRendersExistingPermissionAwareViewsWithoutApplicationChrome(string role)
    {
        using var client = site.Client(role == AppRoles.Kriseledelse ? site.LeaderId : site.OwnerId, role);
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
        Assert.DoesNotContain("target=\"_blank\"", html);
        Assert.DoesNotContain("ny fane", html);
        Assert.DoesNotContain($"href=\"/Home/Edit/{site.OtherResource.Id}\"", html);
        Assert.DoesNotContain($"href=\"/Home/Delete/{site.OtherResource.Id}\"", html);
        if (role == AppRoles.Kriseledelse)
        {
            Assert.DoesNotContain($"href=\"/Home/Edit/{site.OwnResource.Id}\"", html);
            Assert.DoesNotContain($"href=\"/Home/Delete/{site.OwnResource.Id}\"", html);
            Assert.Contains("action=\"/Home/ResetMap\"", html);
            Assert.Contains("name=\"__RequestVerificationToken\"", html);
        }
        else
        {
            Assert.Contains($"href=\"/Home/Edit/{site.OwnResource.Id}\"", html);
            Assert.Contains($"href=\"/Home/Delete/{site.OwnResource.Id}\"", html);
            Assert.Contains("class=\"resource-delete-dialog\"", html);
            Assert.Contains("data-resource-delete=", html);
            Assert.Contains("name=\"__RequestVerificationToken\"", html);
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
        Assert.Equal(AppRoles.All.OrderBy(role => role), authorization.Roles!.Split(',').OrderBy(role => role));
        Assert.DoesNotContain("Admin", authorization.Roles!.Split(','));
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
        using var client = site.Client(site.OwnerId, AppRoles.Ressursleverandor);
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
        Assert.DoesNotContain("action=\"/Operations/NeedStatus\"", html);
        Assert.Contains("href=\"/Operations\"", html);
        Assert.DoesNotContain("/Operations/Requests", html);
        Assert.DoesNotContain("/Operations/Queue", html);
        Assert.DoesNotContain("/Operations/Needs", html);
    }

    [Fact]
    public async Task KriseledelseSeesGlobalOverviewButCannotRegisterNeedsOrVerify()
    {
        using var client = site.Client(site.LeaderId, AppRoles.Kriseledelse);
        var response = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        foreach (var text in new[] { site.OwnResource.ResourceName, site.OtherResource.ResourceName,
                     site.OwnNeed.Description, site.OtherNeed.Description, site.UnrelatedNeed.Description,
                     site.OwnRequest.Reason, site.OtherRequest.Reason, site.Audit.Reason })
            Assert.Contains(text, html);
        Assert.Contains("href=\"#resources\"", html);
        Assert.Contains("href=\"#needs\"", html);
        Assert.Contains("id=\"resources\"", html);
        Assert.Contains("id=\"needs\"", html);
        Assert.Contains("action=\"/Operations/NeedStatus\"", html);
        Assert.Contains("action=\"/Operations/Prioritize\"", html);
        Assert.DoesNotContain("action=\"/Operations/CreateNeed\"", html);
        Assert.DoesNotContain("action=\"/Operations/Verify\"", html);
        Assert.DoesNotContain("action=\"/Operations/Allocate\"", html);
        Assert.DoesNotContain("action=\"/Operations/Respond\"", html);
        Assert.Contains("<div class=\"audit-scroll\" tabindex=\"0\" role=\"region\" aria-label=\"Beslutningslogg\">", html);
    }

    [Fact]
    public async Task OffentligAktorRegistersNeedsAndSeesNoResourcesRequestsOrAudit()
    {
        using var client = site.Client(site.PublicId, AppRoles.OffentligAktor);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Operations"));
        Assert.Contains("action=\"/Operations/CreateNeed\"", html);
        Assert.Contains(site.OwnNeed.Description, html);
        Assert.DoesNotContain(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain(site.OwnRequest.Reason, html);
        Assert.DoesNotContain(site.Audit.Reason, html);
        Assert.DoesNotContain("action=\"/Operations/NeedStatus\"", html);
    }

    [Fact]
    public async Task VerifierSeesQueueWithVerifyFormsAndNoAllocationOrNeedControls()
    {
        using var client = site.Client(site.VerifierId, AppRoles.Verifiseringsansvarlig);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Operations"));
        Assert.Contains(site.PendingResource.ResourceName, html);
        Assert.Contains("action=\"/Operations/Verify\"", html);
        Assert.DoesNotContain(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain(site.OwnRequest.Reason, html);
        Assert.DoesNotContain("action=\"/Operations/Allocate\"", html);
        Assert.DoesNotContain("action=\"/Operations/CreateNeed\"", html);
        Assert.DoesNotContain(site.Audit.Reason, html);
    }

    [Fact]
    public async Task DispatchSeesOnlyVerifiedAvailableResources()
    {
        using var client = site.Client(site.DispatchId, AppRoles.DispatchTeam);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Operations"));
        Assert.Contains(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain(site.PendingResource.ResourceName, html);
        Assert.DoesNotContain("action=\"/Operations/Verify\"", html);
        Assert.DoesNotContain(site.Audit.Reason, html);
        var overlay = WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Resources?overlay=true"));
        Assert.Contains(site.OwnResource.ResourceName, overlay);
        Assert.DoesNotContain(site.PendingResource.ResourceName, overlay);
    }

    [Fact]
    public async Task UnrelatedProviderGetsMeaningfulEmptyStateWithoutDisclosure()
    {
        using var client = site.Client("unrelated-provider", AppRoles.Ressursleverandor);
        var response = await client.GetAsync("/Operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Du har ingen innkommende forespørsler.", html);
        Assert.Contains("Ingen ressurser å vise.", html);
        Assert.Contains("Ingen behov å vise.", html);
        Assert.DoesNotContain(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain(site.OtherResource.ResourceName, html);
        Assert.DoesNotContain(site.OwnNeed.Description, html);
        Assert.DoesNotContain(site.OtherNeed.Description, html);
        Assert.DoesNotContain(site.Audit.Reason, html);
        Assert.DoesNotContain("action=\"/Operations/Respond\"", html);
    }

    [Theory]
    [InlineData(AppRoles.OffentligAktor)]
    [InlineData(AppRoles.Ressursleverandor)]
    [InlineData(AppRoles.DispatchTeam)]
    [InlineData(AppRoles.Kriseledelse)]
    [InlineData(AppRoles.Verifiseringsansvarlig)]
    public async Task HomeCardsLinkToAuthorizedOperationsRoutes(string role)
    {
        using var client = site.Client(site.IdFor(role), role);
        var html = await client.GetStringAsync("/");
        var guide = Regex.Match(html, "<ol class=\"home-steps\">[\\s\\S]*?</ol>").Value;
        var cards = Regex.Matches(guide, "<a\\b[^>]*>")
            .Select(m => m.Value).Where(a => a.Contains("href=\"/Operations")).ToList();
        Assert.Single(cards);
        Assert.DoesNotContain("/Operations/Requests", html);
        Assert.DoesNotContain("/Operations/Queue", html);
        Assert.DoesNotContain("/Operations/Needs", html);
        Assert.Contains(cards, a => a.Contains("href=\"/Operations\""));
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
    public async Task KriseledelseResourceWorkspaceHasOneGlobalResetEvenWhenListIsEmpty(string route, bool empty)
    {
        using var client = site.Client(site.LeaderId, AppRoles.Kriseledelse);
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
        using var client = site.Client(site.OwnerId, AppRoles.Ressursleverandor);
        var html = await client.GetStringAsync("/Home/Resources");
        Assert.Contains(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain("action=\"/Home/ResetMap\"", html);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("roleless", null, HttpStatusCode.Forbidden)]
    [InlineData("unknown-role", "Observer", HttpStatusCode.Forbidden)]
    [InlineData("legacy-admin", "Admin", HttpStatusCode.Forbidden)]
    [InlineData("legacy-user", "Bruker", HttpStatusCode.Forbidden)]
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
        using var client = site.Client("", AppRoles.Kriseledelse);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Operations")).StatusCode);
        Assert.DoesNotContain("href=\"/Operations", await client.GetStringAsync("/"));
    }

    [Theory]
    [InlineData("Verify", AppRoles.Verifiseringsansvarlig)]
    [InlineData("CreateNeed", AppRoles.OffentligAktor)]
    [InlineData("NeedStatus", AppRoles.Kriseledelse)]
    [InlineData("Prioritize", AppRoles.Kriseledelse)]
    [InlineData("CreateRequest", AppRoles.DispatchTeam)]
    [InlineData("Allocate", AppRoles.DispatchTeam)]
    [InlineData("Cancel", AppRoles.DispatchTeam)]
    [InlineData("Assignment", AppRoles.DispatchTeam)]
    [InlineData("Respond", AppRoles.Ressursleverandor)]
    [InlineData("Availability", AppRoles.Ressursleverandor)]
    public async Task ActionsAreOpenOnlyToTheirOwnRoleAndRequireAntiforgery(string action, string allowed)
    {
        foreach (var role in AppRoles.All.Append("Admin").Where(role => role != allowed))
        {
            using var wrong = site.Client(site.IdFor(role), role);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await wrong.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
        }
        using var right = site.Client(site.IdFor(allowed), allowed);
        using var anonymous = site.Client();
        using var roleless = site.Client("roleless");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await right.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await roleless.PostAsync($"/Operations/{action}", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task TamperedOwnerActionsCannotChangeAnotherProvidersResourceOrRequest()
    {
        using var provider = site.Client(site.OwnerId, AppRoles.Ressursleverandor);
        await using var db = site.Db();
        var auditsBefore = await db.WorkflowAudits.CountAsync(a => a.ResourceId == site.OtherResource.Id);
        foreach (var action in new[] { "Respond", "Availability" })
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
    public async Task OwnerAcceptsDispatchAllocatesAndProgressesAssignmentThroughRealRoutes()
    {
        using var owner = site.Client(site.OwnerId, AppRoles.Ressursleverandor);
        using var dispatch = site.Client(site.DispatchId, AppRoles.DispatchTeam);
        using var leader = site.Client(site.LeaderId, AppRoles.Kriseledelse);
        await using var db = site.Db();
        var request = await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == site.FlowRequest.Id);
        var early = await Post(dispatch, "Allocate", new()
        {
            ["id"] = request.Id.ToString(), ["token"] = request.ConcurrencyToken, ["reason"] = "For tidlig"
        });
        Assert.Equal(HttpStatusCode.Redirect, early.StatusCode);
        Assert.Equal(ResourceRequestStatus.Pending,
            (await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);

        foreach (var step in new[]
                 {
                     (Client: owner, Action: "Respond", Status: ResourceRequestStatus.Accepted),
                     (Client: dispatch, Action: "Allocate", Status: ResourceRequestStatus.Allocated)
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
        var assignment = await db.ResourceAssignments.AsNoTracking().SingleAsync(a => a.RequestId == request.Id);
        Assert.Equal(AssignmentStatus.Confirmed, assignment.Status);
        var skip = await Post(dispatch, "Assignment", new()
        {
            ["id"] = assignment.Id.ToString(), ["token"] = assignment.ConcurrencyToken,
            ["status"] = nameof(AssignmentStatus.Completed), ["reason"] = "Hopper over"
        });
        Assert.Equal(HttpStatusCode.Redirect, skip.StatusCode);
        Assert.Equal(AssignmentStatus.Confirmed,
            (await db.ResourceAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id)).Status);
        foreach (var next in new[] { AssignmentStatus.EnRoute, AssignmentStatus.Arrived, AssignmentStatus.Completed })
        {
            assignment = await db.ResourceAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
            var response = await Post(dispatch, "Assignment", new()
            {
                ["id"] = assignment.Id.ToString(), ["token"] = assignment.ConcurrencyToken,
                ["status"] = next.ToString(), ["reason"] = "Fremdrift i rutetest"
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(next, (await db.ResourceAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id)).Status);
        }
        var resource = await db.MapSubmissions.AsNoTracking().SingleAsync(r => r.Id == site.FlowResource.Id);
        Assert.Equal(ResourceAvailability.Returned, resource.Availability);
        Assert.Equal(ResourceVerificationStatus.Pending, resource.VerificationStatus);
        Assert.Equal(ResourceRequestStatus.Returned,
            (await db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        var need = await db.OperationalNeeds.AsNoTracking().SingleAsync(n => n.Id == site.FlowNeed.Id);
        Assert.Equal(NeedStatus.Assigned, need.Status);
        var closed = await Post(leader, "NeedStatus", new()
        {
            ["id"] = need.Id.ToString(), ["token"] = need.ConcurrencyToken,
            ["status"] = nameof(NeedStatus.Completed), ["reason"] = "Behovet er løst"
        });
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        Assert.Equal(NeedStatus.Completed,
            (await db.OperationalNeeds.AsNoTracking().SingleAsync(n => n.Id == need.Id)).Status);
    }

    [Fact]
    public async Task ProviderDashboardShowsOwnResourcesAndIncomingRequestsOnly()
    {
        using var client = site.Client(site.OwnerId, AppRoles.Ressursleverandor);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Dashboard"));
        Assert.Contains(site.OwnResource.ResourceName, html);
        Assert.Contains(site.OwnRequest.Reason, html);
        Assert.Contains("action=\"/Operations/Respond\"", html);
        Assert.DoesNotContain(site.OtherResource.ResourceName, html);
        Assert.DoesNotContain(site.OtherRequest.Reason, html);
        Assert.DoesNotContain("action=\"/Operations/Verify\"", html);
        Assert.DoesNotContain("action=\"/Operations/CreateRequest\"", html);
        Assert.DoesNotContain(site.Audit.Reason, html);
    }

    [Fact]
    public async Task VerifierDashboardListsOnlyResourcesWaitingForVerification()
    {
        using var client = site.Client(site.VerifierId, AppRoles.Verifiseringsansvarlig);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Dashboard"));
        Assert.Contains(site.PendingResource.ResourceName, html);
        Assert.Contains("action=\"/Operations/Verify\"", html);
        Assert.Contains("Begrunnelse (kreves ved avvisning)", html);
        Assert.DoesNotContain(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain("action=\"/Operations/CreateRequest\"", html);
        Assert.DoesNotContain("action=\"/Operations/Respond\"", html);
    }

    [Fact]
    public async Task DispatchDashboardOffersVerifiedResourcesForOpenNeeds()
    {
        using var client = site.Client(site.DispatchId, AppRoles.DispatchTeam);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Dashboard"));
        Assert.Contains(site.OwnNeed.Description, html);
        Assert.Contains("action=\"/Operations/CreateRequest\"", html);
        Assert.Contains(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain(site.PendingResource.ResourceName, html);
        Assert.DoesNotContain("action=\"/Operations/Verify\"", html);
        Assert.DoesNotContain("action=\"/Operations/Respond\"", html);
        Assert.DoesNotContain("action=\"/Operations/CreateNeed\"", html);
    }

    [Fact]
    public async Task LeaderDashboardShowsNeedsAndUserManagementWithoutOperationalForms()
    {
        using var client = site.Client(site.LeaderId, AppRoles.Kriseledelse);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Dashboard"));
        Assert.Contains(site.OwnNeed.Description, html);
        Assert.Contains("href=\"/Admin\"", html);
        Assert.DoesNotContain("action=\"/Operations/Verify\"", html);
        Assert.DoesNotContain("action=\"/Operations/CreateRequest\"", html);
        Assert.DoesNotContain("action=\"/Operations/CreateNeed\"", html);
    }

    [Fact]
    public async Task PublicActorDashboardShowsOwnNeedsAndTheRegistrationForm()
    {
        using var client = site.Client(site.PublicId, AppRoles.OffentligAktor);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Dashboard"));
        Assert.Contains("action=\"/Operations/CreateNeed\"", html);
        Assert.Contains(site.OwnNeed.Description, html);
        Assert.DoesNotContain(site.OwnResource.ResourceName, html);
        Assert.DoesNotContain("action=\"/Operations/Respond\"", html);
        Assert.DoesNotContain("action=\"/Operations/CreateRequest\"", html);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("roleless", null, HttpStatusCode.Forbidden)]
    [InlineData("legacy-admin", "Admin", HttpStatusCode.Forbidden)]
    public async Task DashboardRequiresADomainRole(string? id, string? role, HttpStatusCode status)
    {
        using var client = site.Client(id, role);
        Assert.Equal(status, (await client.GetAsync("/Home/Dashboard")).StatusCode);
    }

    [Theory]
    [InlineData("/Home/Dashboard", "/Home/Dashboard")]
    [InlineData("https://evil.example/", "/Operations")]
    [InlineData("//evil.example/", "/Operations")]
    public async Task ActionsReturnToTheDashboardOnlyForLocalUrls(string returnUrl, string expected)
    {
        using var provider = site.Client(site.OwnerId, AppRoles.Ressursleverandor);
        var response = await Post(provider, "Respond", new()
        {
            ["id"] = site.OtherRequest.Id.ToString(), ["token"] = site.OtherRequest.ConcurrencyToken,
            ["reason"] = "Feil eier", ["accept"] = "true", ["returnUrl"] = returnUrl
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
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
        public string DispatchId => $"operations-dispatch-{marker}";
        public string LeaderId => $"operations-leader-{marker}";
        public string VerifierId => $"operations-verifier-{marker}";
        public string PublicId => $"operations-public-{marker}";
        private string[] ActorIds => [OwnerId, OtherId, DispatchId, LeaderId, VerifierId, PublicId];
        public string IdFor(string role) => role switch
        {
            AppRoles.Ressursleverandor => OwnerId,
            AppRoles.DispatchTeam => DispatchId,
            AppRoles.Kriseledelse => LeaderId,
            AppRoles.Verifiseringsansvarlig => VerifierId,
            AppRoles.OffentligAktor => PublicId,
            _ => $"operations-legacy-{marker}"
        };
        public MapSubmission OwnResource { get; private set; } = null!;
        public MapSubmission OtherResource { get; private set; } = null!;
        public MapSubmission PendingResource { get; private set; } = null!;
        public MapSubmission FlowResource { get; private set; } = null!;
        public OperationalNeed FlowNeed { get; private set; } = null!;
        public ResourceRequest FlowRequest { get; private set; } = null!;
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
                db.Users.AddRange(ActorIds.Select(id => new IdentityUser { Id = id, UserName = $"{id}@example.test" }));
                OwnResource = Resource(OwnerId);
                OtherResource = Resource(OtherId);
                PendingResource = Resource(OtherId, "Venter");
                PendingResource.VerificationStatus = ResourceVerificationStatus.Pending;
                PendingResource.VerifiedAtUtc = null;
                PendingResource.VerificationExpiresAtUtc = null;
                OwnNeed = Need("Eiers behov");
                OtherNeed = Need("Annen eiers behov");
                UnrelatedNeed = Need("Behov uten forespørsler");
                FlowResource = Resource(OwnerId, "Flyt");
                FlowNeed = Need("Behov i arbeidsflyt");
                db.MapSubmissions.AddRange(OwnResource, OtherResource, PendingResource, FlowResource);
                db.OperationalNeeds.AddRange(OwnNeed, OtherNeed, UnrelatedNeed, FlowNeed);
                await db.SaveChangesAsync();
                OwnRequest = Request(OwnResource, OwnNeed);
                OtherRequest = Request(OtherResource, OtherNeed);
                Audit = new WorkflowAudit
                {
                    ActorId = LeaderId, Action = "Test", Reason = $"Intern beslutning {marker}",
                    OccurredAtUtc = DateTime.UtcNow, ResourceId = OtherResource.Id
                };
                FlowRequest = Request(FlowResource, FlowNeed);
                db.ResourceRequests.AddRange(OwnRequest, OtherRequest, FlowRequest);
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
            builder.Services.AddScoped<DashboardService>();
            builder.Services.AddSingleton<IPlaceGeocoder>(new PlaceGeocoder(new HttpClient(), Options.Create(new GeocodingOptions())));
            app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            await app.StartAsync();
        }

        private MapSubmission Resource(string owner, string prefix = "Ressurs") => new()
        {
            OwnerId = owner, ResourceName = $"{prefix} {owner}", ResourceType = "Utstyr", LocationName = "Oslo",
            Latitude = 59.91, Longitude = 10.75, CreatedAtUtc = DateTime.UtcNow,
            VerificationStatus = ResourceVerificationStatus.Approved, VerifiedAtUtc = DateTime.UtcNow.AddDays(-1),
            VerificationExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };

        private OperationalNeed Need(string description) => new()
        {
            Description = $"{description} {marker}", Place = "Oslo", CreatedById = PublicId, CreatedAtUtc = DateTime.UtcNow, Status = NeedStatus.InProgress
        };

        private ResourceRequest Request(MapSubmission resource, OperationalNeed need) => new()
        {
            ResourceId = resource.Id, NeedId = need.Id, CreatedById = DispatchId,
            CreatedAtUtc = DateTime.UtcNow, Reason = $"Privat forespørsel {resource.OwnerId}"
        };

        public async Task DisposeAsync()
        {
            if (app != null) await app.DisposeAsync();
            await using var db = Db();
            var actors = ActorIds;
            await db.WorkflowAudits.Where(a => actors.Contains(a.ActorId)).ExecuteDeleteAsync();
            await db.ResourceAssignments.Where(a => a.CreatedById == DispatchId).ExecuteDeleteAsync();
            await db.ResourceRequests.Where(r => r.CreatedById == DispatchId).ExecuteDeleteAsync();
            await db.OperationalNeeds.Where(n => n.CreatedById == PublicId).ExecuteDeleteAsync();
            await db.MapSubmissions.Where(r => r.OwnerId == OwnerId || r.OwnerId == OtherId).ExecuteDeleteAsync();
            await db.Users.Where(u => actors.Contains(u.Id)).ExecuteDeleteAsync();
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
