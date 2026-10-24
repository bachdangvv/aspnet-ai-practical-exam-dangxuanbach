namespace SmartShop.Api.Models;

public sealed class Review
{
    public int ReviewId { get; set; }
    public int ProductId { get; set; }
    public string CustomerName { get; set; } = "";
    public string Content { get; set; } = "";
    public int Rating { get; set; }
    public string Sentiment { get; set; } = "";
    public double Score { get; set; }
    public string Summary { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Product Product { get; set; } = null!;
}
