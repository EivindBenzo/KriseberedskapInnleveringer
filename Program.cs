using System.Data.Common;
using HeimevernetInnlevering1.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

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
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();

// New browser sessions must complete the form before using the rest of the site.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isFormPage = path.StartsWithSegments("/Home/Form");
    var isErrorPage = path.StartsWithSegments("/Home/Error");
    var isRegistered = context.Session.GetInt32("RegistrationId").HasValue;

    if (path.StartsWithSegments("/Home") && !isFormPage && !isErrorPage && !isRegistered)
    {
        context.Response.Redirect("/Home/Form");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
