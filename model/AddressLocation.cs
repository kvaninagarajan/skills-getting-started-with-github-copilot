using Microsoft.EntityFrameworkCore;

namespace AddressMapper.Model;

public sealed class AddressLocation
{
    public string AddressId { get; set; } = "";
    public string Address { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Status { get; set; } = "notGeocoded";
}

public sealed class AddressDbContext(DbContextOptions<AddressDbContext> options) : DbContext(options)
{
    public DbSet<AddressLocation> Addresses => Set<AddressLocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var address = modelBuilder.Entity<AddressLocation>();
        address.HasKey(x => x.AddressId);
        address.Property(x => x.AddressId).HasMaxLength(100);
        address.Property(x => x.Address).HasMaxLength(500).IsRequired();
    }
}
