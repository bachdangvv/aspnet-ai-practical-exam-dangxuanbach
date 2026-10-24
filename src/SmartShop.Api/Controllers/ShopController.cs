using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartShop.Api.Contracts;
using SmartShop.Api.Data;
using SmartShop.Api.Models;
using SmartShop.Api.Services;

namespace SmartShop.Api.Controllers;

[ApiController]
public sealed class ShopController(ShopDbContext db, IGeminiClient gemini, ChatSessionStore sessions) : ControllerBase
{
    [HttpGet("/api/products")]
    public async Task<ActionResult<List<ProductResponse>>> GetProducts(CancellationToken cancellationToken) =>
        await CatalogAsync(cancellationToken);

    [HttpPost("/api/products/{id:int}/generatedescription")]
    public async Task<ActionResult<ProductResponse>> GenerateDescription(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([id], cancellationToken);
        if (product is null)
            return Problem(statusCode: 404, title: "product_not_found", detail: "The product does not exist.");

        var description = await gemini.GenerateAsync(Prompts.DescriptionSystem,
            [new ChatTurn("user", Prompts.DescriptionUser(product))], false, cancellationToken);
        // Enforce the exam's word limit in code as well as in the prompt. Reject rather than save truncated prose.
        if (string.IsNullOrWhiteSpace(description) || description.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries).Length > 80)
            throw GeminiClient.InvalidOutput();
        product.Description = description.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(product);
    }

    [HttpPost("/api/reviews/analyze")]
    public async Task<ActionResult<ReviewResponse>> AnalyzeReview(AnalyzeReviewRequest request,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FindAsync([request.ProductId], cancellationToken);
        if (product is null)
            return Problem(statusCode: 404, title: "product_not_found", detail: "The product does not exist.");
        var json = await gemini.GenerateAsync(Prompts.ReviewSystem,
            [new ChatTurn("user", Prompts.ReviewUser(product, request))], true, cancellationToken);
        var analysis = GeminiClient.ParseReview(json);
        var review = new Review
        {
            ProductId = request.ProductId,
            CustomerName = request.CustomerName.Trim(),
            Content = request.Content.Trim(),
            Rating = request.Rating,
            Sentiment = analysis.Sentiment,
            Score = analysis.Score,
            Summary = analysis.Summary.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(cancellationToken);
        return StatusCode(201, new ReviewResponse(review.ReviewId, review.ProductId, review.CustomerName,
            review.Content, review.Rating, review.Sentiment, review.Score, review.Summary, review.CreatedAt));
    }

    [HttpPost("/api/assistant/chat")]
    public async Task<ActionResult<ChatResponse>> Chat(ChatRequest request, CancellationToken cancellationToken)
    {
        var reply = await sessions.ReplyAsync(request.SessionId, request.Message.Trim(), async history =>
        {
            var products = await CatalogAsync(cancellationToken);
            return await gemini.GenerateAsync(Prompts.AssistantContext(products), history, false, cancellationToken);
        }, cancellationToken);
        return new ChatResponse(request.SessionId, reply);
    }

    [HttpGet("/api/reports/reviews")]
    public async Task<ActionResult<List<ReviewReportRow>>> GetReviewReport(CancellationToken cancellationToken)
    {
        // Correlated aggregates include products with zero reviews and execute entirely in SQL.
        var rows = await db.Products.AsNoTracking().OrderBy(p => p.ProductId).Select(p => new
        {
            p.ProductId,
            p.ProductName,
            TotalReviews = p.Reviews.Count,
            Positive = p.Reviews.Count(r => r.Sentiment == "Positive"),
            Negative = p.Reviews.Count(r => r.Sentiment == "Negative"),
            Neutral = p.Reviews.Count(r => r.Sentiment == "Neutral"),
            AverageRating = p.Reviews.Average(r => (double?)r.Rating) ?? 0
        }).ToListAsync(cancellationToken);

        return rows.Select((row, index) => new ReviewReportRow(index + 1, row.ProductId, row.ProductName,
            row.TotalReviews, row.Positive, row.Negative, row.Neutral,
            Math.Round(row.AverageRating, 1, MidpointRounding.AwayFromZero))).ToList();
    }

    private Task<List<ProductResponse>> CatalogAsync(CancellationToken cancellationToken) =>
        db.Products.AsNoTracking().OrderBy(p => p.ProductId).Select(p => new ProductResponse(
            p.ProductId, p.ProductName, p.Category, p.Price, p.Features, p.Description)).ToListAsync(cancellationToken);

    private static ProductResponse ToResponse(Product p) =>
        new(p.ProductId, p.ProductName, p.Category, p.Price, p.Features, p.Description);
}
