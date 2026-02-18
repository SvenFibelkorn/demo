using System.Xml.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using dotnet.data;
using dotnet.models;
using Pgvector;
using StackExchange.Redis;

namespace dotnet.endpoints;

public static class RssEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan OrganizationCacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan OrganizationNotFoundCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NewestCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SimilarCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DaySummaryCacheTtl = TimeSpan.FromMinutes(30);
    private const string NullCacheMarker = "__null__";
    private const string NewestCacheKeysSet = "articles:newest:keys";
    private const string SearchCacheKeysSet = "articles:search:keys";
    private const string SimilarCacheKeysSet = "articles:similar:keys";

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(text.Length);
        var previousWasDash = false;

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasDash = false;
            }
            else if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static async Task<Organization?> FindOrganizationBySlugAsync(
        AppDbContext db,
        IDatabase redis,
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = Slugify(slug);
        if (string.IsNullOrEmpty(normalizedSlug))
        {
            return null;
        }

        var cacheKey = $"organizations:slug:{normalizedSlug}";
        try
        {
            var cached = await redis.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                if (cached == NullCacheMarker)
                {
                    return null;
                }

                var cachedOrganization = JsonSerializer.Deserialize<Organization>(cached.ToString(), JsonOptions);
                if (cachedOrganization is not null)
                {
                    return cachedOrganization;
                }
            }
        }
        catch
        {
        }

        var organizations = await db.Organizations
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var organization = organizations.FirstOrDefault(org => Slugify(org.Name) == normalizedSlug);

        try
        {
            if (organization is null)
            {
                await redis.StringSetAsync(cacheKey, NullCacheMarker, OrganizationNotFoundCacheTtl);
            }
            else
            {
                var serialized = JsonSerializer.Serialize(organization, JsonOptions);
                await redis.StringSetAsync(cacheKey, serialized, OrganizationCacheTtl);
            }
        }
        catch
        {
        }

        return organization;
    }

    private static async Task<T?> TryGetCachedAsync<T>(IDatabase redis, string key)
    {
        try
        {
            var cached = await redis.StringGetAsync(key);
            if (!cached.HasValue)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(cached.ToString(), JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static async Task TrySetCachedAsync<T>(IDatabase redis, string key, T value, TimeSpan ttl)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(value, JsonOptions);
            await redis.StringSetAsync(key, serialized, ttl);
        }
        catch
        {
        }
    }

    private static async Task InvalidateOrganizationSlugCacheAsync(IDatabase redis, string slugOrName)
    {
        var normalizedSlug = Slugify(slugOrName);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return;
        }

        try
        {
            await redis.KeyDeleteAsync($"organizations:slug:{normalizedSlug}");
        }
        catch
        {
        }
    }

    private static string CacheHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildNewestCacheKey(string? organization)
    {
        var normalizedOrganization = string.IsNullOrWhiteSpace(organization)
            ? string.Empty
            : organization.Trim().ToLowerInvariant();
        return $"articles:newest:{CacheHash(normalizedOrganization)}";
    }

    private static async Task TrackNewestCacheKeyAsync(IDatabase redis, string newestCacheKey)
    {
        try
        {
            await redis.SetAddAsync(NewestCacheKeysSet, newestCacheKey);
        }
        catch
        {
        }
    }

    private static async Task TrackSearchCacheKeyAsync(IDatabase redis, string searchCacheKey)
    {
        try
        {
            await redis.SetAddAsync(SearchCacheKeysSet, searchCacheKey);
        }
        catch
        {
        }
    }

    private static async Task TrackSimilarCacheKeyAsync(IDatabase redis, string similarCacheKey)
    {
        try
        {
            await redis.SetAddAsync(SimilarCacheKeysSet, similarCacheKey);
        }
        catch
        {
        }
    }

    private static async Task InvalidateNewestCachesAsync(IDatabase redis)
    {
        try
        {
            var trackedKeys = await redis.SetMembersAsync(NewestCacheKeysSet);
            if (trackedKeys.Length == 0)
            {
                await redis.KeyDeleteAsync(BuildNewestCacheKey(null));
                return;
            }

            var keysToDelete = trackedKeys
                .Where(k => k.HasValue)
                .Select(k => (RedisKey)k.ToString())
                .ToList();

            keysToDelete.Add(BuildNewestCacheKey(null));

            await redis.KeyDeleteAsync(keysToDelete.ToArray());
            await redis.KeyDeleteAsync(NewestCacheKeysSet);
        }
        catch
        {
        }
    }

    private static async Task InvalidateSearchCachesAsync(IDatabase redis)
    {
        try
        {
            var trackedKeys = await redis.SetMembersAsync(SearchCacheKeysSet);
            if (trackedKeys.Length == 0)
            {
                return;
            }

            var keysToDelete = trackedKeys
                .Where(k => k.HasValue)
                .Select(k => (RedisKey)k.ToString())
                .ToArray();

            if (keysToDelete.Length > 0)
            {
                await redis.KeyDeleteAsync(keysToDelete);
            }

            await redis.KeyDeleteAsync(SearchCacheKeysSet);
        }
        catch
        {
        }
    }

    private static async Task InvalidateSimilarCachesAsync(IDatabase redis)
    {
        try
        {
            var trackedKeys = await redis.SetMembersAsync(SimilarCacheKeysSet);
            if (trackedKeys.Length == 0)
            {
                return;
            }

            var keysToDelete = trackedKeys
                .Where(k => k.HasValue)
                .Select(k => (RedisKey)k.ToString())
                .ToArray();

            if (keysToDelete.Length > 0)
            {
                await redis.KeyDeleteAsync(keysToDelete);
            }

            await redis.KeyDeleteAsync(SimilarCacheKeysSet);
        }
        catch
        {
        }
    }

    public static void MapRssEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapPost("/rss", async (string Link) =>
        {
        using var httpClient = new HttpClient();
        var xml = await httpClient.GetStringAsync(Link);
        var doc = XDocument.Parse(xml);

        var items = doc.Descendants("item")
            .Select(x => new
            {
                title = x.Element("title")?.Value?.Trim(),
                description = x.Element("description")?.Value?.Trim(),
                link = x.Element("link")?.Value?.Trim()
            })
            .Where(a => !string.IsNullOrEmpty(a.title) && !string.IsNullOrEmpty(a.link))
            .ToList();
        return Results.Json(items);
        });

        // Create new organization
        group.MapPost("/organizations", async (AppDbContext db, IDatabase redis, Organization org) =>
        {
            //if (org.Id == Guid.Empty)
            //{
            //    org.Id = Guid.NewGuid();
            //}

            db.Organizations.Add(org);
            await db.SaveChangesAsync();
            await InvalidateOrganizationSlugCacheAsync(redis, org.Name);

            return Results.Created($"/api/rss/organizations/{org.Id}", org);
        });

        // Delete organization by id
        group.MapDelete("/organizations/{slug}", async (AppDbContext db, IDatabase redis, string slug, CancellationToken cancellationToken) =>
        {
            var org = await FindOrganizationBySlugAsync(db, redis, slug, cancellationToken);
            if (org == null)
            {
                return Results.NotFound(new { error = "organization not found", slug });
            }

            await db.Articles
                .Where(a => a.OrganizationId == org.Id)
                .ExecuteDeleteAsync(cancellationToken);

            db.Organizations.Remove(org);
            await db.SaveChangesAsync(cancellationToken);
            await InvalidateOrganizationSlugCacheAsync(redis, slug);
            await InvalidateNewestCachesAsync(redis);
            await InvalidateSearchCachesAsync(redis);
            await InvalidateSimilarCachesAsync(redis);
            return Results.NoContent();
        });

        // Get all organizations
        group.MapGet("/organizations", async (AppDbContext db) =>
        {
            var orgs = await db.Organizations.ToListAsync();
            return Results.Json(orgs);
        });

        // Create new article
        group.MapPost("/articles", async (AppDbContext db, IDatabase redis, Article article) =>
        {
            //if (article.Id == Guid.Empty)
            //{
            //    article.Id = Guid.NewGuid();
            //}

            db.Articles.Add(article);
            try
            {
                await db.SaveChangesAsync();
                await InvalidateNewestCachesAsync(redis);
                await InvalidateSearchCachesAsync(redis);
                await InvalidateSimilarCachesAsync(redis);
            }
            catch (DbUpdateException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            return Results.Created($"/api/articles/{article.Id}", article);
        });

        // Delete article by id
        group.MapDelete("/articles/{id:guid}", async (AppDbContext db, IDatabase redis, Guid id) =>
        {
            var article = await db.Articles.FindAsync(id);
            if (article == null)
            {
                return Results.NotFound();
            }

            db.Articles.Remove(article);
            await db.SaveChangesAsync();
            await InvalidateNewestCachesAsync(redis);
            await InvalidateSearchCachesAsync(redis);
            await InvalidateSimilarCachesAsync(redis);
            return Results.NoContent();
        });

        // Delete all articles
        group.MapDelete("/articles", async (AppDbContext db, IDatabase redis) =>
        {
            var articles = await db.Articles.ToListAsync();
            if (articles.Count == 0)
            {
                return Results.NoContent();
            }

            db.Articles.RemoveRange(articles);
            await db.SaveChangesAsync();
            await InvalidateNewestCachesAsync(redis);
            await InvalidateSearchCachesAsync(redis);
            await InvalidateSimilarCachesAsync(redis);
            return Results.NoContent();
        });

        // Get total number of articles
        group.MapGet("/articles/count", async (AppDbContext db) =>
        {
            var count = await db.Articles.CountAsync();
            return Results.Json(new { count });
        });

        // Search articles by text, optionally filtered by organization slug
        group.MapGet("/articles/search", async (
            AppDbContext db,
            IDatabase redis,
            HttpContext httpContext,
            string query,
            string? organizationSlug,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "query is required" });
            }

            var searchTerm = query.Trim();
            var normalizedOrganizationSlug = string.IsNullOrWhiteSpace(organizationSlug)
                ? string.Empty
                : Slugify(organizationSlug);
            var searchCacheKey = $"articles:search:{CacheHash($"{searchTerm.ToLowerInvariant()}|{normalizedOrganizationSlug}")}";

            var cachedSearchResults = await TryGetCachedAsync<List<Article>>(redis, searchCacheKey);
            if (cachedSearchResults is not null)
            {
                httpContext.Response.Headers["X-Cache"] = "HIT";
                return Results.Json(cachedSearchResults);
            }

            httpContext.Response.Headers["X-Cache"] = "MISS";

            var pattern = $"%{searchTerm}%";

            var articleQuery = db.Articles
                .Include(a => a.Organization)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(organizationSlug))
            {
                var organization = await FindOrganizationBySlugAsync(db, redis, organizationSlug, cancellationToken);
                if (organization == null)
                {
                    return Results.NotFound(new
                    {
                        error = "organization not found",
                        organizationSlug
                    });
                }

                articleQuery = articleQuery.Where(a => a.OrganizationId == organization.Id);
            }

            articleQuery = articleQuery.Where(a =>
                (a.Headline != null && EF.Functions.ILike(a.Headline, pattern)) ||
                (a.Summary != null && EF.Functions.ILike(a.Summary, pattern)) ||
                (a.Description != null && EF.Functions.ILike(a.Description, pattern)) ||
                (a.Content != null && EF.Functions.ILike(a.Content, pattern)));

            var matches = await articleQuery
                .OrderByDescending(a => a.PublicationDate)
                .ThenByDescending(a => a.Id)
                .Take(10)
                .ToListAsync(cancellationToken);

            await TrySetCachedAsync(redis, searchCacheKey, matches, SearchCacheTtl);
            await TrackSearchCacheKeyAsync(redis, searchCacheKey);

            return Results.Json(matches);
        });

        // Get all articles for a specific organization (including organization)
        group.MapGet("/articles/{organizationSlug}", async (AppDbContext db, IDatabase redis, string organizationSlug, CancellationToken cancellationToken) =>
        {
            var organization = await FindOrganizationBySlugAsync(db, redis, organizationSlug, cancellationToken);
            if (organization == null)
            {
                return Results.NotFound(new { error = "organization not found", organizationSlug });
            }

            var articles = await db.Articles
                .Where(a => a.OrganizationId == organization.Id)
                .Include(a => a.Organization)
                .ToListAsync(cancellationToken);

            return Results.Json(articles);
        });

        // Get counts of articles missing embeddings
        group.MapGet("/articles/embeddings/missing", async (AppDbContext db) =>
        {
            var missingEmbedding = await db.Articles.CountAsync(a => a.Embedding == null);

            return Results.Json(new
            {
                missingEmbedding
            });
        });

        // Get top-10 most similar articles for a given link (by title or summary embedding)
        group.MapGet("/articles/similar", async (AppDbContext db, IDatabase redis, HttpContext httpContext, string Link) =>
        {
            if (!Uri.TryCreate(Link, UriKind.Absolute, out var uri))
            {
                return Results.BadRequest(new { error = "invalid link" });
            }

            var similarCacheKey = $"articles:similar:{CacheHash(uri.AbsoluteUri.ToLowerInvariant())}";
            var cachedSimilar = await TryGetCachedAsync<List<Article>>(redis, similarCacheKey);
            if (cachedSimilar is not null)
            {
                httpContext.Response.Headers["X-Cache"] = "HIT";
                return Results.Json(cachedSimilar);
            }

            httpContext.Response.Headers["X-Cache"] = "MISS";

            var src = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Link == uri);
            if (src == null)
            {
                return Results.NotFound(new { error = "source article not found" });
            }

            var embedding = src.Embedding;
            if (embedding is null)
            {
                return Results.BadRequest(new { error = "no embedding for source article" });
            }

            var sql = "SELECT * FROM \"Articles\" WHERE \"Id\" <> {1} AND \"Embedding\" IS NOT NULL ORDER BY \"Embedding\" <-> {0} LIMIT 10";

            var similar = await db.Articles
                .FromSqlRaw(sql, embedding, src.Id)
                .Include(a => a.Organization)
                .AsNoTracking()
                .ToListAsync();

            await TrySetCachedAsync(redis, similarCacheKey, similar, SimilarCacheTtl);
            await TrackSimilarCacheKeyAsync(redis, similarCacheKey);

            return Results.Json(similar);
        });

        // Create embedding from text and get top-10 most similar articles
        group.MapPost("/articles/similar/text", async (
            AppDbContext db,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            SimilarArticlesTextRequest request,
            string? organization,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(new { error = "text is required" });
            }

            GeneratedEmbeddings<Embedding<float>> embeddings;
            try
            {
                embeddings = await embeddingGenerator.GenerateAsync(
                    new[] { request.Text },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                return Results.Problem($"failed to generate embedding: {ex.Message}");
            }

            if (embeddings.Count == 0)
            {
                return Results.BadRequest(new { error = "embedding generation returned no result" });
            }

            var vector = embeddings[0].Vector.ToArray();
            if (vector.Length == 0)
            {
                return Results.BadRequest(new { error = "generated embedding is empty" });
            }

            var sql = """
                SELECT a.*
                FROM "Articles" a
                LEFT JOIN "Organizations" o ON a."OrganizationId" = o."Id"
                WHERE a."Embedding" IS NOT NULL
                ORDER BY a."Embedding" <-> {0}
                LIMIT 10
                """;

            List<Article> similar;
            if (string.IsNullOrWhiteSpace(organization))
            {
                similar = await db.Articles
                    .FromSqlRaw(sql, new Vector(vector))
                    .Include(a => a.Organization)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
            }
            else
            {
                var sqlWithOrganizationFilter = """
                    SELECT a.*
                    FROM "Articles" a
                    LEFT JOIN "Organizations" o ON a."OrganizationId" = o."Id"
                    WHERE a."Embedding" IS NOT NULL
                      AND lower(o."Name") = lower({1})
                    ORDER BY a."Embedding" <-> {0}
                    LIMIT 10
                    """;

                similar = await db.Articles
                    .FromSqlRaw(sqlWithOrganizationFilter, new Vector(vector), organization.Trim())
                    .Include(a => a.Organization)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
            }

            return Results.Json(similar);
        });

        // Get 10 newest articles, optionally filtered by organization name
        group.MapGet("/articles/newest", async (
            AppDbContext db,
            IDatabase redis,
            HttpContext httpContext,
            string? organization,
            CancellationToken cancellationToken) =>
        {
            var newestCacheKey = BuildNewestCacheKey(organization);

            var cachedNewest = await TryGetCachedAsync<List<Article>>(redis, newestCacheKey);
            if (cachedNewest is not null)
            {
                httpContext.Response.Headers["X-Cache"] = "HIT";
                return Results.Json(cachedNewest);
            }

            httpContext.Response.Headers["X-Cache"] = "MISS";

            var query = db.Articles
                .Include(a => a.Organization)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(organization))
            {
                var organizationFilter = organization.Trim();
                query = query.Where(a => a.Organization != null &&
                                         a.Organization.Name != null &&
                                         a.Organization.Name.ToLower() == organizationFilter.ToLower());
            }

            var newest = await query
                .OrderByDescending(a => a.PublicationDate)
                .ThenByDescending(a => a.Id)
                .Take(10)
                .ToListAsync(cancellationToken);

            await TrySetCachedAsync(redis, newestCacheKey, newest, NewestCacheTtl);
            await TrackNewestCacheKeyAsync(redis, newestCacheKey);

            return Results.Json(newest);
        });

        // Summarize recent news articles with Groq (last 24h, optional organization filter)
        group.MapPost("/articles/day-summary", async (
            AppDbContext db,
            IDatabase redis,
            HttpContext httpContext,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            DaySummaryRequest request,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new { error = "query is required" });
            }

            var normalizedQuery = request.Query.Trim();
            var normalizedOrganizationSlug = string.IsNullOrWhiteSpace(request.OrganizationSlug)
                ? string.Empty
                : Slugify(request.OrganizationSlug);
            var hourBucket = DateTime.UtcNow.ToString("yyyyMMddHH");
            var daySummaryCacheKey = $"articles:day-summary:{CacheHash($"{normalizedQuery.ToLowerInvariant()}|{normalizedOrganizationSlug}|{hourBucket}")}";

            var cachedSummary = await TryGetCachedAsync<DaySummaryResponse>(redis, daySummaryCacheKey);
            if (cachedSummary is not null)
            {
                httpContext.Response.Headers["X-Cache"] = "HIT";
                return Results.Json(cachedSummary);
            }

            httpContext.Response.Headers["X-Cache"] = "MISS";

            var groqApiKey = configuration["GROQ_API_KEY"] ?? configuration["Groq:ApiKey"];
            if (string.IsNullOrWhiteSpace(groqApiKey))
            {
                return Results.Problem("Groq API key is missing. Set Groq:ApiKey in user secrets.");
            }

            var cutoff = DateTime.UtcNow.AddHours(-24);

            var baseQuery = db.Articles
                .Include(a => a.Organization)
                .AsNoTracking()
                .Where(a => a.PublicationDate.HasValue && a.PublicationDate.Value >= cutoff);

            Guid? organizationIdFilter = null;
            var organizationSlugFilter = request.OrganizationSlug;
            if (!string.IsNullOrWhiteSpace(organizationSlugFilter))
            {
                var organization = await FindOrganizationBySlugAsync(db, redis, organizationSlugFilter, cancellationToken);
                if (organization == null)
                {
                    return Results.NotFound(new
                    {
                        error = "organization not found",
                        organizationSlug = organizationSlugFilter
                    });
                }

                organizationIdFilter = organization.Id;
                baseQuery = baseQuery.Where(a => a.OrganizationId == organizationIdFilter.Value);
            }

            var articleQuery = baseQuery
                .OrderByDescending(a => a.PublicationDate)
                .ThenByDescending(a => a.Id);

            var articles = organizationIdFilter.HasValue
                ? await articleQuery.ToListAsync(cancellationToken)
                : await articleQuery.Take(30).ToListAsync(cancellationToken);

            if (articles.Count == 0)
            {
                return Results.NotFound(new
                {
                    error = "no matching articles found in the last 24 hours",
                    cutoffUtc = cutoff,
                    organizationSlug = organizationSlugFilter
                });
            }

            var articleLines = articles.Select(a =>
            {
                var headline = string.IsNullOrWhiteSpace(a.Headline) ? "(untitled)" : a.Headline.Trim();
                var text = a.Summary;
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = a.Description;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    text = a.Content;
                }

                if (!string.IsNullOrWhiteSpace(text) && text.Length > 600)
                {
                    text = text[..600] + "...";
                }

                var org = a.Organization?.Name ?? "Unknown";
                var published = a.PublicationDate?.ToUniversalTime().ToString("O") ?? "unknown";
                return $"- [{org}] {headline} (published: {published})\\n  {text}";
            });

            var prompt = $"""
            User request: {request.Query.Trim()}

            You are given news articles from the last 24 hours.
            Use only these articles as source material.
            If the user asks for a summary, provide a concise day summary with key topics and notable developments.
            If there are conflicting reports, mention that uncertainty.

            Articles:
            {string.Join("\n", articleLines)}
            """;

            var payload = new
            {
                model = "moonshotai/kimi-k2-instruct",
                temperature = 0.2,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You summarize current news from provided article snippets. Stay grounded in the provided content and do not invent facts."
                    },
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            var httpClient = httpClientFactory.CreateClient();
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem($"Groq request failed ({(int)response.StatusCode}): {responseText}");
            }

            string? completion;
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                completion = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to parse Groq response: {ex.Message}");
            }

            var summaryResponse = new DaySummaryResponse(
                request.Query,
                organizationSlugFilter,
                cutoff,
                articles.Count,
                completion);

            await TrySetCachedAsync(redis, daySummaryCacheKey, summaryResponse, DaySummaryCacheTtl);

            return Results.Json(summaryResponse);
        });
    }

    public sealed record SimilarArticlesTextRequest(string Text);
    public sealed record DaySummaryRequest(string Query, string? OrganizationSlug);
    public sealed record DaySummaryResponse(string Query, string? OrganizationSlug, DateTime CutoffUtc, int ArticleCount, string? Summary);
}