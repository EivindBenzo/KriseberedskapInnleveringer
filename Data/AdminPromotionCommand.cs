using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

public static class AdminPromotionCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--promote-admin"
            || string.IsNullOrWhiteSpace(args[1]) || args[1].Trim().Length > 256
            || !new EmailAddressAttribute().IsValid(args[1].Trim()))
        {
            Console.Error.WriteLine("Usage: --promote-admin <existing-account-email>");
            return 2;
        }

        try
        {
            // Recovery deliberately skips normal startup, migrations, bootstrap and HTTP hosting.
            var builder = WebApplication.CreateBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            var connection = DatabaseProviderConfiguration.GetRequiredConnectionString(builder.Configuration);
            builder.Services.AddDbContext<AppDbContext>(options =>
                DatabaseProviderConfiguration.Configure(options, connection,
                    builder.Configuration["DATABASE_SERVER_VERSION"]));
            builder.Services.AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>();
            await using var services = builder.Services.BuildServiceProvider();
            await using var scope = services.CreateAsyncScope();
            await PromoteAsync(scope.ServiceProvider, args[1].Trim());
            Console.WriteLine("Kriseledelse access is configured. Sign in again to use it.");
            return 0;
        }
        catch (Exception)
        {
            // Do not print provider/Identity exceptions: they can contain account or connection data.
            Console.Error.WriteLine("Promotion failed. Verify the existing account, database schema and connection configuration.");
            return 1;
        }
    }

    public static async Task PromoteAsync(IServiceProvider services, string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 256
            || !new EmailAddressAttribute().IsValid(email.Trim()))
            throw new InvalidOperationException("A valid existing account is required.");

        var db = services.GetRequiredService<AppDbContext>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var normalizedEmail = users.NormalizeEmail(email.Trim());
        var matches = await users.Users.Where(user => user.NormalizedEmail == normalizedEmail)
            .Take(2).ToListAsync();
        if (matches.Count != 1)
            throw new InvalidOperationException("A unique existing account is required.");

        var user = matches[0];
        if (!await users.IsInRoleAsync(user, IdentitySeeder.BootstrapRole))
        {
            foreach (var role in new[] { IdentitySeeder.BootstrapRole })
            {
                if (!await roles.RoleExistsAsync(role))
                    EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)));
                if (!await users.IsInRoleAsync(user, role))
                    EnsureSuccess(await users.AddToRoleAsync(user, role));
            }

            // Role writes and session revocation commit together, including on stamp update failure.
            EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        }

        await transaction.CommitAsync();
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Role promotion could not be completed.");
    }
}
