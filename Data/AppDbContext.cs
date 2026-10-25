using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Data;

/// <summary>
/// Entity Framework Core context for Identity accounts and owned submissions.
/// </summary>
public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<MapSubmission> MapSubmissions => Set<MapSubmission>();
    public DbSet<OperationalNeed> OperationalNeeds => Set<OperationalNeed>();
    public DbSet<ResourceRequest> ResourceRequests => Set<ResourceRequest>();
    public DbSet<WorkflowAudit> WorkflowAudits => Set<WorkflowAudit>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefreshConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefreshConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RefreshConcurrencyTokens()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified &&
                     (e.Entity is MapSubmission or OperationalNeed or ResourceRequest)))
        {
            entry.Property("ConcurrencyToken").CurrentValue = Guid.NewGuid().ToString();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.Entity<IdentityUser>().Property(user => user.Id).HasMaxLength(127);
        modelBuilder.Entity<IdentityRole>().Property(role => role.Id).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserRole<string>>().Property(role => role.UserId).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserRole<string>>().Property(role => role.RoleId).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserClaim<string>>().Property(claim => claim.UserId).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserLogin<string>>().Property(login => login.UserId).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserLogin<string>>().Property(login => login.LoginProvider).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserLogin<string>>().Property(login => login.ProviderKey).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserToken<string>>().Property(token => token.UserId).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserToken<string>>().Property(token => token.LoginProvider).HasMaxLength(127);
        modelBuilder.Entity<IdentityUserToken<string>>().Property(token => token.Name).HasMaxLength(127);
        modelBuilder.Entity<IdentityRoleClaim<string>>().Property(claim => claim.RoleId).HasMaxLength(127);

        modelBuilder.Entity<FormSubmission>().Property(submission => submission.OwnerId).HasMaxLength(127);
        modelBuilder.Entity<FormSubmission>().HasOne(submission => submission.Owner)
            .WithMany().HasForeignKey(submission => submission.OwnerId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MapSubmission>().Property(submission => submission.OwnerId).HasMaxLength(127);
        modelBuilder.Entity<MapSubmission>().HasOne(submission => submission.Owner)
            .WithMany().HasForeignKey(submission => submission.OwnerId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MapSubmission>().Property(r => r.Availability)
            .HasDefaultValue(ResourceAvailability.Available);
        modelBuilder.Entity<MapSubmission>().Property(r => r.VerificationStatus)
            .HasDefaultValue(ResourceVerificationStatus.Pending);
        modelBuilder.Entity<ResourceRequest>().HasOne(r => r.Resource).WithMany()
            .HasForeignKey(r => r.ResourceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ResourceRequest>().HasOne(r => r.Need).WithMany()
            .HasForeignKey(r => r.NeedId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ResourceRequest>().HasIndex(r => new { r.ResourceId, r.Status });
        modelBuilder.Entity<WorkflowAudit>().HasIndex(a => new { a.ResourceId, a.OccurredAtUtc });
    }
}
