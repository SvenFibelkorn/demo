using scheduler.data;
using scheduler.jobs;
using scheduler.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.Instrumentation.OpenTelemetry;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("rss", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("demo-scheduler/1.0");
});

builder.Services.Configure<EmbeddingOptions>(
    builder.Configuration.GetSection(EmbeddingOptions.SectionName));

var embeddingProvider = builder.Configuration
    .GetValue<string>($"{EmbeddingOptions.SectionName}:Provider")
    ?? "ollama";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PostgresConnection"),
        npgsqlOptions => npgsqlOptions.UseVector()));

if (embeddingProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, LocalOnnxEmbeddingService>();
}
else
{
    builder.Services.AddHttpClient("ollama", (serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
        client.BaseAddress = new Uri(options.Ollama.Endpoint);
        client.Timeout = TimeSpan.FromSeconds(options.Ollama.TimeoutSeconds);
    });

    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, OllamaEmbeddingService>();
}
builder.Services.AddSingleton<ArticleEmbeddingService>();

builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;
    options.AddOtlpExporter(otlpOptions =>
    {
        var endpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
        otlpOptions.Endpoint = new Uri(endpoint);
    });
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("TickerQ")
            .AddSource("scheduler.jobs")
            .AddOtlpExporter(options =>
            {
                var endpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
                options.Endpoint = new Uri(endpoint);
            });
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("scheduler.jobs")
            .AddOtlpExporter(options =>
            {
                var endpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
                options.Endpoint = new Uri(endpoint);
            });
    });

builder.Services.AddTickerQ(options =>
{
    options.IgnoreSeedDefinedCronTickers();

    var configuredRedisConnection = builder.Configuration.GetConnectionString("Redis");
    var defaultRedisHost = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
        ? "redis:6379"
        : "localhost:6379";

    var redisConfiguration = ConfigurationOptions.Parse(
        string.IsNullOrWhiteSpace(configuredRedisConnection) ? defaultRedisHost : configuredRedisConnection,
        true);

    redisConfiguration.AbortOnConnectFail = false;
    redisConfiguration.ConnectRetry = Math.Max(redisConfiguration.ConnectRetry, 10);

    options.AddStackExchangeRedis(redisOptions =>
    {
        redisOptions.Configuration = redisConfiguration.ToString();
        redisOptions.InstanceName = builder.Configuration["TickerQ:InstanceName"] ?? "tickerq:scheduler:";
        redisOptions.NodeHeartbeatInterval = TimeSpan.FromMinutes(1);
    });

    options.AddOpenTelemetryInstrumentation();
});

builder.Services.AddHostedService<SchedulerBootstrapper>();

var host = builder.Build();
host.UseTickerQ();
host.Run();