using HeimevernetInnlevering1.Models;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>
/// Entity Framework Core context for form and map submissions.
/// This adapts the EF approach from the Marius branch to Oliver's MVC application.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<MapSubmission> MapSubmissions => Set<MapSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4");
    }
}
