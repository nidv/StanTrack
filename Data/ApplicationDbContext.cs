using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StanTrack.Models;

namespace StanTrack.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Celebrity> Celebrities => Set<Celebrity>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<Follow> Follows => Set<Follow>();
        public DbSet<Comment> Comments => Set<Comment>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Follow>()
                .HasIndex(f => new { f.UserId, f.CelebrityId })
                .IsUnique();

            builder.Entity<Event>()
                .HasIndex(e => new { e.Source, e.SourceExternalId })
                .IsUnique()
                .HasFilter("[SourceExternalId] IS NOT NULL");

            builder.Entity<Celebrity>()
                .HasOne(c => c.CreatedBy)
                .WithMany(u => u.CreatedCelebrities)
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Event>()
                .HasOne(e => e.Celebrity)
                .WithMany(c => c.Events)
                .HasForeignKey(e => e.CelebrityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Follow>()
                .HasOne(f => f.Celebrity)
                .WithMany(c => c.Follows)
                .HasForeignKey(f => f.CelebrityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Follow>()
                .HasOne(f => f.User)
                .WithMany(u => u.Follows)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Comment>()
                .HasOne(c => c.Event)
                .WithMany(e => e.Comments)
                .HasForeignKey(c => c.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Comment>()
                .HasOne(c => c.User)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
