using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using scheduler.data;
using scheduler.models;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using TickerQ.Utilities.Base;

namespace scheduler.jobs;

public sealed class RssIngestionJob
{
    public const string FunctionName = "IngestRssFeeds";

    private static readonly ActivitySource ActivitySource = new("scheduler.jobs");
    private static readonly Meter Meter = new("scheduler.jobs");
    private static readonly Counter<long> ArticlesInserted =
        Meter.CreateCounter<long>("scheduler.articles.inserted");

    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RssIngestionJob> _logger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IConfiguration _configuration;

    public RssIngestionJob(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<RssIngestionJob> logger,
        IHostEnvironment hostEnvironment,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
        _configuration = configuration;
    }

    [TickerFunction(FunctionName)]
    public async Task IngestRssFeeds(
        TickerFunctionContext context,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(FunctionName);
        var feedListFiles = ResolveFeedListFiles();
        activity?.SetTag("feed.list.count", feedListFiles.Length);
        foreach (var feedListFile in feedListFiles)
        {
            if (!File.Exists(feedListFile))
            {
                _logger.LogWarning("Feed list file not found: {FeedListFile}", feedListFile);
                continue;
            }

            var feedUrls = await File.ReadAllLinesAsync(feedListFile, cancellationToken);
            var normalizedFeedUrls = feedUrls
                .Select(url => url.Trim())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToArray();

            if (normalizedFeedUrls.Length == 0)
            {
                _logger.LogInformation("No feeds found in {FeedListFile}", feedListFile);
                continue;
            }

            var organization = await GetOrCreateOrganizationAsync(feedListFile, normalizedFeedUrls, cancellationToken);
            if (organization == null)
            {
                _logger.LogWarning("Unable to resolve organization for {FeedListFile}", feedListFile);
                continue;
            }

            foreach (var feedUrl in normalizedFeedUrls)
            {
                await ProcessFeedAsync(feedUrl, organization, cancellationToken);
            }
        }
    }

    private async Task ProcessFeedAsync(string feedUrl, Organization organization, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ProcessFeed");
        var feedHost = Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
        if (!string.IsNullOrWhiteSpace(feedHost))
        {
            activity?.SetTag("feed.host", feedHost);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("rss");
            using var response = await client.GetAsync(feedUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = XDocument.Parse(xml);

            var items = document
                .Descendants()
                .Where(element => element.Name.LocalName is "item" or "entry")
                .ToList();

            if (items.Count == 0)
            {
                _logger.LogInformation("No items found in feed {FeedUrl}", feedUrl);
                return;
            }

            var newArticles = new List<Article>();
            foreach (var item in items)
            {
                var link = ExtractLink(item);
                if (link == null)
                {
                    continue;
                }

                var exists = await _dbContext.Articles
                    .AsNoTracking()
                    .AnyAsync(article => article.Link == link, cancellationToken);

                if (exists)
                {
                    _logger.LogInformation("Existing article found in {FeedUrl}, stopping further imports.", feedUrl);
                    break;
                }

                var article = new Article
                {
                    Id = Guid.CreateVersion7(),
                    Link = link,
                    OrganizationId = organization.Id,
                    Headline = ExtractValue(item, "title"),
                    Description = ExtractValue(item, "description"),
                    Summary = ExtractValue(item, "summary"),
                    PublicationDate = ExtractPublicationDate(item),
                    //Content = ExtractContent(item)
                };

                newArticles.Add(article);
            }

            if (newArticles.Count == 0)
            {
                return;
            }

            _dbContext.Articles.AddRange(newArticles);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Failed to save articles to database. Articles count: {Count}", newArticles.Count);
                throw;
            }

            if (newArticles.Count > 0)
            {
                var tags = new TagList();
                if (!string.IsNullOrWhiteSpace(feedHost))
                {
                    tags.Add("feed.host", feedHost);
                }

                ArticlesInserted.Add(newArticles.Count, tags);
                activity?.SetTag("articles.inserted", newArticles.Count);
            }

            _logger.LogInformation(
                "Imported {Count} new articles from {FeedUrl}",
                newArticles.Count,
                feedUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "Failed to process feed {FeedUrl}", feedUrl);
        }
    }

    private string[] ResolveFeedListFiles()
    {
        var configured = _configuration.GetSection("Scheduler:FeedListFiles").Get<string[]>();
        if (configured is { Length: > 0 })
        {
            return configured
                .Select(path => Path.IsPathRooted(path)
                    ? path
                    : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, path)))
                .ToArray();
        }

        var repoRoot = Directory.GetParent(_hostEnvironment.ContentRootPath)?.FullName
            ?? _hostEnvironment.ContentRootPath;

        return
        [
            Path.Combine(repoRoot, "corpus", "economist.txt"),
            Path.Combine(repoRoot, "corpus", "theverge.txt"),
            Path.Combine(repoRoot, "corpus", "arstechnica.txt"),
            Path.Combine(repoRoot, "corpus", "zeit.txt"),
            Path.Combine(repoRoot, "corpus", "semafor.txt"),
            Path.Combine(repoRoot, "corpus", "dw.txt")
        ];
    }

    private async Task<Organization?> GetOrCreateOrganizationAsync(
        string feedListFile,
        string[] feedUrls,
        CancellationToken cancellationToken)
    {
        var definition = ResolveOrganizationDefinition(feedListFile, feedUrls);
        if (definition == null)
        {
            return null;
        }

        var existing = await _dbContext.Organizations
            .FirstOrDefaultAsync(org => org.Name == definition.Value.Name, cancellationToken);

        if (existing != null)
        {
            return existing;
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = definition.Value.Name,
            Url = definition.Value.Url
        };

        _dbContext.Organizations.Add(organization);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return organization;
    }

    private static (string Name, Uri Url)? ResolveOrganizationDefinition(string feedListFile, string[] feedUrls)
    {
        var fileName = Path.GetFileNameWithoutExtension(feedListFile).ToLowerInvariant();
        if (fileName.Contains("economist"))
        {
            return ("The Economist", new Uri("https://www.economist.com"));
        }

        if (fileName.Contains("theverge"))
        {
            return ("The Verge", new Uri("https://www.theverge.com"));
        }

        if (fileName.Contains("zeit"))
        {
            return ("DIE ZEIT", new Uri("https://www.zeit.de"));
        }

        var firstUrl = feedUrls.FirstOrDefault();
        if (firstUrl != null && Uri.TryCreate(firstUrl, UriKind.Absolute, out var uri))
        {
            return (uri.Host, new Uri($"{uri.Scheme}://{uri.Host}"));
        }

        return null;
    }

    private static Uri? ExtractLink(XElement item)
    {
        var linkValue = ExtractValue(item, "link");
        if (string.IsNullOrWhiteSpace(linkValue))
        {
            var linkElement = item.Elements().FirstOrDefault(element => element.Name.LocalName == "link");
            linkValue = linkElement?.Attribute("href")?.Value;
        }

        if (string.IsNullOrWhiteSpace(linkValue))
        {
            linkValue = ExtractValue(item, "guid");
        }

        if (string.IsNullOrWhiteSpace(linkValue))
        {
            return null;
        }

        return Uri.TryCreate(linkValue.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private static string? ExtractValue(XElement item, string elementName)
    {
        return item
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
    }

    private static string? ExtractContent(XElement item)
    {
        var content = item
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("encoded", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (!string.IsNullOrWhiteSpace(content))
        {
            return content.Trim();
        }

        content = item
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
    }

    private static DateTime? ExtractPublicationDate(XElement item)
    {
        var candidateValues = new[]
        {
            ExtractValue(item, "pubDate"),
            ExtractValue(item, "published"),
            ExtractValue(item, "updated"),
            ExtractValue(item, "date")
        };

        foreach (var candidate in candidateValues)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(
                    candidate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return parsed.UtcDateTime;
            }
        }

        return null;
    }
}
