using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AnthropicProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public AnthropicProxyController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpPost("anthropic/analyze")]
    public async Task<ActionResult<AnthropicAnalyzeResponse>> Analyze([FromBody] AnthropicAnalyzeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt is required.");
        }

        var provider = (_configuration["AiProvider:Active"] ?? _configuration["AiProvider:Provider"] ?? "Anthropic").Trim();

        var model = request.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            model = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                ? await GetBestOllamaModelAsync()
                : (_configuration["AiProvider:AnthropicModel"] ?? _configuration["AiProvider:DefaultModel"] ?? "claude-opus-4-5");
        }

        if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return await AnalyzeWithOllamaAsync(request, model!);
        }

        return await AnalyzeWithAnthropicAsync(request, model!);
    }

    private async Task<string> GetBestOllamaModelAsync()
    {
        var ollamaBaseUrl = (_configuration["AiProvider:OllamaBaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        var configuredModel = _configuration["AiProvider:OllamaModel"] ?? _configuration["AiProvider:DefaultModel"] ?? "llama3.1:8b-instruct";
        var useBestAvailable = bool.TryParse(_configuration["AiProvider:OllamaUseBestAvailable"], out var parsed) && parsed;

        if (!useBestAvailable)
        {
            return configuredModel;
        }

        var preferredModels = _configuration.GetSection("AiProvider:OllamaBestModelPriority").Get<string[]>()
            ?? new[]
            {
                "llama3.3:70b-instruct-q4_K_M",
                "llama3.3:70b-instruct",
                "qwen2.5:72b-instruct",
                "qwen2.5:32b-instruct",
                "llama3.1:70b-instruct",
                "qwen2.5:14b-instruct",
                "llama3.1:8b-instruct"
            };

        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{ollamaBaseUrl}/api/tags");
            if (!response.IsSuccessStatusCode)
            {
                return configuredModel;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            if (!doc.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
            {
                return configuredModel;
            }

            var installedModels = modelsElement
                .EnumerateArray()
                .Where(x => x.TryGetProperty("name", out _))
                .Select(x => x.GetProperty("name").GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();

            foreach (var preferred in preferredModels)
            {
                var match = installedModels.FirstOrDefault(m => string.Equals(m, preferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }

            return installedModels.FirstOrDefault() ?? configuredModel;
        }
        catch
        {
            return configuredModel;
        }
    }

    private async Task<ActionResult<AnthropicAnalyzeResponse>> AnalyzeWithAnthropicAsync(
        AnthropicAnalyzeRequest request,
        string model)
    {
        var apiKey = _configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return StatusCode(500, "Anthropic API key is not configured on the server.");
        }

        var maxTokens = request.MaxTokens <= 0 ? 2000 : request.MaxTokens;

        var payload = new
        {
            model,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = request.Prompt } }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var client = _httpClientFactory.CreateClient();
        var response = await client.SendAsync(message);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, new
            {
                message = "Anthropic request failed.",
                status = (int)response.StatusCode,
                details = responseContent
            });
        }

        using var doc = JsonDocument.Parse(responseContent);
        var contentArray = doc.RootElement.GetProperty("content");
        if (contentArray.GetArrayLength() == 0)
        {
            return StatusCode(502, "Anthropic response did not contain text content.");
        }

        var text = contentArray[0].GetProperty("text").GetString() ?? string.Empty;
        return Ok(new AnthropicAnalyzeResponse
        {
            Text = text,
            Model = model
        });
    }

    private async Task<ActionResult<AnthropicAnalyzeResponse>> AnalyzeWithOllamaAsync(
        AnthropicAnalyzeRequest request,
        string model)
    {
        var ollamaBaseUrl = (_configuration["AiProvider:OllamaBaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        var maxTokens = request.MaxTokens <= 0 ? 2000 : request.MaxTokens;

        var payload = new
        {
            model,
            prompt = request.Prompt,
            stream = false,
            options = new
            {
                num_predict = maxTokens
            }
        };

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync(
            $"{ollamaBaseUrl}/api/generate",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        var responseContent = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, new
            {
                message = "Ollama request failed.",
                status = (int)response.StatusCode,
                details = responseContent
            });
        }

        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;
        var text = root.TryGetProperty("response", out var responseText)
            ? responseText.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return StatusCode(502, "Ollama response did not contain text content.");
        }

        return Ok(new AnthropicAnalyzeResponse
        {
            Text = text,
            Model = model
        });
    }
}

public class AnthropicAnalyzeRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int MaxTokens { get; set; } = 2000;
}

public class AnthropicAnalyzeResponse
{
    public string Text { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
