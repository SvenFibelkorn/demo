using scheduler.jobs;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace scheduler.services;

public sealed class SchedulerBootstrapper : IHostedService
{
    private const string DefaultCronExpression = "0 0 * * * *";
    private const string IngestionCronKey = "Scheduler:CronExpression";
    private const string EmbeddingCronKey = "Scheduler:EmbeddingCronExpression";

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SchedulerBootstrapper> _logger;

    public SchedulerBootstrapper(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<SchedulerBootstrapper> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var cronManager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var timeManager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();

        var ingestionCron = _configuration[IngestionCronKey] ?? DefaultCronExpression;
        var embeddingCron = _configuration[EmbeddingCronKey] ?? ingestionCron;

        await EnsureCronScheduledAsync(
            cronManager,
            RssIngestionJob.FunctionName,
            "Ingest RSS feeds",
            ingestionCron,
            cancellationToken);

        await EnsureCronScheduledAsync(
            cronManager,
            ArticleEmbeddingJob.FunctionName,
            "Generate article embeddings",
            embeddingCron,
            cancellationToken);

        if (ShouldRunOnce())
        {
            await ScheduleRunOnceAsync(timeManager, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureCronScheduledAsync(
        ICronTickerManager<CronTickerEntity> cronManager,
        string functionName,
        string description,
        string cronExpression,
        CancellationToken cancellationToken)
    {
        var result = await cronManager.AddAsync(new CronTickerEntity
        {
            Function = functionName,
            Description = description,
            Expression = cronExpression
        });

        if (result.IsSucceeded)
        {
            _logger.LogInformation("Scheduled cron job with ID {JobId}", result.Result.Id);
        }
        else
        {
            _logger.LogWarning("Failed to schedule cron job: {Error}", result.Exception?.Message);
        }
    }

    private async Task ScheduleRunOnceAsync(
        ITimeTickerManager<TimeTickerEntity> timeManager,
        CancellationToken cancellationToken)
    {
        var ingestionResult = await timeManager.AddAsync(new TimeTickerEntity
        {
            Function = RssIngestionJob.FunctionName,
            Description = "Manual run-once RSS ingestion",
            ExecutionTime = DateTime.UtcNow.AddSeconds(5)
        });

        if (ingestionResult.IsSucceeded)
        {
            _logger.LogInformation("Scheduled run-once job with ID {JobId}", ingestionResult.Result.Id);
        }
        else
        {
            _logger.LogWarning("Failed to schedule run-once job: {Error}", ingestionResult.Exception?.Message);
        }

        var embeddingResult = await timeManager.AddAsync(new TimeTickerEntity
        {
            Function = ArticleEmbeddingJob.FunctionName,
            Description = "Manual run-once embedding generation",
            ExecutionTime = DateTime.UtcNow.AddSeconds(5)
        });

        if (embeddingResult.IsSucceeded)
        {
            _logger.LogInformation("Scheduled run-once job with ID {JobId}", embeddingResult.Result.Id);
        }
        else
        {
            _logger.LogWarning("Failed to schedule run-once job: {Error}", embeddingResult.Exception?.Message);
        }
    }

    private bool ShouldRunOnce()
    {
        if (_configuration.GetValue<bool>("Scheduler:RunOnceOnStartup"))
        {
            return true;
        }

        var args = Environment.GetCommandLineArgs();
        return args.Any(arg => arg.Equals("--run-once", StringComparison.OrdinalIgnoreCase));
    }
}
