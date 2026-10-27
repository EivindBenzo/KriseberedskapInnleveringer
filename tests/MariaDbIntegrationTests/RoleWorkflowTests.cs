using System.Security.Claims;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MariaDbIntegrationTests;

public class RoleWorkflowTests
{
    [Fact]
    public void RoleModelHasExactlyTheFiveDomainRoles()
    {
        Assert.Equal(
            new[] { "DispatchTeam", "Kriseledelse", "OffentligAktor", "Ressursleverandor", "Verifiseringsansvarlig" },
            AppRoles.All.OrderBy(role => role));
        Assert.DoesNotContain("Admin", AppRoles.All);
        Assert.DoesNotContain("Bruker", AppRoles.All);
        Assert.Equal(AppRoles.All.OrderBy(role => role), AppRoles.Any.Split(',').OrderBy(role => role));
        Assert.Equal(AppRoles.All.OrderBy(role => role), AppRoles.SelfService.OrderBy(role => role));
    }

    [Theory]
    [InlineData(NeedStatus.Open, NeedStatus.InProgress, true)]
    [InlineData(NeedStatus.InProgress, NeedStatus.Assigned, false)]
    [InlineData(NeedStatus.Assigned, NeedStatus.Completed, true)]
    [InlineData(NeedStatus.Open, NeedStatus.Cancelled, true)]
    [InlineData(NeedStatus.Open, NeedStatus.Completed, false)]
    [InlineData(NeedStatus.Open, NeedStatus.Assigned, false)]
    [InlineData(NeedStatus.InProgress, NeedStatus.Completed, false)]
    [InlineData(NeedStatus.InProgress, NeedStatus.Open, false)]
    [InlineData(NeedStatus.Assigned, NeedStatus.InProgress, false)]
    [InlineData(NeedStatus.Completed, NeedStatus.Open, false)]
    [InlineData(NeedStatus.Cancelled, NeedStatus.InProgress, false)]
    public void NeedTransitionsFollowNyVurderesTildeltLost(NeedStatus from, NeedStatus to, bool allowed) =>
        Assert.Equal(allowed, WorkflowTransitions.CanMoveNeed(from, to));

    [Theory]
    [InlineData(AssignmentStatus.Confirmed, AssignmentStatus.EnRoute, true)]
    [InlineData(AssignmentStatus.EnRoute, AssignmentStatus.Arrived, true)]
    [InlineData(AssignmentStatus.Arrived, AssignmentStatus.Completed, true)]
    [InlineData(AssignmentStatus.Confirmed, AssignmentStatus.Arrived, false)]
    [InlineData(AssignmentStatus.Confirmed, AssignmentStatus.Completed, false)]
    [InlineData(AssignmentStatus.Arrived, AssignmentStatus.EnRoute, false)]
    [InlineData(AssignmentStatus.Completed, AssignmentStatus.Confirmed, false)]
    [InlineData(AssignmentStatus.Completed, AssignmentStatus.Completed, false)]
    public void AssignmentProgressOnlyMovesForwardOneStep(AssignmentStatus from, AssignmentStatus to, bool allowed) =>
        Assert.Equal(allowed, WorkflowTransitions.CanMoveAssignment(from, to));

    [Fact]
    public async Task EveryWorkflowMutationRejectsRolesThatDoNotOwnIt()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        var requestId = await test.Request();
        var resourceToken = test.ResourceToken();
        var needToken = test.NeedToken();
        var requestToken = test.RequestToken(requestId);
        var others = (string role) => AppRoles.All.Where(r => r != role).Select(r => WorkflowTests.Principal($"actor-{r}", r)).ToList();

        foreach (var actor in others(AppRoles.OffentligAktor))
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.CreateNeedAsync("Behov", "Oslo", NeedPriority.Normal, actor));
        foreach (var actor in others(AppRoles.Verifiseringsansvarlig))
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, true, resourceToken, actor, "Godkjent"));
        foreach (var actor in others(AppRoles.DispatchTeam))
        {
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.CreateRequestAsync(test.NeedId, test.ResourceId, resourceToken, needToken, actor, "Behov"));
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.CancelAsync(requestId, requestToken, actor, "Avbryt"));
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId, requestToken, actor, "Tildel"));
        }
        foreach (var actor in others(AppRoles.Kriseledelse))
        {
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId, NeedStatus.Cancelled, needToken, actor, "Avslutt"));
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedPriorityAsync(test.NeedId, NeedPriority.Critical, needToken, actor, "Prioriter"));
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ResetMapAsync(actor, true));
        }
        var legacyAdmin = WorkflowTests.Principal("legacy", "Admin");
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, true, resourceToken, legacyAdmin, "Godkjent"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.CreateRequestAsync(test.NeedId, test.ResourceId, resourceToken, needToken, legacyAdmin, "Behov"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId, NeedStatus.Cancelled, needToken, legacyAdmin, "Avslutt"));
        foreach (var actor in others(AppRoles.Ressursleverandor))
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true, requestToken, actor, "Aksepter"));
        Assert.Equal(ResourceRequestStatus.Pending, (await test.Db.ResourceRequests.AsNoTracking().SingleAsync(r => r.Id == requestId)).Status);
    }

    [Fact]
    public async Task RejectionRequiresReasonAndUnverifiedResourcesCannotBeRequestedOrAllocated()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, false, test.ResourceToken(), test.Verifier, null));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, false, test.ResourceToken(), test.Verifier, "  "));
        Assert.Equal(ResourceVerificationStatus.Approved, (await test.Resource()).VerificationStatus);
        await test.Service.VerifyAsync(test.ResourceId, false, test.ResourceToken(), test.Verifier, "Mangler dokumentasjon");
        var rejected = await test.Resource();
        Assert.Equal(ResourceVerificationStatus.Rejected, rejected.VerificationStatus);
        Assert.Equal("Avvist", WorkflowTransitions.Lifecycle(rejected, DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        Assert.Empty(await test.Db.ResourceRequests.Where(r => r.ResourceId == test.ResourceId).ToListAsync());

        await test.Service.VerifyAsync(test.ResourceId, true, test.ResourceToken(), test.Verifier, null);
        var requestId = await test.Request();
        await test.Service.RespondAsync(requestId, true, test.RequestToken(requestId), test.Owner, "Reserver");
        var resource = await test.Resource();
        resource.VerificationStatus = ResourceVerificationStatus.Pending;
        resource.VerifiedAtUtc = null;
        resource.VerificationExpiresAtUtc = null;
        await test.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId, test.RequestToken(requestId), test.Dispatch, "Uverifisert"));
        Assert.False(await test.Db.ResourceAssignments.AnyAsync(a => a.ResourceId == test.ResourceId));
    }

    [Fact]
    public async Task ReturnedResourceIsReleasedOrRejectedAfterNewVerification()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        var assignmentId = await test.Allocated();
        foreach (var step in new[] { AssignmentStatus.EnRoute, AssignmentStatus.Arrived })
            await test.Service.UpdateAssignmentAsync(assignmentId, step, test.AssignmentToken(assignmentId), test.Dispatch, "Fremdrift");
        Assert.Equal(ResourceAvailability.InUse, (await test.Resource()).Availability);
        Assert.Equal("I bruk", WorkflowTransitions.Lifecycle(await test.Resource(), DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, true, test.ResourceToken(), test.Verifier, "Under bruk"));
        await test.Service.UpdateAssignmentAsync(assignmentId, AssignmentStatus.Completed, test.AssignmentToken(assignmentId), test.Dispatch, "Ferdig");
        var returned = await test.Resource();
        Assert.Equal("Returnert", WorkflowTransitions.Lifecycle(returned, DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        await test.Service.VerifyAsync(test.ResourceId, false, test.ResourceToken(), test.Verifier, "Skadet i bruk");
        Assert.Equal(ResourceAvailability.Unavailable, (await test.Resource()).Availability);
        Assert.Equal("Avvist", WorkflowTransitions.Lifecycle(await test.Resource(), DateTime.UtcNow));
    }

    [Fact]
    public async Task ApprovedReturnedResourceBecomesAvailableAgain()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        var assignmentId = await test.Allocated();
        foreach (var step in new[] { AssignmentStatus.EnRoute, AssignmentStatus.Arrived, AssignmentStatus.Completed })
            await test.Service.UpdateAssignmentAsync(assignmentId, step, test.AssignmentToken(assignmentId), test.Dispatch, "Fremdrift");
        await test.Service.VerifyAsync(test.ResourceId, true, test.ResourceToken(), test.Verifier, "Kontrollert etter bruk");
        var resource = await test.Resource();
        Assert.Equal(ResourceAvailability.Available, resource.Availability);
        Assert.True(WorkflowEligibility.IsEligible(resource, DateTime.UtcNow));
        Assert.False(await test.Db.ResourceAssignments.AnyAsync(a => a.ResourceId == test.ResourceId && a.ActiveResourceId != null));
    }

    [Fact]
    public async Task IllegalNeedAndAssignmentJumpsAreRejected()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId, NeedStatus.Assigned, test.NeedToken(), test.Leader, "Hopp"));
        var assignmentId = await test.Allocated();
        foreach (var jump in new[] { AssignmentStatus.Arrived, AssignmentStatus.Completed, AssignmentStatus.Confirmed })
            await Assert.ThrowsAsync<WorkflowException>(() => test.Service.UpdateAssignmentAsync(assignmentId, jump, test.AssignmentToken(assignmentId), test.Dispatch, "Hopp"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId, NeedStatus.InProgress, test.NeedToken(), test.Leader, "Tilbake"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.UpdateAssignmentAsync(assignmentId, AssignmentStatus.EnRoute, test.AssignmentToken(assignmentId), test.Leader, "Feil rolle"));
        Assert.Equal(AssignmentStatus.Confirmed, (await test.Db.ResourceAssignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId)).Status);
    }

    [Fact]
    public async Task ResourceCannotBeAssignedTwiceSequentially()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        await test.Allocated();
        var secondNeed = await test.Service.CreateNeedAsync("Andre behov", "Bergen", NeedPriority.High, test.Public);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.CreateRequestAsync(secondNeed, test.ResourceId, test.ResourceToken(),
            test.Db.OperationalNeeds.Single(n => n.Id == secondNeed).ConcurrencyToken, test.Dispatch, "Samme ressurs"));
        Assert.Equal(1, await test.Db.ResourceAssignments.CountAsync(a => a.ResourceId == test.ResourceId));
    }

    [Fact]
    public async Task ConcurrentAllocationOfTheSameResourceCreatesOneAssignmentAndReportsConflict()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        var first = await test.Request();
        var secondNeed = await test.Service.CreateNeedAsync("Andre behov", "Bergen", NeedPriority.Critical, test.Public);
        var second = await test.Service.CreateRequestAsync(secondNeed, test.ResourceId, test.ResourceToken(),
            test.Db.OperationalNeeds.Single(n => n.Id == secondNeed).ConcurrencyToken, test.Dispatch, "Alternativt oppdrag");
        await test.Service.RespondAsync(first, true, test.RequestToken(first), test.Owner, "Reserver");
        // Simulate a legacy or racing row: the second request is also accepted against the same reserved resource.
        await test.Db.ResourceRequests.Where(r => r.Id == second).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ResourceRequestStatus.Accepted));
        test.Db.ChangeTracker.Clear();
        var firstToken = test.RequestToken(first);
        var secondToken = test.RequestToken(second);

        async Task<string?> Allocate(int id, string token)
        {
            await using var db = new AppDbContext(test.Options);
            try
            {
                await new WorkflowService(db, Microsoft.Extensions.Options.Options.Create(new WorkflowOptions()))
                    .AllocateAsync(id, token, test.Dispatch, "Samtidig tildeling");
                return null;
            }
            catch (WorkflowException exception) { return exception.Message; }
        }

        var results = await Task.WhenAll(Allocate(first, firstToken), Allocate(second, secondToken));
        Assert.Single(results, message => message is null);
        Assert.All(results.Where(message => message is not null), message => Assert.False(string.IsNullOrWhiteSpace(message)));
        test.Db.ChangeTracker.Clear();
        Assert.Equal(1, await test.Db.ResourceAssignments.CountAsync(a => a.ResourceId == test.ResourceId && a.ActiveResourceId != null));
        Assert.Equal(1, await test.Db.ResourceRequests.CountAsync(r => r.ResourceId == test.ResourceId && r.Status == ResourceRequestStatus.Allocated));
        Assert.Equal(ResourceAvailability.Allocated, (await test.Resource()).Availability);
    }

    [Fact]
    public async Task DatabaseRefusesTwoActiveAssignmentsForOneResource()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        await test.Allocated();
        var secondNeed = await test.Service.CreateNeedAsync("Andre behov", "Bergen", NeedPriority.High, test.Public);
        await using var other = new AppDbContext(test.Options);
        var request = new ResourceRequest { NeedId = secondNeed, ResourceId = test.ResourceId, CreatedById = test.DispatchId, Reason = "Direkte", Status = ResourceRequestStatus.Cancelled, CreatedAtUtc = DateTime.UtcNow };
        other.ResourceRequests.Add(request);
        await other.SaveChangesAsync();
        other.ResourceAssignments.Add(new ResourceAssignment
        {
            RequestId = request.Id, ResourceId = test.ResourceId, NeedId = secondNeed, CreatedById = test.DispatchId,
            Status = AssignmentStatus.Confirmed, ActiveResourceId = test.ResourceId, CreatedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Theory]
    [InlineData(AppRoles.Ressursleverandor, null, null, "")]
    [InlineData(AppRoles.Ressursleverandor, "Drone", null, "")]
    [InlineData(AppRoles.Ressursleverandor, null, "drone", "   ")]
    [InlineData(AppRoles.Ressursleverandor, null, null, "Oslo")]
    [InlineData(AppRoles.Kriseledelse, "Kjøretøy", null, "")]
    [InlineData(AppRoles.DispatchTeam, null, null, "")]
    [InlineData(AppRoles.DispatchTeam, "Drone", null, " oslo ")]
    public async Task ListAndMapShowTheSameResourcesForTheSameFilters(string role, string? type, string? query, string city)
    {
        await using var test = await WorkflowTests.Scenario.Create();
        var created = new List<MapSubmission>
        {
            Make(test, "Konsistens drone Oslo", "Drone", "Gate 1, 0456 Oslo", true, ResourceAvailability.Available),
            Make(test, "Konsistens bil Bergen", "Kjøretøy", "Bergen", true, ResourceAvailability.Available),
            Make(test, "Konsistens drone ledig", "Drone", "Oslo", true, ResourceAvailability.Unavailable),
            Make(test, "Konsistens drone ventende", "Drone", "Oslo", false, ResourceAvailability.Available)
        };
        test.Db.MapSubmissions.AddRange(created);
        await test.Db.SaveChangesAsync();
        var user = WorkflowTests.Principal(role == AppRoles.Ressursleverandor ? test.OwnerId : $"viewer-{role}", role);
        var controller = new HomeController(test.Db, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        await controller.Map(type, query, city, false, false);
        var onMap = OnMap(controller).Select(r => r.Id).Order().ToList();
        var listing = Assert.IsType<DataPageViewModel>(
            Assert.IsType<ViewResult>(await controller.Resources(type, query, false, false, false, city)).Model);
        var listed = listing.MapSubmissions.Select(r => r.Id).Order().ToList();
        Assert.Equal(listed, onMap);
        Assert.NotEmpty(listed);
        if (string.IsNullOrWhiteSpace(city) && type is null && query is null && role != AppRoles.DispatchTeam)
            Assert.All(created, resource => Assert.Contains(resource.Id, listed));
        if (role == AppRoles.DispatchTeam)
        {
            Assert.DoesNotContain(created[2].Id, listed);
            Assert.DoesNotContain(created[3].Id, listed);
        }
        Assert.Equal(listed.Count, listing.MapSubmissions.Count);
    }

    [Fact]
    public async Task EmptyCityMeansNoCityFilterAndDefaultsDoNotHideEverything()
    {
        await using var test = await WorkflowTests.Scenario.Create();
        var user = WorkflowTests.Principal(test.OwnerId, AppRoles.Ressursleverandor);
        var controller = new HomeController(test.Db, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        await controller.Map();
        var unfiltered = OnMap(controller);
        Assert.Contains(unfiltered, r => r.Id == test.ResourceId);
        foreach (var city in new[] { "", "   ", null })
        {
            await controller.Map(null, null, city, false, false);
            Assert.Equal(unfiltered.Count, OnMap(controller).Count);
        }
        await controller.Map(null, null, "Finnes ikke", false, false);
        Assert.Empty(OnMap(controller));
    }

    private static List<MapSubmission> OnMap(HomeController controller) =>
        ((IEnumerable<MapSubmission>)controller.ViewBag.Resources).ToList();

    private static MapSubmission Make(WorkflowTests.Scenario test, string name, string type, string location, bool verified, ResourceAvailability availability) => new()
    {
        ResourceName = $"{name} {test.OwnerId}", ResourceType = type, LocationName = location, OwnerId = test.OwnerId,
        Latitude = 59.91, Longitude = 10.75, CreatedAtUtc = DateTime.UtcNow, Availability = availability,
        VerificationStatus = verified ? ResourceVerificationStatus.Approved : ResourceVerificationStatus.Pending,
        VerifiedAtUtc = verified ? DateTime.UtcNow.AddDays(-1) : null,
        VerificationExpiresAtUtc = verified ? DateTime.UtcNow.AddDays(10) : null
    };
}
