using System.Data.Common;
using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

if (args.Any(arg => arg.StartsWith("--promote-admin", StringComparison.Ordinal)))
{
    Environment.ExitCode = await AdminPromotionCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.Configure<HeimevernetInnlevering1.Models.WorkflowOptions>(builder.Configuration.GetSection("Workflow"));
builder.Services.AddScoped<WorkflowService>();
builder.Services.Configure<GeocodingOptions>(builder.Configuration.GetSection("Geocoding"));
builder.Services.AddSingleton<IPlaceGeocoder>(services => new PlaceGeocoder(
    new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }),
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeocodingOptions>>()));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "Heimevernet.Identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});
// Validate role changes and revoked sessions on the very next authenticated request.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
var keyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet", "DataProtection-Keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("HeimevernetInnlevering");

var connectionString = DatabaseProviderConfiguration.GetRequiredConnectionString(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(options =>
    DatabaseProviderConfiguration.Configure(options, connectionString));

var app = builder.Build();

// Wait briefly for MariaDB during container startup, then initialize the schema from migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    const int maximumAttempts = 10;
    var connected = false;

    for (var attempt = 1; attempt <= maximumAttempts; attempt++)
    {
        try
        {
            connected = await db.Database.CanConnectAsync();
        }
        catch (DbException)
        {
            connected = false;
        }

        if (connected)
        {
            break;
        }

        if (attempt < maximumAttempts)
        {
            app.Logger.LogWarning(
                "MariaDB is not ready (attempt {Attempt} of {MaximumAttempts}); retrying in 2 seconds.",
                attempt,
                maximumAttempts);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    if (!connected)
    {
        throw new InvalidOperationException(
            $"Could not connect to MariaDB after {maximumAttempts} attempts. Check the database connection configuration and availability.");
    }

    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, builder.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
