using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MariaDbIntegrationTests;

public class MariaDbPersistenceTests
{
    [Fact]
    public async Task MigrationsAndSubmissionOperationsRoundTripAgainstMariaDb()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var connectionString =
            DatabaseProviderConfiguration.GetRequiredConnectionString(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProviderConfiguration.Configure(optionsBuilder, connectionString);
        var options = optionsBuilder.Options;
        var marker = Guid.NewGuid().ToString("N");
        var createdAtUtc = new DateTime(2025, 10, 24, 13, 14, 15, 123, DateTimeKind.Utc);
        var form = new FormSubmission
        {
            FirstName = "Åse",
            LastName = "Østby",
            Email = $"{marker}@example.test",
            Description = "Melding med norske tegn: æ, ø og å.",
            CreatedAtUtc = createdAtUtc
        };
        var map = new MapSubmission
        {
            ResourceName = $"Ressurs Ø {marker}",
            ResourceType = "Utstyr",
            LocationName = "Kristiansand sør",
            Latitude = 58.1467,
            Longitude = 7.9956,
            CreatedAtUtc = createdAtUtc
        };

        try
        {
            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.FormSubmissions.Add(form);
                db.MapSubmissions.Add(map);
                await db.SaveChangesAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var savedForm = await db.FormSubmissions
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == form.Id);
                Assert.Equal("Åse", savedForm.FirstName);
                Assert.Equal("Østby", savedForm.LastName);
                Assert.Equal("Melding med norske tegn: æ, ø og å.", savedForm.Description);
                Assert.Null(savedForm.LocationName);
                Assert.Null(savedForm.Latitude);
                Assert.Null(savedForm.Longitude);
                Assert.Equal(createdAtUtc, savedForm.CreatedAtUtc);

                var savedMap = await db.MapSubmissions.SingleAsync(x => x.Id == map.Id);
                Assert.Equal("Kristiansand sør", savedMap.LocationName);
                Assert.Equal(58.1467, savedMap.Latitude);
                Assert.Equal(7.9956, savedMap.Longitude);
                Assert.Equal(createdAtUtc, savedMap.CreatedAtUtc);

                savedMap.ResourceName = $"Kjøretøy – {marker}";
                await db.SaveChangesAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var updatedMap = await db.MapSubmissions
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == map.Id);
                Assert.Equal($"Kjøretøy – {marker}", updatedMap.ResourceName);

                db.MapSubmissions.Remove(updatedMap);
                await db.SaveChangesAsync();
                Assert.False(await db.MapSubmissions.AnyAsync(x => x.Id == map.Id));
            }
        }
        finally
        {
            await using var db = new AppDbContext(options);
            if (map.Id != 0)
            {
                var remainingMap = await db.MapSubmissions.FindAsync(map.Id);
                if (remainingMap is not null)
                {
                    db.MapSubmissions.Remove(remainingMap);
                }
            }

            if (form.Id != 0)
            {
                var remainingForm = await db.FormSubmissions.FindAsync(form.Id);
                if (remainingForm is not null)
                {
                    db.FormSubmissions.Remove(remainingForm);
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
