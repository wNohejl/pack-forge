using Microsoft.EntityFrameworkCore;
using PackForge.Core;

namespace PackForge.Web.Data;

public class PackForgeDbContext(DbContextOptions<PackForgeDbContext> options) : DbContext(options)
{
    public DbSet<Upload> Uploads => Set<Upload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
