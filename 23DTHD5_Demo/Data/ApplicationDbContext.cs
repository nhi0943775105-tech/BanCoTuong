using _23DTHD5_Demo.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace _23DTHD5_Demo.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(x => x.DisplayName).HasMaxLength(30).IsRequired();
            user.HasIndex(x => x.NormalizedEmail).IsUnique()
                .HasDatabaseName("EmailIndex").HasFilter("[NormalizedEmail] IS NOT NULL");
        });
    }
}
