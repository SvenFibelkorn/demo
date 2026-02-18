using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace scheduler.services;

public sealed class OllamaEmbeddingService : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingOptions _options;
    private readonly IReadOnlyDictionary<string, object?> _metadata;

    public OllamaEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IOptions<EmbeddingOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _metadata = new Dictionary<string, object?>
        {
            ["model"] = _options.Ollama.Model
        };
    }

    public string ModelId => _options.Ollama.Model;

    public IReadOnlyDictionary<string, object?> Metadata => _metadata;

    public object? GetService(Type serviceType, object? serviceKey)
    {
        return null;
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values as IList<string> ?? values.ToList();
        if (inputs.Count == 0)
        {
            return new GeneratedEmbeddings<Embedding<float>>(
                Array.Empty<Embedding<float>>());
        }

        var client = _httpClientFactory.CreateClient("ollama");
        var results = new List<Embedding<float>>(inputs.Count);

        foreach (var text in inputs)
        {
            var response = await client.PostAsJsonAsync(
                "api/embeddings",
                new OllamaEmbeddingRequest(_options.Ollama.Model, text),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
                cancellationToken: cancellationToken);

            if (payload?.Embedding is null || payload.Embedding.Length == 0)
            {
                throw new InvalidOperationException("Ollama embeddings response was empty.");
            }

            results.Add(new Embedding<float>(payload.Embedding));
        }

        return new GeneratedEmbeddings<Embedding<float>>(results);
    }

    private sealed record OllamaEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaEmbeddingResponse(
        [property: JsonPropertyName("embedding")] float[] Embedding);

    public void Dispose()
    {
    }
}
