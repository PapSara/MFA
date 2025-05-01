using Microsoft.EntityFrameworkCore;
using MFAAuthApp.Models; // adaugă asta!!

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
