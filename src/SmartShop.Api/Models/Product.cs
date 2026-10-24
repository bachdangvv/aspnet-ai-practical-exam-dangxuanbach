namespace SmartShop.Api.Models;

public sealed class Product
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public string Features { get; set; } = "";
    public string? Description { get; set; }
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
