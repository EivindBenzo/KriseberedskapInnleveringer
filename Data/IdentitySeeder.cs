using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;

namespace HeimevernetInnlevering1.Data;

public static class IdentitySeeder
{
    private const string LegacyUserRole = "Bruker";
    private const string LegacyAdminRole = "Admin";
    public const string BootstrapRole = AppRoles.Kriseledelse;

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)));
        }
        await MigrateLegacyRolesAsync(services);
        await BootstrapAdminAsync(services, configuration);
        if (configuration.GetValue<bool>("SEED_DEMO_ACCOUNTS"))
        {
            await SeedDemoAccountsAsync(services, configuration["DEMO_ACCOUNT_PASSWORD"]);
            await DemoDataSeeder.SeedAsync(services);
        }
    }

    public const string DefaultDemoPassword = "Dragoncool22!";

    public static readonly IReadOnlyList<(string Email, string Role)> DemoAccounts =
    [
        ("offentlig@gmail.com", AppRoles.OffentligAktor),
        ("ressurs@gmail.com", AppRoles.Ressursleverandor),
        ("dispatch@gmail.com", AppRoles.DispatchTeam),
        ("kriseledelse@gmail.com", AppRoles.Kriseledelse),
        ("verifisering@gmail.com", AppRoles.Verifiseringsansvarlig)
    ];

    // Creates missing demo accounts and makes sure each one has its role. Existing accounts keep their password.
    public static async Task SeedDemoAccountsAsync(IServiceProvider services, string? password)
    {
        if (string.IsNullOrEmpty(password)) password = DefaultDemoPassword;
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var (email, role) in DemoAccounts)
        {
            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                EnsureSuccess(await users.CreateAsync(user, password));
            }
            else if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                EnsureSuccess(await users.UpdateAsync(user));
            }

            var current = (await users.GetRolesAsync(user)).Where(AppRoles.All.Contains).ToList();
            var stale = current.Where(existing => existing != role).ToList();
            if (stale.Count > 0)
                EnsureSuccess(await users.RemoveFromRolesAsync(user, stale));
            if (!current.Contains(role))
                EnsureSuccess(await users.AddToRoleAsync(user, role));
            if (stale.Count > 0 || !current.Contains(role))
                EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        }
    }

    private static async Task BootstrapAdminAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["ADMIN_EMAIL"]?.Trim();
        var password = configuration["ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrEmpty(password))
            return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Både ADMIN_EMAIL og ADMIN_PASSWORD må angis.");

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);
        if (user != null)
        {
            if (!await users.IsInRoleAsync(user, BootstrapRole))
            {
                // Verify ownership before elevating a previously registered account.
                if (!await users.CheckPasswordAsync(user, password))
                    throw new InvalidOperationException("Kontoen finnes allerede. ADMIN_PASSWORD må samsvare med kontoens passord, eller bruk --promote-admin fra en betrodd terminal.");
                await AdminPromotionCommand.PromoteAsync(services, email);
            }
            return;
        }

        user = new IdentityUser { UserName = email, Email = email };
        EnsureSuccess(await users.CreateAsync(user, password));
        EnsureSuccess(await users.AddToRolesAsync(user, new[] { BootstrapRole }));
    }

    // Accounts from the earlier Bruker/Admin model keep working: Admin becomes Kriseledelse, Bruker becomes Ressursleverandor.
    private static async Task MigrateLegacyRolesAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var (legacy, replacement) in new[]
                 { (LegacyAdminRole, AppRoles.Kriseledelse), (LegacyUserRole, AppRoles.Ressursleverandor) })
        {
            if (!await roles.RoleExistsAsync(legacy)) continue;
            foreach (var user in await users.GetUsersInRoleAsync(legacy))
            {
                if (!await users.IsInRoleAsync(user, replacement))
                    EnsureSuccess(await users.AddToRoleAsync(user, replacement));
                EnsureSuccess(await users.RemoveFromRoleAsync(user, legacy));
                EnsureSuccess(await users.UpdateSecurityStampAsync(user));
            }
            EnsureSuccess(await roles.DeleteAsync((await roles.FindByNameAsync(legacy))!));
        }
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Kunne ikke initialisere brukere og roller. Kontroller administratorkonfigurasjon og passordkrav.");
    }
}
