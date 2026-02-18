using dotnet.models;
using Microsoft.EntityFrameworkCore;

namespace dotnet.data;

public sealed class AppDbContext : DbContext
{
    private const int EmbeddingDimensions = 768;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Article> Articles { get; set; }
    public DbSet<Organization> Organizations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Article>(entity =>
        {
            entity.HasIndex(a => a.Link).IsUnique();

            entity.Property(a => a.Embedding)
                .HasColumnType($"vector({EmbeddingDimensions})");

            entity.HasOne(a => a.Organization)
                .WithMany()
                .HasForeignKey(a => a.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}