using Microsoft.EntityFrameworkCore;
using PackForge.Core;
using PackForge.Core.Migration;

namespace PackForge.Web.Data;

public class PackForgeDbContext(DbContextOptions<PackForgeDbContext> options) : DbContext(options)
{
    public DbSet<Upload> Uploads => Set<Upload>();
    public DbSet<PackageBuild> PackageBuilds => Set<PackageBuild>();
    public DbSet<MigrationItem> MigrationItems => Set<MigrationItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationItem>(e =>
        {
            e.Property(i => i.SourceSystem).HasMaxLength(64);
            e.Property(i => i.SourcePath).HasMaxLength(1024);
            e.Property(i => i.BlobName).HasMaxLength(1024);
            e.Property(i => i.SourceSha256).HasMaxLength(64);
            e.Property(i => i.BlobSha256).HasMaxLength(64);
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(i => new { i.SourceSystem, i.SourcePath }).IsUnique();
            e.HasIndex(i => i.Status);
        });

        modelBuilder.Entity<PackageBuild>(e =>
        {
            e.Property(b => b.ModelName).HasMaxLength(256);
            e.Property(b => b.ModelSha256).HasMaxLength(64);
            e.Property(b => b.PackageSha256).HasMaxLength(64);
            e.Property(b => b.BlobName).HasMaxLength(1024);
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(b => new { b.ModelName, b.Version }).IsUnique();
            e.HasIndex(b => b.ModelSha256);
        });

        modelBuilder.Entity<Upload>(e =>
        {
            e.Property(u => u.FileName).HasMaxLength(512);
            e.Property(u => u.BlobName).HasMaxLength(1024);
            e.Property(u => u.Sha256).HasMaxLength(64);
            e.Property(u => u.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(u => u.CreatedAt);
        });
    }
}
