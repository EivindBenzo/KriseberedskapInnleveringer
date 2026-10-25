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

    [Fact]
    public void OperationsRequireAuthenticationAntiforgeryAndAdminForCoordination()
    {
        var type = typeof(OperationsController);
        Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(AuthorizeAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(AutoValidateAntiforgeryTokenAttribute)));
        foreach (var name in new[] { "Verify", "CreateNeed", "NeedStatus", "CreateRequest", "Allocate", "Cancel" })
        {
            var method = type.GetMethod(name)!;
            Assert.Equal("Admin", method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Single().Roles);
            Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(HttpPostAttribute)));
        }
    }

    [Fact]
    public async Task OwnerAcceptsBeforeCoordinatorAllocationAndReturnRequiresReverification()
    {
        await using var test = await Scenario.Create();
        var requestId = await test.Request();
        Assert.Equal(ResourceAvailability.Available, (await test.Resource()).Availability);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId,
            test.RequestToken(requestId), test.Admin, "For tidlig"));
        await test.Service.RespondAsync(requestId, true, test.RequestToken(requestId), test.Owner, "Kan levere");
        Assert.Equal(ResourceAvailability.Reserved, (await test.Resource()).Availability);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId,
            test.RequestToken(requestId), test.Owner, "Ikke koordinator"));
        await test.Service.AllocateAsync(requestId, test.RequestToken(requestId), test.Admin, "Tildelt til innsats");
        Assert.Equal(ResourceAvailability.Allocated, (await test.Resource()).Availability);
        await test.Service.ReturnAsync(requestId, test.RequestToken(requestId), test.Owner, "Returnert i god stand");
        var resource = await test.Resource();
        Assert.Equal(ResourceAvailability.Available, resource.Availability);
        Assert.Equal(ResourceVerificationStatus.Pending, resource.VerificationStatus);
        Assert.Null(resource.VerifiedAtUtc);
        Assert.Null(resource.VerificationExpiresAtUtc);
        Assert.False(WorkflowEligibility.IsEligible(resource, DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        var audit = await test.Db.WorkflowAudits.Where(a => a.ResourceId == resource.Id).ToListAsync();
        Assert.Contains(audit, a => a.Action == "RequestAccept" && a.ActorId == test.OwnerId);
        Assert.Contains(audit, a => a.Action == "RequestAllocate" && a.ActorId == test.AdminId);
        Assert.Contains(audit, a => a.Action == "RequestReturn" && a.ActorId == test.OwnerId);
        Assert.All(audit, a => { Assert.False(string.IsNullOrWhiteSpace(a.Reason)); Assert.NotEqual(default, a.OccurredAtUtc); });
    }

    [Fact]
    public async Task OtherProvidersCannotRespondOrChangeResourcesAndAllFailuresAreAtomic()
    {
        await using var test = await Scenario.Create();
        var requestId = await test.Request();
        var token = test.RequestToken(requestId);
        var other = Principal("other-provider");
        var audits = await test.Db.WorkflowAudits.CountAsync(a => a.ResourceId == test.ResourceId);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.VerifyAsync(test.ResourceId, true,
            test.ResourceToken(), Principal(test.OwnerId, true), "Egen godkjenning"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true, token, other, "Feil eier"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.RespondAsync(requestId, true, token, test.Admin, "Admin er ikke eier"));
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
        await test.Service.VerifyAsync(test.ResourceId, true, test.ResourceToken(), test.Admin, "Kontrollert igjen");
        await test.Service.RespondAsync(requestId, true, test.RequestToken(requestId), test.Owner, "Reserver");
        resource = await test.Resource();
        resource.VerificationExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await test.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.AllocateAsync(requestId,
            test.RequestToken(requestId), test.Admin, "Godkjenning utløpt"));
        Assert.Equal(ResourceAvailability.Reserved, (await test.Resource()).Availability);
        await test.Service.CancelAsync(requestId, test.RequestToken(requestId), test.Admin, "Frigjør for ny kontroll");
    }

    [Fact]
    public async Task ConcurrentOwnerAcceptancesCannotDoubleReserve()
    {
        await using var test = await Scenario.Create();
        var first = await test.Request();
        var secondNeed = await test.Service.CreateNeedAsync("Andre behov", "Bergen", NeedPriority.Critical, test.Admin);
        var second = await test.Service.CreateRequestAsync(secondNeed, test.ResourceId, test.ResourceToken(),
            (await test.Db.OperationalNeeds.FindAsync(secondNeed))!.ConcurrencyToken, test.Admin, "Alternativ innsats");
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
            oldResourceToken, test.Admin, "Gammelt skjema"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.DeleteResourceAsync(test.ResourceId,
            oldResourceToken, test.Owner, "Gammel sletting"));
        await test.Service.UpdateResourceAvailabilityAsync(test.ResourceId, ResourceAvailability.Available,
            test.ResourceToken(), test.Owner, "Klar");
        var oldNeedToken = test.NeedToken();
        var id = await test.Request();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.InProgress, oldNeedToken, test.Admin, "Utdatert behovsskjema"));
        var oldRequestToken = test.RequestToken(id);
        await test.Service.RespondAsync(id, true, oldRequestToken, test.Owner, "Aksepter");
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.CancelAsync(id, oldRequestToken, test.Admin, "Gammelt skjema"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.UpdateResourceAvailabilityAsync(test.ResourceId,
            ResourceAvailability.Available, test.ResourceToken(), test.Owner, "Omgå reservasjon"));
    }

    [Fact]
    public async Task RejectCancelNeedCompletionAndHistoryAreSafe()
    {
        await using var test = await Scenario.Create();
        var id = await test.Request();
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.Completed, test.NeedToken(), test.Admin, "For tidlig"));
        await test.Service.RespondAsync(id, false, test.RequestToken(id), test.Owner, "Kan ikke levere");
        Assert.Equal(ResourceAvailability.Available, (await test.Resource()).Availability);
        var next = await test.Request();
        await test.Service.RespondAsync(next, true, test.RequestToken(next), test.Owner, "Aksepter");
        await test.Service.CancelAsync(next, test.RequestToken(next), test.Admin, "Behov endret");
        Assert.Equal(ResourceAvailability.Available, (await test.Resource()).Availability);
        await test.Service.ChangeNeedStatusAsync(test.NeedId, NeedStatus.Completed, test.NeedToken(), test.Admin, "Oppdrag fullført");
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.ChangeNeedStatusAsync(test.NeedId,
            NeedStatus.Open, test.NeedToken(), test.Admin, "Gjenåpne"));
        await Assert.ThrowsAsync<WorkflowException>(() => test.Service.DeleteResourceAsync(test.ResourceId,
            test.ResourceToken(), test.Owner, "Slett historikk"));
    }

    [Fact]
    public async Task ResourceEditsInvalidateApprovalAndConfiguredValidityIsApplied()
    {
        await using var test = await Scenario.Create();
        var custom = new WorkflowService(test.Db, Options.Create(new WorkflowOptions { VerificationValidityDays = 7 }));
        await custom.VerifyAsync(test.ResourceId, true, test.ResourceToken(), test.Admin, "Ukentlig kontroll");
        var resource = await test.Resource();
        Assert.Equal(TimeSpan.FromDays(7), resource.VerificationExpiresAtUtc - resource.VerifiedAtUtc);
        await custom.UpdateResourceAsync(test.ResourceId, new MapSubmission
        {
            ResourceName = "Endret ressurs", ResourceType = resource.ResourceType,
            LocationName = resource.LocationName, Latitude = resource.Latitude, Longitude = resource.Longitude
        }, ResourceAvailability.Available, resource.ConcurrencyToken, test.Owner, "Endret kapasitet");
        Assert.Equal(ResourceVerificationStatus.Pending, (await test.Resource()).VerificationStatus);
        await Assert.ThrowsAsync<WorkflowException>(() => test.Request());
        await custom.VerifyAsync(test.ResourceId, false, test.ResourceToken(), test.Admin, "Mangler dokumentasjon");
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
        controller.HttpContext.User = Principal("unrelated-provider");
        var other = Assert.IsType<OperationsViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);
        Assert.Empty(other.Requests);
        Assert.Empty(other.Resources);
        Assert.Empty(other.Needs);
        Assert.Empty(other.Audit);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([], "integration"));
        Assert.IsType<ForbidResult>(await controller.Index());
    }

    private static ClaimsPrincipal Principal(string id, bool admin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id) };
        if (admin) claims.Add(new(ClaimTypes.Role, "Admin"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "integration"));
    }

    private sealed class Scenario : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public required DbContextOptions<AppDbContext> Options { get; init; }
        public required WorkflowService Service { get; init; }
        public string OwnerId { get; } = Guid.NewGuid().ToString();
        public string AdminId { get; } = Guid.NewGuid().ToString();
        public ClaimsPrincipal Owner => Principal(OwnerId);
        public ClaimsPrincipal Admin => Principal(AdminId, true);
        public int ResourceId { get; private set; }
        public int NeedId { get; private set; }
        public Task<MapSubmission> Resource() => Db.MapSubmissions.SingleAsync(r => r.Id == ResourceId);
        public string ResourceToken() => Db.MapSubmissions.Single(r => r.Id == ResourceId).ConcurrencyToken;
        public string NeedToken() => Db.OperationalNeeds.Single(n => n.Id == NeedId).ConcurrencyToken;
        public string RequestToken(int id) => Db.ResourceRequests.Single(r => r.Id == id).ConcurrencyToken;
        public Task<int> Request() => Service.CreateRequestAsync(NeedId, ResourceId, ResourceToken(), NeedToken(), Admin, "Behov for transport");

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
            db.Users.Add(new IdentityUser { Id = test.OwnerId, UserName = $"{test.OwnerId}@example.test" });
            db.Users.Add(new IdentityUser { Id = test.AdminId, UserName = $"{test.AdminId}@example.test" });
            var resource = new MapSubmission
            {
                ResourceName = $"Test {test.OwnerId}", ResourceType = "Kjøretøy", LocationName = "Oslo",
                Latitude = 59.91, Longitude = 10.75, CreatedAtUtc = DateTime.UtcNow, OwnerId = test.OwnerId
            };
            db.MapSubmissions.Add(resource);
            await db.SaveChangesAsync();
            test.ResourceId = resource.Id;
            await test.Service.VerifyAsync(resource.Id, true, resource.ConcurrencyToken, test.Admin, "Kontrollert kapasitet");
            test.NeedId = await test.Service.CreateNeedAsync("Transport av utstyr", "Oslo", NeedPriority.High, test.Admin);
            return test;
        }

        public async ValueTask DisposeAsync()
        {
            Db.ChangeTracker.Clear();
            await Db.WorkflowAudits.Where(a => a.ActorId == OwnerId || a.ActorId == AdminId).ExecuteDeleteAsync();
            await Db.ResourceRequests.Where(r => r.ResourceId == ResourceId).ExecuteDeleteAsync();
            await Db.OperationalNeeds.Where(n => n.CreatedById == AdminId).ExecuteDeleteAsync();
            await Db.MapSubmissions.Where(r => r.Id == ResourceId).ExecuteDeleteAsync();
            await Db.Users.Where(u => u.Id == OwnerId || u.Id == AdminId).ExecuteDeleteAsync();
            await Db.DisposeAsync();
        }
    }
}
