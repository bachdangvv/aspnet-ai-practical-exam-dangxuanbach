using Microsoft.EntityFrameworkCore;
using SmartShop.Api.Data;
using SmartShop.Api.Infrastructure;
using SmartShop.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddDbContext<ShopDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("SmartShop") ??
    throw new InvalidOperationException("Configure ConnectionStrings:SmartShop."),
    sql => sql.EnableRetryOnFailure(3)));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddHttpClient<IGeminiClient, GeminiClient>(http => http.Timeout = TimeSpan.FromSeconds(60))
    .RedactLoggedHeaders(["x-goog-api-key"]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ChatSessionStore>();

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();

// Exposes the entry point to WebApplicationFactory integration tests.
public partial class Program;
