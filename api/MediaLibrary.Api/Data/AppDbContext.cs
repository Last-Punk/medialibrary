using Microsoft.EntityFrameworkCore;
using MediaLibrary.Api.Models.Entities;

namespace MediaLibrary.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ViewingSession> ViewingSessions => Set<ViewingSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaItem>()
            .HasMany(m => m.Genres)
            .WithMany(g => g.MediaItems)
            .UsingEntity<Dictionary<string, object>>(
                "MediaItemGenre",
                j => j.HasOne<Genre>().WithMany().HasForeignKey("GenreId"),
                j => j.HasOne<MediaItem>().WithMany().HasForeignKey("MediaItemId"));

        modelBuilder.Entity<MediaItem>()
            .HasMany(m => m.Tags)
            .WithMany(t => t.MediaItems)
            .UsingEntity<Dictionary<string, object>>(
                "MediaItemTag",
                j => j.HasOne<Tag>().WithMany().HasForeignKey("TagId"),
                j => j.HasOne<MediaItem>().WithMany().HasForeignKey("MediaItemId"));

        modelBuilder.Entity<ViewingSession>()
            .HasOne(s => s.MediaItem)
            .WithMany(m => m.Sessions)
            .HasForeignKey(s => s.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
