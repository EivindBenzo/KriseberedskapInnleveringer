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
