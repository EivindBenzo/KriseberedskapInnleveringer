using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;

namespace HeimevernetInnlevering1.Data;

/// <summary>
/// Oppretter rollene ved oppstart, eventuelt en første kriseledelse-konto (ADMIN_EMAIL/ADMIN_PASSWORD)
/// og demokontoer med demodata (SEED_DEMO_ACCOUNTS=true).
/// </summary>
public static class IdentitySeeder
{
    public const string BootstrapRole = AppRoles.Kriseledelse;
    public const string DefaultDemoPassword = "Dragoncool22!";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roles = services.GetRequiredService<RoleManager<Rolle>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                EnsureSuccess(await roles.CreateAsync(new Rolle(role)));
        }
        await BootstrapAdminAsync(services, configuration);
        if (configuration.GetValue<bool>("SEED_DEMO_ACCOUNTS"))
        {
            await SeedDemoAccountsAsync(services, configuration["DEMO_ACCOUNT_PASSWORD"]);
            await DemoDataSeeder.SeedAsync(services);
        }
    }

    public static readonly IReadOnlyList<(string Email, string Role, string Fornavn, string Etternavn, string Organisasjon)> DemoAccounts =
    [
        ("offentlig@gmail.com", AppRoles.OffentligAktor, "Olav", "Offentlig", "Hamar kommune"),
        ("ressurs@gmail.com", AppRoles.Ressursleverandor, "Rita", "Ressurs", "Innlandet Maskin AS"),
        ("dispatch@gmail.com", AppRoles.DispatchTeam, "Dina", "Dispatch", "HV-distrikt Innlandet"),
        ("kriseledelse@gmail.com", AppRoles.Kriseledelse, "Kari", "Kriseledelse", "Statsforvalteren i Innlandet"),
        ("verifisering@gmail.com", AppRoles.Verifiseringsansvarlig, "Vegard", "Verifisering", "HV-distrikt Innlandet")
    ];

    /// <summary>Oppretter manglende demokontoer og sørger for riktig rolle. Eksisterende kontoer beholder passordet.</summary>
    public static async Task SeedDemoAccountsAsync(IServiceProvider services, string? password)
    {
        if (string.IsNullOrEmpty(password)) password = DefaultDemoPassword;
        var users = services.GetRequiredService<UserManager<Bruker>>();
        foreach (var (email, role, fornavn, etternavn, organisasjon) in DemoAccounts)
        {
            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new Bruker
                {
                    UserName = email, Email = email, EmailConfirmed = true, Fornavn = fornavn, Etternavn = etternavn,
                    Organisasjon = organisasjon, PhoneNumber = "40000000"
                };
                EnsureSuccess(await users.CreateAsync(user, password));
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

        var users = services.GetRequiredService<UserManager<Bruker>>();
        var user = await users.FindByEmailAsync(email);
        if (user != null)
        {
            if (!await users.IsInRoleAsync(user, BootstrapRole))
            {
                // Sjekk at det er riktig konto før den får kriseledelse-tilgang.
                if (!await users.CheckPasswordAsync(user, password))
                    throw new InvalidOperationException("Kontoen finnes allerede. ADMIN_PASSWORD må samsvare med kontoens passord, eller bruk --promote-admin fra en betrodd terminal.");
                await AdminPromotionCommand.PromoteAsync(services, email);
            }
            return;
        }

        user = new Bruker { UserName = email, Email = email, EmailConfirmed = true, Fornavn = "Administrator", Etternavn = "Kriseledelse" };
        EnsureSuccess(await users.CreateAsync(user, password));
        EnsureSuccess(await users.AddToRolesAsync(user, new[] { BootstrapRole }));
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Kunne ikke initialisere brukere og roller. Kontroller administratorkonfigurasjon og passordkrav.");
    }
}
