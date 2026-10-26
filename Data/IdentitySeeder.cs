using Microsoft.AspNetCore.Identity;

namespace HeimevernetInnlevering1.Data;

public static class IdentitySeeder
{
    public const string UserRole = "Bruker";
    public const string AdminRole = "Admin";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { UserRole, AdminRole })
        {
            if (!await roles.RoleExistsAsync(role))
                EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)));
        }

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
            if (!await users.IsInRoleAsync(user, AdminRole))
            {
                // Verify ownership before elevating a previously registered account.
                if (!await users.CheckPasswordAsync(user, password))
                    throw new InvalidOperationException("Administratorkontoen finnes allerede. ADMIN_PASSWORD må samsvare med kontoens passord, eller bruk --promote-admin fra en betrodd terminal.");
                await AdminPromotionCommand.PromoteAsync(services, email);
            }
            return;
        }

        user = new IdentityUser { UserName = email, Email = email };
        EnsureSuccess(await users.CreateAsync(user, password));
        EnsureSuccess(await users.AddToRolesAsync(user, new[] { UserRole, AdminRole }));
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Kunne ikke initialisere brukere og roller. Kontroller administratorkonfigurasjon og passordkrav.");
    }
}
