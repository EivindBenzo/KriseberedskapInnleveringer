using System.Diagnostics;
using HeimevernetInnlevering1.Controllers;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MariaDbIntegrationTests;

public class AdminPromotionTests
{
    [Theory]
    [InlineData("--promote-admin")]
    [InlineData("--promote-admin", "not-an-email")]
    [InlineData("--promote-admin", "operator@example.test", "extra")]
    [InlineData("--promote-admin=operator@example.test")]
    public async Task InvalidCommandArgumentsFailWithoutOpeningDatabase(params string[] args)
    {
        Assert.Equal(2, await AdminPromotionCommand.RunAsync(args));
    }

    [Fact]
    public async Task BootstrapPromotesExistingAccountOnlyAfterVerifyingConfiguredPassword()
    {
        await using var test = await Scenario.Create();
        var originalHash = test.User.PasswordHash;
        var originalStamp = test.User.SecurityStamp;
        var bootstrap = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ADMIN_EMAIL"] = test.User.Email,
            ["ADMIN_PASSWORD"] = Scenario.Password + "-wrong"
        }).Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IdentitySeeder.SeedAsync(test.Services, bootstrap));
        Assert.False(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.AdminRole));
        Assert.Equal(originalStamp, test.User.SecurityStamp);

        bootstrap["ADMIN_PASSWORD"] = Scenario.Password;
        await IdentitySeeder.SeedAsync(test.Services, bootstrap);
        Assert.True(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.AdminRole));
        Assert.True(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.UserRole));
        Assert.NotEqual(originalStamp, test.User.SecurityStamp);
        Assert.Equal(originalHash, test.User.PasswordHash);
        Assert.True(await test.Users.CheckPasswordAsync(test.User, Scenario.Password));
        test.Db.ChangeTracker.Clear();
        var storedUser = await test.Db.Users.SingleAsync(user => user.Id == test.User.Id);
        Assert.Equal(originalHash, storedUser.PasswordHash);
        Assert.NotEqual(originalStamp, storedUser.SecurityStamp);
        var storedResource = await test.Db.MapSubmissions.SingleAsync(r => r.Id == test.Resource.Id);
        Assert.Equal(test.User.Id, storedResource.OwnerId);
        Assert.Equal(test.Resource.ResourceName, storedResource.ResourceName);
        Assert.Equal(test.Resource.ConcurrencyToken, storedResource.ConcurrencyToken);
    }

    [Fact]
    public async Task RepeatedRecoveryIsNoOpAndNormalSeedingKeepsRecoveredAccount()
    {
        await using var test = await Scenario.Create();
        await AdminPromotionCommand.PromoteAsync(test.Services, test.User.Email!);
        var stamp = test.User.SecurityStamp;
        var hash = test.User.PasswordHash;
        var concurrencyStamp = test.User.ConcurrencyStamp;
        await AdminPromotionCommand.PromoteAsync(test.Services, test.User.Email!);
        await IdentitySeeder.SeedAsync(test.Services, new ConfigurationBuilder().Build());
        Assert.Equal(stamp, test.User.SecurityStamp);
        Assert.Equal(hash, test.User.PasswordHash);
        Assert.Equal(concurrencyStamp, test.User.ConcurrencyStamp);
        Assert.Equal(2, await test.Db.UserRoles.CountAsync(role => role.UserId == test.User.Id));
    }

    [Fact]
    public async Task EnvironmentBootstrapCreatesAdministratorWhoCanUseNormalPasswordLogin()
    {
        await using var test = await Scenario.Create();
        var email = $"{Guid.NewGuid():N}@example.test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ADMIN_EMAIL"] = $" {email} ",
            ["ADMIN_PASSWORD"] = Scenario.Password
        }).Build();
        try
        {
            await IdentitySeeder.SeedAsync(test.Services, configuration);
            var admin = await test.Users.FindByEmailAsync(email);
            Assert.NotNull(admin);
            Assert.True(await test.Users.IsInRoleAsync(admin, IdentitySeeder.AdminRole));
            Assert.True(await test.Users.IsInRoleAsync(admin, IdentitySeeder.UserRole));
            Assert.True(AccountLoginRules.CanUseChoice(nameof(LoginAccess.Admin), true));
            var login = test.Services.GetRequiredService<SignInManager<IdentityUser>>();
            Assert.True((await login.CheckPasswordSignInAsync(admin, Scenario.Password, true)).Succeeded);
            Assert.False((await login.CheckPasswordSignInAsync(admin, Scenario.Password + "-wrong", true)).Succeeded);
            var stamp = admin.SecurityStamp;
            await IdentitySeeder.SeedAsync(test.Services, configuration);
            Assert.Equal(stamp, admin.SecurityStamp);
        }
        finally
        {
            await test.Db.Users.Where(user => user.Email == email).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task MissingTargetFailsWithoutCreatingAccountOrChangingExistingAccount()
    {
        await using var test = await Scenario.Create();
        var missing = $"{Guid.NewGuid():N}@example.test";
        var stamp = test.User.SecurityStamp;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminPromotionCommand.PromoteAsync(test.Services, missing));
        Assert.Null(await test.Users.FindByEmailAsync(missing));
        Assert.False(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.AdminRole));
        Assert.Equal(stamp, test.User.SecurityStamp);
    }

    [Fact]
    public async Task StampFailureRollsBackRoleAssignment()
    {
        await using var test = await Scenario.Create();
        var originalStamp = test.User.SecurityStamp;
        var originalHash = test.User.PasswordHash;
        var store = Assert.IsType<RecoveryUserStore>(test.Services.GetRequiredService<IUserStore<IdentityUser>>());
        store.RejectStampChanges = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminPromotionCommand.PromoteAsync(test.Services, test.User.Email!));
        test.Db.ChangeTracker.Clear();
        var stored = await test.Users.FindByIdAsync(test.User.Id);
        Assert.NotNull(stored);
        Assert.False(await test.Users.IsInRoleAsync(stored, IdentitySeeder.AdminRole));
        Assert.Equal(originalStamp, stored.SecurityStamp);
        Assert.Equal(originalHash, stored.PasswordHash);
    }

    [Fact]
    public async Task CliExitsWithoutHttpHostingOrBootstrapAndDoesNotPrintAccount()
    {
        await using var test = await Scenario.Create();
        var hash = test.User.PasswordHash;
        var result = await RunCliAsync("--promote-admin", test.User.Email!);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(test.User.Email!, result.Output + result.Error);
        Assert.DoesNotContain("Now listening", result.Output);
        test.Db.ChangeTracker.Clear();
        var stored = await test.Users.FindByIdAsync(test.User.Id);
        Assert.NotNull(stored);
        Assert.True(await test.Users.IsInRoleAsync(stored, IdentitySeeder.AdminRole));
        var stamp = stored.SecurityStamp;
        result = await RunCliAsync("--promote-admin", test.User.Email!);
        Assert.Equal(0, result.ExitCode);
        test.Db.ChangeTracker.Clear();
        stored = await test.Users.FindByIdAsync(test.User.Id);
        Assert.NotNull(stored);
        Assert.Equal(stamp, stored.SecurityStamp);
        Assert.Equal(hash, stored.PasswordHash);
    }

    [Fact]
    public async Task CliMissingAccountFailsWithoutPrintingTargetOrCreatingAccount()
    {
        await using var test = await Scenario.Create();
        var missing = $"{Guid.NewGuid():N}@example.test";
        var result = await RunCliAsync("--promote-admin", missing);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(missing, result.Output + result.Error);
        Assert.Null(await test.Users.FindByEmailAsync(missing));
        Assert.False(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.AdminRole));
    }

    [Theory]
    [InlineData("--promote-admin")]
    [InlineData("--promote-admin", "invalid")]
    [InlineData("--promote-admin", "operator@example.test", "extra")]
    public async Task CliMalformedArgumentsExitWithUsage(params string[] args)
    {
        var result = await RunCliAsync(args);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.Error);
        Assert.DoesNotContain("operator@example.test", result.Output + result.Error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add(typeof(AdminPromotionCommand).Assembly.Location);
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        // Invalid bootstrap settings would stop normal startup; recovery must ignore them.
        process.StartInfo.Environment["ADMIN_EMAIL"] = "invalid-bootstrap@example.test";
        process.StartInfo.Environment["ADMIN_PASSWORD"] = "";
        process.StartInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        return (process.ExitCode, await output, await error);
    }

    [Fact]
    public async Task PublicSignupCannotRequestAdministratorRole()
    {
        await using var test = await Scenario.Create(registerViaController: true);
        Assert.True(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.UserRole));
        Assert.False(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.AdminRole));
        await IdentitySeeder.SeedAsync(test.Services, new ConfigurationBuilder().Build());
        Assert.False(await test.Users.IsInRoleAsync(test.User, IdentitySeeder.AdminRole));
    }

    private sealed class Scenario : IAsyncDisposable
    {
        public const string Password = "Integration-Only-42!";
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;
        public IServiceProvider Services => scope.ServiceProvider;
        public AppDbContext Db => Services.GetRequiredService<AppDbContext>();
        public UserManager<IdentityUser> Users => Services.GetRequiredService<UserManager<IdentityUser>>();
        public IdentityUser User { get; private set; } = null!;
        public MapSubmission Resource { get; private set; } = null!;

        private Scenario(ServiceProvider provider)
        {
            this.provider = provider;
            scope = provider.CreateAsyncScope();
        }

        public static async Task<Scenario> Create(bool registerViaController = false)
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var connection = DatabaseProviderConfiguration.GetRequiredConnectionString(configuration);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options =>
                DatabaseProviderConfiguration.Configure(options, connection));
            services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
            }).AddEntityFrameworkStores<AppDbContext>();
            services.AddScoped<IUserStore<IdentityUser>, RecoveryUserStore>();
            var test = new Scenario(services.BuildServiceProvider());
            try
            {
                await test.Db.Database.MigrateAsync();
                await IdentitySeeder.SeedAsync(test.Services, new ConfigurationBuilder().Build());
                var email = $"{Guid.NewGuid():N}@example.test";
                if (registerViaController)
                {
                    var context = new DefaultHttpContext();
                    context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                    {
                        ["Role"] = "Admin", ["IsAdmin"] = "true", ["Access"] = "Admin"
                    });
                    var controller = new AccountController(test.Users,
                        test.Services.GetRequiredService<SignInManager<IdentityUser>>())
                    {
                        ControllerContext = new ControllerContext { HttpContext = context },
                        TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
                    };
                    var result = await controller.Register(new RegisterViewModel
                    {
                        Email = email, Password = Password, ConfirmPassword = Password
                    });
                    Assert.IsType<RedirectToActionResult>(result);
                    test.User = (await test.Users.FindByEmailAsync(email))!;
                }
                else
                {
                    test.User = new IdentityUser { UserName = email, Email = email };
                    Assert.True((await test.Users.CreateAsync(test.User, Password)).Succeeded);
                    Assert.True((await test.Users.AddToRoleAsync(test.User, IdentitySeeder.UserRole)).Succeeded);
                }

                test.Resource = new MapSubmission
                {
                    OwnerId = test.User.Id, ResourceName = "Recovery integration resource",
                    ResourceType = "Utstyr", LocationName = "Oslo", Latitude = 59.91, Longitude = 10.75,
                    CreatedAtUtc = DateTime.UtcNow
                };
                test.Db.MapSubmissions.Add(test.Resource);
                await test.Db.SaveChangesAsync();
                return test;
            }
            catch
            {
                await test.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Resource is not null)
                await Db.MapSubmissions.Where(resource => resource.Id == Resource.Id).ExecuteDeleteAsync();
            if (User is not null)
                await Db.Users.Where(user => user.Id == User.Id).ExecuteDeleteAsync();
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class RecoveryUserStore(AppDbContext db, IdentityErrorDescriber errors)
        : UserStore<IdentityUser, IdentityRole, AppDbContext>(db, errors)
    {
        public bool RejectStampChanges { get; set; }

        public override Task SetSecurityStampAsync(IdentityUser user, string stamp,
            CancellationToken cancellationToken = default)
        {
            if (RejectStampChanges)
                throw new InvalidOperationException("Simulated stamp update failure.");
            return base.SetSecurityStampAsync(user, stamp, cancellationToken);
        }
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
