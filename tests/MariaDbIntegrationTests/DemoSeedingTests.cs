using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MariaDbIntegrationTests;

public class DemoSeedingTests
{
    private static IConfiguration Config(bool enabled, string? password = null)
    {
        var values = new Dictionary<string, string?> { ["SEED_DEMO_ACCOUNTS"] = enabled ? "true" : "false" };
        if (password != null) values["DEMO_ACCOUNT_PASSWORD"] = password;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public async Task NothingIsSeededUnlessTheFlagIsTrue()
    {
        await using var test = await Scenario.Create();
        await IdentitySeeder.SeedAsync(test.Services, Config(false));
        await IdentitySeeder.SeedAsync(test.Services, new ConfigurationBuilder().Build());
        Assert.Empty(await test.DemoUsers());
        Assert.False(await test.Db.WorkflowAudits.AnyAsync(a => a.Action == DemoDataSeeder.Marker));
    }

    [Fact]
    public async Task EachRoleGetsOneConfirmedAccountWithTheDefaultPasswordAndSeedingIsIdempotent()
    {
        await using var test = await Scenario.Create();
        for (var run = 0; run < 2; run++)
            await IdentitySeeder.SeedAsync(test.Services, Config(true));

        var accounts = await test.DemoUsers();
        Assert.Equal(5, accounts.Count);
        foreach (var (email, role) in IdentitySeeder.DemoAccounts)
        {
            var user = Assert.Single(accounts, u => u.Email == email);
            Assert.True(user.EmailConfirmed);
            Assert.True(await test.Users.CheckPasswordAsync(user, IdentitySeeder.DefaultDemoPassword));
            Assert.Equal(new[] { role }, await test.Users.GetRolesAsync(user));
        }
        Assert.Equal(5, IdentitySeeder.DemoAccounts.Select(a => a.Role).Distinct().Count());
        Assert.Equal(3, await test.Db.OperationalNeeds.CountAsync(n => n.CreatedById == test.IdOf(AppRoles.OffentligAktor)));
        Assert.Equal(4, await test.Db.MapSubmissions.CountAsync(r => r.OwnerId == test.IdOf(AppRoles.Ressursleverandor)));
        Assert.Equal(1, await test.Db.WorkflowAudits.CountAsync(a => a.Action == DemoDataSeeder.Marker));
        Assert.Contains(await test.Db.MapSubmissions.Where(r => r.OwnerId == test.IdOf(AppRoles.Ressursleverandor)).ToListAsync(),
            r => r.VerificationStatus == ResourceVerificationStatus.Pending);
        Assert.Single(await test.Db.ResourceAssignments.Where(a => a.CreatedById == test.IdOf(AppRoles.DispatchTeam)).ToListAsync());
    }

    [Fact]
    public async Task ExistingAccountsKeepTheirPasswordAndAreMovedToTheirDemoRole()
    {
        await using var test = await Scenario.Create();
        await IdentitySeeder.SeedAsync(test.Services, Config(true));
        var user = (await test.Users.FindByEmailAsync("dispatch@gmail.com"))!;
        var token = await test.Users.GeneratePasswordResetTokenAsync(user);
        Assert.True((await test.Users.ResetPasswordAsync(user, token, "Changed-Password-77!")).Succeeded);
        Assert.True((await test.Users.RemoveFromRoleAsync(user, AppRoles.DispatchTeam)).Succeeded);
        Assert.True((await test.Users.AddToRoleAsync(user, AppRoles.Ressursleverandor)).Succeeded);

        await IdentitySeeder.SeedAsync(test.Services, Config(true, "Another-Demo-Pass-1!"));

        test.Db.ChangeTracker.Clear();
        user = (await test.Users.FindByEmailAsync("dispatch@gmail.com"))!;
        Assert.True(await test.Users.CheckPasswordAsync(user, "Changed-Password-77!"));
        Assert.False(await test.Users.CheckPasswordAsync(user, "Another-Demo-Pass-1!"));
        Assert.Equal(new[] { AppRoles.DispatchTeam }, await test.Users.GetRolesAsync(user));
        Assert.Equal(5, (await test.DemoUsers()).Count);
    }

    [Fact]
    public async Task ConfiguredPasswordIsUsedForNewAccounts()
    {
        await using var test = await Scenario.Create();
        await IdentitySeeder.SeedAsync(test.Services, Config(true, "Another-Demo-Pass-1!"));
        var user = (await test.Users.FindByEmailAsync("kriseledelse@gmail.com"))!;
        Assert.True(await test.Users.CheckPasswordAsync(user, "Another-Demo-Pass-1!"));
        Assert.False(await test.Users.CheckPasswordAsync(user, IdentitySeeder.DefaultDemoPassword));
    }

    [Fact]
    public async Task AdminBootstrapStillCreatesKriseledelseAlongsideDemoAccounts()
    {
        await using var test = await Scenario.Create();
        var email = $"{Guid.NewGuid():N}@example.test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ADMIN_EMAIL"] = email, ["ADMIN_PASSWORD"] = "Bootstrap-Admin-42!", ["SEED_DEMO_ACCOUNTS"] = "true"
        }).Build();
        try
        {
            await IdentitySeeder.SeedAsync(test.Services, configuration);
            var admin = (await test.Users.FindByEmailAsync(email))!;
            Assert.Equal(new[] { AppRoles.Kriseledelse }, await test.Users.GetRolesAsync(admin));
        }
        finally
        {
            await test.Db.Users.Where(u => u.Email == email).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task DashboardHasContentForEveryDemoRole()
    {
        await using var test = await Scenario.Create();
        await IdentitySeeder.SeedAsync(test.Services, Config(true));
        var dashboard = test.Services.GetRequiredService<DashboardService>();
        var now = DateTime.UtcNow;

        var provider = await dashboard.BuildAsync(test.Principal(AppRoles.Ressursleverandor), now);
        Assert.Equal(4, provider.OwnResources.Count);
        Assert.Single(provider.IncomingRequests, r => r.Status == ResourceRequestStatus.Pending);
        Assert.NotEmpty(provider.ResourceTimeline);

        var verifier = await dashboard.BuildAsync(test.Principal(AppRoles.Verifiseringsansvarlig), now);
        var queued = Assert.Single(verifier.VerificationQueue, r => r.ResourceName == "Beltegraver 14 tonn");
        Assert.Equal(ResourceVerificationStatus.Pending, queued.VerificationStatus);
        Assert.Empty(verifier.OwnResources);
        Assert.Empty(verifier.MatchingResources);

        var dispatch = await dashboard.BuildAsync(test.Principal(AppRoles.DispatchTeam), now);
        var publicId = test.IdOf(AppRoles.OffentligAktor);
        Assert.Equal(3, dispatch.Needs.Count(n => n.CreatedById == publicId));
        Assert.Contains(dispatch.MatchingResources, r => r.ResourceName == "Terrengbil 4x4");
        Assert.Contains(dispatch.MatchingResources, r => r.ResourceName == "Drone med varmekamera");
        Assert.DoesNotContain(dispatch.MatchingResources, r => r.Id == queued.Id);
        Assert.Single(dispatch.Assignments, a => a.Resource.ResourceName == "Aggregat 20 kVA");
        Assert.Single(dispatch.Requests, r => r.Resource.ResourceName == "Drone med varmekamera");

        var leader = await dashboard.BuildAsync(test.Principal(AppRoles.Kriseledelse), now);
        Assert.Equal(NeedPriority.Critical, leader.Needs[0].Priority);
        Assert.Equal(5, leader.RoleCounts.Count(c => c.Count >= 1));
        Assert.Empty(leader.MatchingResources);

        var publicActor = await dashboard.BuildAsync(test.Principal(AppRoles.OffentligAktor), now);
        Assert.Equal(3, publicActor.Needs.Count);
        Assert.Empty(publicActor.VerificationQueue);
        Assert.Empty(publicActor.OwnResources);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;
        public IServiceProvider Services => scope.ServiceProvider;
        public AppDbContext Db => Services.GetRequiredService<AppDbContext>();
        public UserManager<IdentityUser> Users => Services.GetRequiredService<UserManager<IdentityUser>>();

        private Scenario(ServiceProvider provider)
        {
            this.provider = provider;
            scope = provider.CreateAsyncScope();
        }

        public static async Task<Scenario> Create()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var connection = DatabaseProviderConfiguration.GetRequiredConnectionString(configuration);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options => DatabaseProviderConfiguration.Configure(options, connection));
            // Same password policy as Program.cs.
            services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
            }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
            services.AddScoped<DashboardService>();
            var test = new Scenario(services.BuildServiceProvider());
            await test.Db.Database.MigrateAsync();
            await test.CleanUp();
            return test;
        }

        public async Task<List<IdentityUser>> DemoUsers()
        {
            var emails = IdentitySeeder.DemoAccounts.Select(a => a.Email).ToList();
            return await Db.Users.AsNoTracking().Where(u => emails.Contains(u.Email!)).ToListAsync();
        }

        public string IdOf(string role) => Users.FindByEmailAsync(IdentitySeeder.DemoAccounts.First(a => a.Role == role).Email)
            .GetAwaiter().GetResult()!.Id;

        public System.Security.Claims.ClaimsPrincipal Principal(string role) => WorkflowTests.Principal(IdOf(role), role);

        private async Task CleanUp()
        {
            Db.ChangeTracker.Clear();
            var ids = await (await DemoUsers().ConfigureAwait(false)).ToAsyncEnumerable().Select(u => u.Id).ToListAsync();
            await Db.WorkflowAudits.Where(a => a.Action == DemoDataSeeder.Marker || ids.Contains(a.ActorId)).ExecuteDeleteAsync();
            await Db.ResourceAssignments.Where(a => ids.Contains(a.CreatedById)).ExecuteDeleteAsync();
            await Db.ResourceRequests.Where(r => ids.Contains(r.CreatedById)).ExecuteDeleteAsync();
            await Db.OperationalNeeds.Where(n => ids.Contains(n.CreatedById)).ExecuteDeleteAsync();
            await Db.MapSubmissions.Where(r => r.OwnerId != null && ids.Contains(r.OwnerId)).ExecuteDeleteAsync();
            await Db.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await CleanUp();
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
