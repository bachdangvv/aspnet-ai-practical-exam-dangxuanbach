using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SmartShop.Api.Contracts;

namespace SmartShop.Api.Services;

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.8-flash";
}

public sealed class AiException(int statusCode, string code, string message, int? retryAfter = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public int? RetryAfter { get; } = retryAfter;
}

public sealed record ChatTurn(string Role, string Text);

public interface IGeminiClient
{
    Task<string> GenerateAsync(string systemInstruction, IReadOnlyList<ChatTurn> turns,
        bool structuredReview, CancellationToken cancellationToken);
}

public sealed class GeminiClient(HttpClient http, IOptions<GeminiOptions> options) : IGeminiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<string> GenerateAsync(string systemInstruction, IReadOnlyList<ChatTurn> turns,
        bool structuredReview, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new AiException(503, "gemini_not_configured",
                "Set Gemini:ApiKey using User Secrets or the Gemini__ApiKey environment variable.");

        var config = new JsonObject
        {
            ["temperature"] = structuredReview ? 0.1 : 0.4,
            ["maxOutputTokens"] = 4096
        };
        if (settings.Model.StartsWith("gemini-2.5-", StringComparison.Ordinal))
            config["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = 0 };
        else if (settings.Model.StartsWith("gemini-3", StringComparison.Ordinal))
            config["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = "low" };
        if (structuredReview)
        {
            config["responseMimeType"] = "application/json";
            // Schema-constrained JSON output in the generateContent REST API.
            config["responseSchema"] = JsonNode.Parse("""
                {
                  "type": "OBJECT",
                  "properties": {
                    "sentiment": { "type": "STRING", "enum": ["Positive", "Negative", "Neutral"] },
                    "score": { "type": "NUMBER", "minimum": 0, "maximum": 1 },
                    "summary": { "type": "STRING", "description": "Faithful summary, at most 256 characters" }
                  },
                  "required": ["sentiment", "score", "summary"]
                }
                """);
        }

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = turns.Select(t => new { role = t.Role, parts = new[] { new { text = t.Text } } }),
            generationConfig = config
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(settings.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        request.Content = JsonContent.Create(payload);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException(504, "gemini_timeout", "Gemini timed out. Please try again.");
        }
        catch (HttpRequestException)
        {
            throw new AiException(502, "gemini_unreachable", "Could not connect to Gemini. Please try again.");
        }

        using (response)
        {
            if ((int)response.StatusCode == 402)
                throw new AiException(503, "gemini_billing_required",
                    "The Gemini project's prepaid credits are depleted. Update billing in Google AI Studio or configure a key for a project with available credits.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta?.TotalSeconds
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 30;
                throw new AiException(429, "gemini_quota_exceeded",
                    "Gemini quota exceeded. Wait before retrying.", (int)Math.Clamp(Math.Ceiling(delay), 1, 3600));
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new AiException(502, "gemini_model_unavailable",
                    "The configured Gemini model is unavailable. Set Gemini:Model to an available Flash model.");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AiException(503, "gemini_authentication_failed",
                    "Gemini denied access. Check the server's API key and its API restrictions.");
            if (!response.IsSuccessStatusCode)
                throw new AiException(502, "gemini_request_failed",
                    "Gemini rejected the request. Check the server's API key, model and quota.");

            try
            {
                using var document = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                    throw InvalidOutput();
                var candidate = candidates[0];
                if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() != "STOP")
                    throw InvalidOutput();
                var parts = candidate.GetProperty("content").GetProperty("parts").EnumerateArray();
                var text = string.Concat(parts
                    .Where(p => !(p.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True))
                    .Where(p => p.TryGetProperty("text", out _))
                    .Select(p => p.GetProperty("text").GetString())).Trim();
                return string.IsNullOrWhiteSpace(text) ? throw InvalidOutput() : text;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw InvalidOutput();
            }
        }
    }

    public static ReviewAnalysis ParseReview(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<ReviewAnalysis>(json, JsonOptions);
            if (result is null || result.Sentiment is not ("Positive" or "Negative" or "Neutral") ||
                !double.IsFinite(result.Score) || result.Score is < 0 or > 1 ||
                string.IsNullOrWhiteSpace(result.Summary) || result.Summary.Length > 256)
                throw InvalidOutput();
            return result;
        }
        catch (JsonException)
        {
            throw InvalidOutput();
        }
    }

    public static AiException InvalidOutput() => new(502, "gemini_invalid_response",
        "Gemini returned an empty, blocked, incomplete or invalid response. Nothing was saved; please try again.");
}
