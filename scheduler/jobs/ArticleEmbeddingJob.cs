using Microsoft.EntityFrameworkCore;
using scheduler.data;
using scheduler.services;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using TickerQ.Utilities.Base;

namespace scheduler.jobs;

public sealed class ArticleEmbeddingJob
{
    public const string FunctionName = "GenerateArticleEmbeddings";

    private const int DefaultBatchSize = 200;

    private static readonly ActivitySource ActivitySource = new("scheduler.jobs");
    private static readonly Meter Meter = new("scheduler.jobs");
    private static readonly Counter<long> EmbeddingsUpdated =
        Meter.CreateCounter<long>("scheduler.embeddings.updated");
    private static readonly Counter<long> EmbeddingsFailed =
        Meter.CreateCounter<long>("scheduler.embeddings.failed");
    private static readonly Counter<long> EmbeddingsAttempted =
        Meter.CreateCounter<long>("scheduler.embeddings.attempted");
    private static readonly SemaphoreSlim JobLock = new(1, 1);

    private readonly AppDbContext _dbContext;
    private readonly ArticleEmbeddingService _embeddingService;
    private readonly ILogger<ArticleEmbeddingJob> _logger;
    private readonly IConfiguration _configuration;

    public ArticleEmbeddingJob(
        AppDbContext dbContext,
        ArticleEmbeddingService embeddingService,
        ILogger<ArticleEmbeddingJob> logger,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _logger = logger;
        _configuration = configuration;
    }

    [TickerFunction(FunctionName)]
    public async Task GenerateMissingEmbeddings(
        TickerFunctionContext context,
        CancellationToken cancellationToken)
    {
        if (!await JobLock.WaitAsync(0, cancellationToken))
        {
            _logger.LogInformation("Embedding job already running. Skipping this tick.");
            return;
        }

        using var activity = ActivitySource.StartActivity(FunctionName);
        var batchSize = _configuration.GetValue<int?>("Scheduler:EmbeddingBatchSize") ?? DefaultBatchSize;
        activity?.SetTag("batch.size", batchSize);

        _logger.LogInformation("Starting embedding batch. BatchSize: {BatchSize}", batchSize);
        try
        {
            var queryStarted = Stopwatch.StartNew();
            var articles = await _dbContext.Articles
                .Where(article => article.Embedding == null)
                .OrderBy(article => article.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            queryStarted.Stop();

            activity?.SetTag("articles.count", articles.Count);
            activity?.SetTag("query.ms", queryStarted.ElapsedMilliseconds);

            _logger.LogInformation(
                "Loaded {Count} articles to embed in {ElapsedMs} ms.",
                articles.Count,
                queryStarted.ElapsedMilliseconds);

            if (articles.Count == 0)
            {
                _logger.LogInformation("No articles pending embeddings.");
                return;
            }

            var updatedCount = 0;
            var failedCount = 0;
            foreach (var article in articles)
            {
                var hadEmbedding = article.Embedding is not null;

                await _embeddingService.PopulateEmbeddingsAsync(article, cancellationToken);

                if (!hadEmbedding && article.Embedding is not null)
                {
                    updatedCount++;
                }
                else if (!hadEmbedding)
                {
                    failedCount++;
                }
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Failed to save embeddings for {Count} articles.", articles.Count);
                throw;
            }

            EmbeddingsAttempted.Add(articles.Count);
            EmbeddingsUpdated.Add(updatedCount);
            EmbeddingsFailed.Add(failedCount);
            activity?.SetTag("updated.count", updatedCount);
            activity?.SetTag("failed.count", failedCount);
            _logger.LogInformation(
                "Processed {Count} articles for embeddings. Updated: {Updated}. Failed: {Failed}",
                articles.Count,
                updatedCount,
                failedCount);

            if (updatedCount == 0 && failedCount > 0)
            {
                _logger.LogWarning(
                    "No embeddings were generated for this batch. Check embedding provider/model configuration.");
            }
        }
        finally
        {
            JobLock.Release();
        }
    }
}
