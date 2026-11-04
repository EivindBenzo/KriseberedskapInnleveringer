using System.Security.Claims;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace MariaDbIntegrationTests;

/// <summary>
/// Lager en egen, tom MariaDB-database for hver test, kjører migrasjonene (ER-modellen)
/// og sletter databasen etterpå. Tilkoblingen leses fra MARIADB_*-miljøvariablene.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string Passord = "Testpassord123!";
    private readonly string _adminConnection;
    private readonly string _database;
    public ServiceProvider Services { get; }
    public AsyncServiceScope Scope { get; }
    public AppDbContext Db => Scope.ServiceProvider.GetRequiredService<AppDbContext>();
    public WorkflowService Workflow => Scope.ServiceProvider.GetRequiredService<WorkflowService>();
    public UserManager<Bruker> Users => Scope.ServiceProvider.GetRequiredService<UserManager<Bruker>>();

    private TestDatabase(string adminConnection, string database, ServiceProvider services)
    {
        _adminConnection = adminConnection;
        _database = database;
        Services = services;
        Scope = services.CreateAsyncScope();
    }

    public static async Task<TestDatabase> CreateAsync(bool demoData = false)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var baseConnection = DatabaseProviderConfiguration.GetRequiredConnectionString(configuration);
        var database = "test_" + Guid.NewGuid().ToString("N")[..16];
        var connection = new MySqlConnectionStringBuilder(baseConnection) { Database = database }.ConnectionString;
        var serverVersion = configuration["DATABASE_SERVER_VERSION"];

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => DatabaseProviderConfiguration.Configure(o, connection, serverVersion));
        services.AddIdentity<Bruker, Rolle>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 12;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.Configure<WorkflowOptions>(_ => { });
        services.AddScoped<WorkflowService>();
        services.AddScoped<DashboardService>();
        var provider = services.BuildServiceProvider();

        var test = new TestDatabase(baseConnection, database, provider);
        await test.Db.Database.MigrateAsync();
        var seedConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SEED_DEMO_ACCOUNTS"] = demoData ? "true" : "false",
            ["DEMO_ACCOUNT_PASSWORD"] = Passord
        }).Build();
        await IdentitySeeder.SeedAsync(test.Scope.ServiceProvider, seedConfig);
        return test;
    }

    /// <summary>Oppretter en bruker med én rolle og returnerer en innlogget ClaimsPrincipal for den.</summary>
    public async Task<(Bruker Bruker, ClaimsPrincipal Principal)> BrukerAsync(string rolle, string? navn = null)
    {
        var epost = $"{rolle}-{Guid.NewGuid():N}@test.no".ToLowerInvariant();
        var bruker = new Bruker
        {
            UserName = epost, Email = epost, Fornavn = navn ?? "Test", Etternavn = rolle,
            PhoneNumber = "40000000", EmailConfirmed = true
        };
        var result = await Users.CreateAsync(bruker, Passord);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        await Users.AddToRoleAsync(bruker, rolle);
        return (bruker, Principal(bruker.Id, rolle));
    }

    public static ClaimsPrincipal Principal(int id, params string[] roller)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()) };
        claims.AddRange(roller.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    public async ValueTask DisposeAsync()
    {
        await Scope.DisposeAsync();
        await Services.DisposeAsync();
        await using var connection = new MySqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
        await command.ExecuteNonQueryAsync();
    }
}
