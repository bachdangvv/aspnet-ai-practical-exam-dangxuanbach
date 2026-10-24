using Microsoft.EntityFrameworkCore;
using SmartShop.Api.Models;

namespace SmartShop.Api.Data;

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var products = modelBuilder.Entity<Product>();
        products.ToTable("Product", table => table.HasCheckConstraint("CK_Product_Price", "[Price] >= 0"));
        products.HasKey(p => p.ProductId);
        products.Property(p => p.ProductId).ValueGeneratedNever();
        products.Property(p => p.ProductName).HasMaxLength(128).IsRequired();
        products.Property(p => p.Category).HasMaxLength(64).IsRequired();
        products.Property(p => p.Price).HasPrecision(18, 2);
        products.Property(p => p.Features).HasMaxLength(512).IsRequired();

        var reviews = modelBuilder.Entity<Review>();
        reviews.ToTable("Review", table =>
        {
            table.HasCheckConstraint("CK_Review_Rating", "[Rating] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_Review_Score", "[Score] >= 0 AND [Score] <= 1");
            table.HasCheckConstraint("CK_Review_Sentiment", "[Sentiment] IN ('Positive', 'Negative', 'Neutral')");
        });
        reviews.HasKey(r => r.ReviewId);
        reviews.Property(r => r.CustomerName).HasMaxLength(64).IsRequired();
        reviews.Property(r => r.Content).HasMaxLength(1000).IsRequired();
        reviews.Property(r => r.Sentiment).HasMaxLength(16).IsRequired();
        reviews.Property(r => r.Summary).HasMaxLength(256).IsRequired();
        reviews.Property(r => r.CreatedAt).HasColumnType("datetime");
        reviews.HasOne(r => r.Product).WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);

        products.HasData(
            new Product { ProductId = 1, ProductName = "Galaxy S25", Category = "Phone",
                Price = 22990000m, Features = "6.2 inch AMOLED, Snapdragon 8 Elite, 50MP camera" },
            new Product { ProductId = 2, ProductName = "MacBook Air M4", Category = "Laptop",
                Price = 28490000m, Features = "13.6 inch, 16GB RAM, 18 hours battery" },
            new Product { ProductId = 3, ProductName = "AirPods Pro 3", Category = "Accessory",
                Price = 6790000m, Features = "Active noise cancellation, USB C, IP57" },
            new Product { ProductId = 4, ProductName = "Xiaomi Redmi Note 15", Category = "Phone",
                Price = 6490000m, Features = "120Hz display, 5500mAh battery, 108MP camera" });
    }
}
