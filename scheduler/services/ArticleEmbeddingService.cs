using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Pgvector;
using scheduler.models;

namespace scheduler.services;

public sealed class ArticleEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<ArticleEmbeddingService> _logger;
    private readonly int _vectorSize;

    public ArticleEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<EmbeddingOptions> options,
        ILogger<ArticleEmbeddingService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
        _vectorSize = options.Value.VectorSize;
    }

    public async Task PopulateEmbeddingsAsync(Article article, CancellationToken cancellationToken)
    {
        var combinedText = BuildCombinedText(article);
        var embedding = await GenerateEmbeddingAsync(combinedText, cancellationToken);
        if (embedding is not null)
        {
            article.Embedding = new Vector(embedding);
        }
    }

    private static string? BuildCombinedText(Article article)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(article.Headline))
        {
            parts.Add(article.Headline.Trim());
        }

        if (!string.IsNullOrWhiteSpace(article.Description))
        {
            parts.Add(article.Description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(article.Summary))
        {
            parts.Add(article.Summary.Trim());
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join("\n\n", parts);
    }

    private async Task<float[]?> GenerateEmbeddingAsync(string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(
                new[] { text },
                cancellationToken: cancellationToken);

            if (embeddings.Count == 0)
            {
                return null;
            }

            var vector = embeddings[0].Vector.ToArray();
            if (vector.Length == 0)
            {
                return null;
            }

            if (_vectorSize > 0 && vector.Length != _vectorSize)
            {
                _logger.LogWarning(
                    "Embedding size mismatch. Expected {Expected}, got {Actual}.",
                    _vectorSize,
                    vector.Length);
            }

            return vector;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding.");
            return null;
        }
    }
}
