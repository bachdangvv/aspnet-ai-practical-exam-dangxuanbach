using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartShop.Api.Contracts;

public sealed record AnalyzeReviewRequest(
    [Range(1, int.MaxValue)] int ProductId,
    [Required, StringLength(64)] string CustomerName,
    [Required, StringLength(1000)] string Content,
    [Range(1, 5)] int Rating);

public sealed record ChatRequest(
    [Required, StringLength(128), RegularExpression(@"^[a-zA-Z0-9_-]+$")]
    string SessionId,
    [Required, StringLength(2000)] string Message);

public sealed record ProductResponse(int ProductId, string ProductName, string Category,
    decimal Price, string Features, string? Description);

public sealed record ReviewResponse(int ReviewId, int ProductId, string CustomerName,
    string Content, int Rating, string Sentiment, double Score, string Summary, DateTime CreatedAt);

public sealed record ReviewReportRow(int No, int ProductId, string ProductName,
    int TotalReviews, int Positive, int Negative, int Neutral, double AverageRating);

public sealed record ChatResponse(string SessionId, string Reply);

public sealed record ReviewAnalysis(
    [property: JsonRequired] string Sentiment,
    [property: JsonRequired] double Score,
    [property: JsonRequired] string Summary);
