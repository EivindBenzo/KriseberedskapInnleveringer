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
    private const string RetiredVerificationRole = "Verifiseringsansvarlig";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roles = services.GetRequiredService<RoleManager<Rolle>>();
        var users = services.GetRequiredService<UserManager<Bruker>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                EnsureSuccess(await roles.CreateAsync(new Rolle(role)));
        }
        await MergeVerificationIntoDispatchAsync(roles, users);
        await BootstrapAdminAsync(services, configuration);
        if (configuration.GetValue<bool>("SEED_DEMO_ACCOUNTS"))
        {
            await SeedDemoAccountsAsync(services, configuration["DEMO_ACCOUNT_PASSWORD"]);
            await DemoDataSeeder.SeedAsync(services);
        }
    }

    public static readonly IReadOnlyList<(string Email, string Role, string Fornavn, string Etternavn, string Organisasjon, string Phone)> DemoAccounts =
    [
        ("offentlig@gmail.com", AppRoles.OffentligAktor, "Olav", "Offentlig", "Hamar kommune", "400 10 101"),
        ("ressurs@gmail.com", AppRoles.Ressursleverandor, "Rita", "Ressurs", "Innlandet Maskin AS", "400 10 102"),
        ("dispatch@gmail.com", AppRoles.DispatchTeam, "Dina", "Dispatch", "HV-distrikt Innlandet", "400 10 103"),
        ("kriseledelse@gmail.com", AppRoles.Kriseledelse, "Kari", "Kriseledelse", "Statsforvalteren i Innlandet", "400 10 104"),
        ("logistikk@gmail.com", AppRoles.DispatchTeam, "Ola", "Logistikk", "Sivilforsvaret Innlandet", "400 10 105"),
        ("politi@gmail.com", AppRoles.Kriseledelse, "Eva", "Nødvendig", "Innlandet politidistrikt", "400 10 106"),
        ("energi@gmail.com", AppRoles.Ressursleverandor, "Terje", "Støtte", "Innlandet Energi AS", "400 10 107"),
        ("transport@gmail.com", AppRoles.Ressursleverandor, "Monica", "Løft", "Innlandet Transport AS", "400 10 108"),
        ("kommune@gmail.com", AppRoles.Kriseledelse, "Håkon", "Kommune", "Lillehammer kommune", "400 10 109"),
        ("samband@gmail.com", AppRoles.DispatchTeam, "Anne", "Samband",         "Røde Kors Innlandet", "400 10 110"),
                ("bergen@gmail.com", AppRoles.OffentligAktor, "Bjørg", "Bergen", "Bergen kommune", "400 10 111"),
                ("tromso@gmail.com", AppRoles.OffentligAktor, "Tor", "Tromsø", "Tromsø kommune", "400 10 112"),
                ("trondheim@gmail.com", AppRoles.OffentligAktor, "Tuva", "Trondheim", "Trondheim kommune", "400 10 113"),
                ("stavanger@gmail.com", AppRoles.OffentligAktor, "Sander", "Stavanger", "Stavanger kommune", "400 10 114"),
                ("vest@gmail.com", AppRoles.Ressursleverandor, "Vegard", "Vest", "Vestland Beredskap AS", "400 10 115"),
                ("nord@gmail.com", AppRoles.Ressursleverandor, "Nina", "Nord", "Nordnorsk Maskin AS", "400 10 116")    ];

    private static async Task MergeVerificationIntoDispatchAsync(RoleManager<Rolle> roles, UserManager<Bruker> users)
    {
        if (!await roles.RoleExistsAsync(RetiredVerificationRole)) return;

        foreach (var user in await users.GetUsersInRoleAsync(RetiredVerificationRole))
        {
            if (!await users.IsInRoleAsync(user, AppRoles.DispatchTeam))
                EnsureSuccess(await users.AddToRoleAsync(user, AppRoles.DispatchTeam));
            EnsureSuccess(await users.RemoveFromRoleAsync(user, RetiredVerificationRole));
            EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        }

        var retiredRole = await roles.FindByNameAsync(RetiredVerificationRole);
        if (retiredRole is not null)
            EnsureSuccess(await roles.DeleteAsync(retiredRole));
    }

    /// <summary>Oppretter manglende demokontoer og sørger for riktig rolle. Eksisterende kontoer beholder passordet.</summary>
    public static async Task SeedDemoAccountsAsync(IServiceProvider services, string? password)
    {
        if (string.IsNullOrEmpty(password)) password = DefaultDemoPassword;
        var users = services.GetRequiredService<UserManager<Bruker>>();
        foreach (var (email, role, fornavn, etternavn, organisasjon, phone) in DemoAccounts)
        {
            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new Bruker
                {
                    UserName = email, Email = email, EmailConfirmed = true, Fornavn = fornavn, Etternavn = etternavn,
                    Organisasjon = organisasjon, PhoneNumber = phone
                };
                EnsureSuccess(await users.CreateAsync(user, password));
            }
            else if (user.PhoneNumber != phone)
            {
                user.PhoneNumber = phone;
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
