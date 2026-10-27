using System.Security.Claims;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace MariaDbIntegrationTests;

public class WorkflowTests
{
    [Fact]
    public void ApprovalAndAvailabilityAreIndependentAndPendingByDefault()
    {
        var resource = new MapSubmission();
        Assert.Equal(ResourceVerificationStatus.Pending, resource.VerificationStatus);
        Assert.Equal(ResourceAvailability.Available, resource.Availability);
        Assert.False(WorkflowEligibility.IsEligible(resource, DateTime.UtcNow));
        resource.VerificationStatus = ResourceVerificationStatus.Approved;
        resource.VerifiedAtUtc = DateTime.UtcNow.AddDays(-1);
        resource.VerificationExpiresAtUtc = DateTime.UtcNow;
        Assert.False(WorkflowEligibility.IsEligible(resource, resource.VerificationExpiresAtUtc.Value));
        resource.VerificationExpiresAtUtc = DateTime.UtcNow.AddDays(1);
        Assert.True(WorkflowEligibility.IsEligible(resource, DateTime.UtcNow));
        resource.Availability = ResourceAvailability.Unavailable;
        Assert.False(WorkflowEligibility.IsEligible(resource, DateTime.UtcNow));
        Assert.Equal(30, new WorkflowOptions().VerificationValidityDays);
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
    public void OperationsActionsRequireAuthenticationAntiforgeryAndTheirOwnRole(string name, string role)
    {
        var type = typeof(OperationsController);
        Assert.Equal(AppRoles.Any, type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles);
        Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(AutoValidateAntiforgeryTokenAttribute)));
        var method = type.GetMethod(name)!;
        Assert.Equal(role, method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles);
        Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(HttpPostAttribute)));
    }

    [Fact]
    public async Task OwnerAcceptsBeforeDispatchAllocationAndCompletedAssignmentRequiresReverification()
    {
        await using var test = await Scenario.Create();
        var requestId = await test.Request();
        Assert.Equal(ResourceAvailability.Available, (await test.Resource()).Availability);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId,
            test.RequestToken(requestId), test.Dispatch, "For tidlig"));
        await test.Service.RespondAsync(requestId, true, test.RequestToken(requestId), test.Owner, "Kan levere");
        Assert.Equal(ResourceAvailability.Reserved, (await test.Resource()).Availability);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId,
            test.RequestToken(requestId), test.Owner, "Ikke koordinator"));
        await test.Service.AllocateAsync(requestId, test.RequestToken(requestId), test.Dispatch, "Tildelt til innsats");
        Assert.Equal(ResourceAvailability.Allocated, (await test.Resource()).Availability);
        var assignmentId = await test.AssignmentId(requestId);
        foreach (var step in new[] { AssignmentStatus.EnRoute, AssignmentStatus.Arrived, AssignmentStatus.Completed })
            await test.Service.UpdateAssignmentAsync(assignmentId, step, test.AssignmentToken(assignmentId), test.Dispatch, "Fremdrift");
        var resource = await test.Resource();
        Assert.Equal(ResourceAvailability.Returned, resource.Availability);
        Assert.Equal(ResourceVerificationStatus.Pending, resource.VerificationStatus);
        Assert.Null(resource.VerifiedAtUtc);
        Assert.Null(resource.VerificationExpiresAtUtc);
        Assert.False(WorkflowEligibility.IsEligible(resource, DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        var audit = await test.Db.WorkflowAudits.Where(a => a.ResourceId == resource.Id).ToListAsync();
        Assert.Contains(audit, a => a.Action == "RequestAccept" && a.ActorId == test.OwnerId);
        Assert.Contains(audit, a => a.Action == "RequestAllocate" && a.ActorId == test.DispatchId);
        Assert.Contains(audit, a => a.Action == "AssignmentCompleted" && a.ActorId == test.DispatchId);
        Assert.All(audit, a => { Assert.False(string.IsNullOrWhiteSpace(a.Reason)); Assert.NotEqual(default, a.OccurredAtUtc); });
    }

    [Fact]
    public async Task OtherProvidersCannotRespondOrChangeResourcesAndAllFailuresAreAtomic()
    {
        await using var test = await Scenario.Create();
        var requestId = await test.Request();
        var token = test.RequestToken(requestId);
        var other = Principal("other-provider", AppRoles.Ressursleverandor);
        var audits = await test.Db.WorkflowAudits.CountAsync(a => a.ResourceId == test.ResourceId);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, true,
            test.ResourceToken(), Principal(test.OwnerId, AppRoles.Verifiseringsansvarlig, AppRoles.Ressursleverandor), "Egen godkjenning"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true, token, other, "Feil eier"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true, token, test.Dispatch, "Dispatch er ikke eier"));
        await Assert.ThrowsAsync<WorkflowException>(async () => await test.Service.CreateNeedAsync("Uautorisert", "Oslo", NeedPriority.Normal, other));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.UpdateResourceAvailabilityAsync(test.ResourceId,
            ResourceAvailability.Unavailable, test.ResourceToken(), other, "Ikke min"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true, token, test.Owner, " "));
        Assert.Equal(ResourceAvailability.Available, (await test.Resource()).Availability);
        Assert.Equal(ResourceRequestStatus.Pending, (await test.Db.ResourceRequests.FindAsync(requestId))!.Status);
        Assert.Equal(audits, await test.Db.WorkflowAudits.CountAsync(a => a.ResourceId == test.ResourceId));
    }

    [Fact]
    public async Task ExpiredAndUnavailableResourcesCannotBeRequestedOrAllocated()
    {
        await using var test = await Scenario.Create();
        var requestId = await test.Request();
        await test.Service.UpdateResourceAvailabilityAsync(test.ResourceId, ResourceAvailability.Unavailable,
            test.ResourceToken(), test.Owner, "I vedlikehold");
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true,
            test.RequestToken(requestId), test.Owner, "Kan ikke reserveres"));
        await test.Service.UpdateResourceAvailabilityAsync(test.ResourceId, ResourceAvailability.Available,
            test.ResourceToken(), test.Owner, "Klar");
        var resource = await test.Resource();
        resource.VerificationExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await test.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true,
            test.RequestToken(requestId), test.Owner, "Utløpt"));
        await test.Service.VerifyAsync(test.ResourceId, true, test.ResourceToken(), test.Verifier, "Kontrollert igjen");
        await test.Service.RespondAsync(requestId, true, test.RequestToken(requestId), test.Owner, "Reserver");
        resource = await test.Resource();
        resource.VerificationExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await test.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId,
            test.RequestToken(requestId), test.Dispatch, "Godkjenning utløpt"));
        Assert.Equal(ResourceAvailability.Reserved, (await test.Resource()).Availability);
        await test.Service.CancelAsync(requestId, test.RequestToken(requestId), test.Dispatch, "Frigjør for ny kontroll");
    }

    [Fact]
    public async Task ConcurrentOwnerAcceptancesCannotDoubleReserve()
    {
        await using var test = await Scenario.Create();
        var first = await test.Request();
        var secondNeed = await test.Service.CreateNeedAsync("Andre behov", "Bergen", NeedPriority.Critical, test.Public);
        var second = await test.Service.CreateRequestAsync(secondNeed, test.ResourceId, test.ResourceToken(),
            (await test.Db.OperationalNeeds.FindAsync(secondNeed))!.ConcurrencyToken, test.Dispatch, "Alternativ innsats");
        var firstToken = test.RequestToken(first);
        var secondToken = test.RequestToken(second);
        async Task<bool> Accept(int id, string token)
        {
            await using var db = new AppDbContext(test.Options);
            try
            {
                await new WorkflowService(db, Microsoft.Extensions.Options.Options.Create(new WorkflowOptions()))
                    .RespondAsync(id, true, token, test.Owner, "Samtidig aksept");
                return true;
            }
            catch (WorkflowException) { return false; }
        }
        var results = await Task.WhenAll(Accept(first, firstToken), Accept(second, secondToken));
        Assert.Single(results, r => r);
        test.Db.ChangeTracker.Clear();
        Assert.Equal(1, await test.Db.ResourceRequests.CountAsync(r => r.ResourceId == test.ResourceId &&
            r.Status == ResourceRequestStatus.Accepted));
        Assert.Equal(ResourceAvailability.Reserved, (await test.Resource()).Availability);
        Assert.Equal(1, await test.Db.WorkflowAudits.CountAsync(a => a.ResourceId == test.ResourceId && a.Action == "RequestAccept"));
    }

    [Fact]
    public async Task StaleTokensPreventVerificationEditsAndRequestDecisions()
    {
        await using var test = await Scenario.Create();
        var oldResourceToken = test.ResourceToken();
        await test.Service.UpdateResourceAvailabilityAsync(test.ResourceId, ResourceAvailability.Unavailable,
            oldResourceToken, test.Owner, "Vedlikehold");
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, true,
            oldResourceToken, test.Verifier, "Gammelt skjema"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.DeleteResourceAsync(test.ResourceId,
            oldResourceToken, test.Owner, "Gammel sletting"));
        await test.Service.UpdateResourceAvailabilityAsync(test.ResourceId, ResourceAvailability.Available,
            test.ResourceToken(), test.Owner, "Klar");
        var oldNeedToken = test.NeedToken();
        var id = await test.Request();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.InProgress, oldNeedToken, test.Leader, "Utdatert behovsskjema"));
        var oldRequestToken = test.RequestToken(id);
        await test.Service.RespondAsync(id, true, oldRequestToken, test.Owner, "Aksepter");
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.CancelAsync(id, oldRequestToken, test.Dispatch, "Gammelt skjema"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.UpdateResourceAvailabilityAsync(test.ResourceId,
            ResourceAvailability.Available, test.ResourceToken(), test.Owner, "Omgå reservasjon"));
    }

    [Fact]
    public async Task RejectCancelNeedCompletionAndHistoryAreSafe()
    {
        await using var test = await Scenario.Create();
        var id = await test.Request();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.Completed, test.NeedToken(), test.Leader, "For tidlig"));
        await test.Service.RespondAsync(id, false, test.RequestToken(id), test.Owner, "Kan ikke levere");
        Assert.Equal(ResourceAvailability.Available, (await test.Resource()).Availability);
        var next = await test.Request();
        await test.Service.RespondAsync(next, true, test.RequestToken(next), test.Owner, "Aksepter");
        await test.Service.CancelAsync(next, test.RequestToken(next), test.Dispatch, "Behov endret");
        Assert.Equal(ResourceAvailability.Available, (await test.Resource()).Availability);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.Completed, test.NeedToken(), test.Leader, "Hopper over tildeling"));
        var assignmentId = await test.Allocated();
        Assert.Equal(NeedStatus.Assigned, (await test.Db.OperationalNeeds.AsNoTracking().SingleAsync(n => n.Id == test.NeedId)).Status);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.Completed, test.NeedToken(), test.Leader, "Tildelingen pågår"));
        foreach (var step in new[] { AssignmentStatus.EnRoute, AssignmentStatus.Arrived, AssignmentStatus.Completed })
            await test.Service.UpdateAssignmentAsync(assignmentId, step, test.AssignmentToken(assignmentId), test.Dispatch, "Fremdrift");
        await test.Service.ChangeNeedStatusAsync(test.NeedId, NeedStatus.Completed, test.NeedToken(), test.Leader, "Oppdrag fullført");
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.Open, test.NeedToken(), test.Leader, "Gjenåpne"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.DeleteResourceAsync(test.ResourceId,
            test.ResourceToken(), test.Owner, "Slett historikk"));
    }

    [Fact]
    public async Task ResourceEditsInvalidateApprovalAndConfiguredValidityIsApplied()
    {
        await using var test = await Scenario.Create();
        var custom = new WorkflowService(test.Db, Options.Create(new WorkflowOptions { VerificationValidityDays = 7 }));
        await custom.VerifyAsync(test.ResourceId, true, test.ResourceToken(), test.Verifier, "Ukentlig kontroll");
        var resource = await test.Resource();
        Assert.Equal(TimeSpan.FromDays(7), resource.VerificationExpiresAtUtc - resource.VerifiedAtUtc);
        await custom.UpdateResourceAsync(test.ResourceId, new MapSubmission
        {
            ResourceName = "Endret ressurs", ResourceType = resource.ResourceType,
            LocationName = resource.LocationName, Latitude = resource.Latitude, Longitude = resource.Longitude
        }, ResourceAvailability.Available, resource.ConcurrencyToken, test.Owner, "Endret kapasitet");
        Assert.Equal(ResourceVerificationStatus.Pending, (await test.Resource()).VerificationStatus);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        await custom.VerifyAsync(test.ResourceId, false, test.ResourceToken(), test.Verifier, "Mangler dokumentasjon");
        Assert.Equal(ResourceVerificationStatus.Rejected, (await test.Resource()).VerificationStatus);
        Assert.Null((await test.Resource()).VerificationExpiresAtUtc);
    }

    [Fact]
    public async Task DatabaseConcurrencyTokenProtectsIndependentResourceWriters()
    {
        await using var test = await Scenario.Create();
        await using var other = new AppDbContext(test.Options);
        var stale = await other.MapSubmissions.SingleAsync(r => r.Id == test.ResourceId);
        await test.Service.UpdateResourceAvailabilityAsync(test.ResourceId, ResourceAvailability.Unavailable,
            test.ResourceToken(), test.Owner, "Vedlikehold");
        stale.ResourceName = "Utdatert endring";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task ProviderQueueContainsOnlyOwnedRequestsAndNoAuditLog()
    {
        await using var test = await Scenario.Create();
        var id = await test.Request();
        var controller = new OperationsController(test.Db, test.Service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = test.Owner }
            }
        };
        var ownResult = Assert.IsType<ViewResult>(await controller.Index());
        var own = Assert.IsType<OperationsViewModel>(ownResult.Model);
        Assert.Single(own.Requests, r => r.Id == id);
        Assert.All(own.Requests, r => Assert.Equal(test.OwnerId, r.Resource.OwnerId));
        Assert.All(own.Resources, r => Assert.Equal(test.OwnerId, r.OwnerId));
        Assert.Empty(own.Audit);
        controller.HttpContext.User = Principal("unrelated-provider", AppRoles.Ressursleverandor);
        var other = Assert.IsType<OperationsViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);
        Assert.Empty(other.Requests);
        Assert.Empty(other.Resources);
        Assert.Empty(other.Needs);
        Assert.Empty(other.Audit);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([], "integration"));
        Assert.IsType<ForbidResult>(await controller.Index());
    }

    internal static ClaimsPrincipal Principal(string id, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "integration"));
    }

    internal sealed class Scenario : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public required DbContextOptions<AppDbContext> Options { get; init; }
        public required WorkflowService Service { get; init; }
        public string OwnerId { get; } = Guid.NewGuid().ToString();
        public string DispatchId { get; } = Guid.NewGuid().ToString();
        public string VerifierId { get; } = Guid.NewGuid().ToString();
        public string LeaderId { get; } = Guid.NewGuid().ToString();
        public string PublicId { get; } = Guid.NewGuid().ToString();
        private string[] ActorIds => [OwnerId, DispatchId, VerifierId, LeaderId, PublicId];
        public ClaimsPrincipal Owner => Principal(OwnerId, AppRoles.Ressursleverandor);
        public ClaimsPrincipal Dispatch => Principal(DispatchId, AppRoles.DispatchTeam);
        public ClaimsPrincipal Verifier => Principal(VerifierId, AppRoles.Verifiseringsansvarlig);
        public ClaimsPrincipal Leader => Principal(LeaderId, AppRoles.Kriseledelse);
        public ClaimsPrincipal Public => Principal(PublicId, AppRoles.OffentligAktor);
        public int ResourceId { get; private set; }
        public int NeedId { get; private set; }
        public Task<MapSubmission> Resource() => Db.MapSubmissions.SingleAsync(r => r.Id == ResourceId);
        public string ResourceToken() => Db.MapSubmissions.Single(r => r.Id == ResourceId).ConcurrencyToken;
        public string NeedToken() => Db.OperationalNeeds.Single(n => n.Id == NeedId).ConcurrencyToken;
        public string RequestToken(int id) => Db.ResourceRequests.Single(r => r.Id == id).ConcurrencyToken;
        public Task<int> Request() => Service.CreateRequestAsync(NeedId, ResourceId, ResourceToken(), NeedToken(), Dispatch, "Behov for transport");
        public Task<int> AssignmentId(int requestId) => Db.ResourceAssignments.Where(a => a.RequestId == requestId).Select(a => a.Id).SingleAsync();
        public string AssignmentToken(int id) => Db.ResourceAssignments.Single(a => a.Id == id).ConcurrencyToken;
        public async Task<int> Allocated()
        {
            var id = await Request();
            await Service.RespondAsync(id, true, RequestToken(id), Owner, "Kan levere");
            await Service.AllocateAsync(id, RequestToken(id), Dispatch, "Tildelt");
            return await AssignmentId(id);
        }

        public static async Task<Scenario> Create()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProviderConfiguration.Configure(optionsBuilder, DatabaseProviderConfiguration.GetRequiredConnectionString(configuration));
            var db = new AppDbContext(optionsBuilder.Options);
            await db.Database.MigrateAsync();
            var test = new Scenario
            {
                Db = db, Options = optionsBuilder.Options,
                Service = new WorkflowService(db, Microsoft.Extensions.Options.Options.Create(new WorkflowOptions()))
            };
            foreach (var id in test.ActorIds)
                db.Users.Add(new IdentityUser { Id = id, UserName = $"{id}@example.test" });
            var resource = new MapSubmission
            {
                ResourceName = $"Test {test.OwnerId}", ResourceType = "Kjøretøy", LocationName = "Oslo",
                Latitude = 59.91, Longitude = 10.75, CreatedAtUtc = DateTime.UtcNow, OwnerId = test.OwnerId
            };
            db.MapSubmissions.Add(resource);
            await db.SaveChangesAsync();
            test.ResourceId = resource.Id;
            await test.Service.VerifyAsync(resource.Id, true, resource.ConcurrencyToken, test.Verifier, "Kontrollert kapasitet");
            test.NeedId = await test.Service.CreateNeedAsync("Transport av utstyr", "Oslo", NeedPriority.High, test.Public);
            return test;
        }

        public async ValueTask DisposeAsync()
        {
            Db.ChangeTracker.Clear();
            var actors = ActorIds;
            await Db.WorkflowAudits.Where(a => actors.Contains(a.ActorId)).ExecuteDeleteAsync();
            await Db.ResourceAssignments.Where(a => a.ResourceId == ResourceId).ExecuteDeleteAsync();
            await Db.ResourceRequests.Where(r => r.ResourceId == ResourceId).ExecuteDeleteAsync();
            await Db.OperationalNeeds.Where(n => n.CreatedById == PublicId).ExecuteDeleteAsync();
            await Db.MapSubmissions.Where(r => r.Id == ResourceId).ExecuteDeleteAsync();
            await Db.Users.Where(u => actors.Contains(u.Id)).ExecuteDeleteAsync();
            await Db.DisposeAsync();
        }
    }
}
