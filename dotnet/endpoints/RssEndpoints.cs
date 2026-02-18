using System.Xml.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using dotnet.data;
using dotnet.models;
using Pgvector;

namespace dotnet.endpoints;

public static class RssEndpoints
{
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
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = Slugify(slug);
        if (string.IsNullOrEmpty(normalizedSlug))
        {
            return null;
        }

        var organizations = await db.Organizations
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return organizations.FirstOrDefault(org => Slugify(org.Name) == normalizedSlug);
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
        group.MapPost("/organizations", async (AppDbContext db, Organization org) =>
        {
            //if (org.Id == Guid.Empty)
            //{
            //    org.Id = Guid.NewGuid();
            //}

            db.Organizations.Add(org);
            await db.SaveChangesAsync();

            return Results.Created($"/api/rss/organizations/{org.Id}", org);
        });

        // Delete organization by id
        group.MapDelete("/organizations/{slug}", async (AppDbContext db, string slug, CancellationToken cancellationToken) =>
        {
            var org = await FindOrganizationBySlugAsync(db, slug, cancellationToken);
            if (org == null)
            {
                return Results.NotFound(new { error = "organization not found", slug });
            }

            await db.Articles
                .Where(a => a.OrganizationId == org.Id)
                .ExecuteDeleteAsync(cancellationToken);

            db.Organizations.Remove(org);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        // Get all organizations
        group.MapGet("/organizations", async (AppDbContext db) =>
        {
            var orgs = await db.Organizations.ToListAsync();
            return Results.Json(orgs);
        });

        // Create new article
        group.MapPost("/articles", async (AppDbContext db, Article article) =>
        {
            //if (article.Id == Guid.Empty)
            //{
            //    article.Id = Guid.NewGuid();
            //}

            db.Articles.Add(article);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            return Results.Created($"/api/articles/{article.Id}", article);
        });

        // Delete article by id
        group.MapDelete("/articles/{id:guid}", async (AppDbContext db, Guid id) =>
        {
            var article = await db.Articles.FindAsync(id);
            if (article == null)
            {
                return Results.NotFound();
            }

            db.Articles.Remove(article);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Delete all articles
        group.MapDelete("/articles", async (AppDbContext db) =>
        {
            var articles = await db.Articles.ToListAsync();
            if (articles.Count == 0)
            {
                return Results.NoContent();
            }

            db.Articles.RemoveRange(articles);
            await db.SaveChangesAsync();
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
            string query,
            string? organizationSlug,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "query is required" });
            }

            var searchTerm = query.Trim();
            var pattern = $"%{searchTerm}%";

            var articleQuery = db.Articles
                .Include(a => a.Organization)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(organizationSlug))
            {
                var organization = await FindOrganizationBySlugAsync(db, organizationSlug, cancellationToken);
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

            return Results.Json(matches);
        });

        // Get all articles for a specific organization (including organization)
        group.MapGet("/articles/{organizationSlug}", async (AppDbContext db, string organizationSlug, CancellationToken cancellationToken) =>
        {
            var organization = await FindOrganizationBySlugAsync(db, organizationSlug, cancellationToken);
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
        group.MapGet("/articles/similar", async (AppDbContext db, string Link) =>
        {
            if (!Uri.TryCreate(Link, UriKind.Absolute, out var uri))
            {
                return Results.BadRequest(new { error = "invalid link" });
            }

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
            string? organization,
            CancellationToken cancellationToken) =>
        {
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
                .OrderByDescending(a => a.Id)
                .Take(10)
                .ToListAsync(cancellationToken);

            return Results.Json(newest);
        });

        // Summarize recent news articles with Groq (last 24h, optional organization filter)
        group.MapPost("/articles/day-summary", async (
            AppDbContext db,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            DaySummaryRequest request,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new { error = "query is required" });
            }

            var groqApiKey = configuration["Groq:ApiKey"];
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
                var organization = await FindOrganizationBySlugAsync(db, organizationSlugFilter, cancellationToken);
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

            return Results.Json(new
            {
                query = request.Query,
                organizationSlug = organizationSlugFilter,
                cutoffUtc = cutoff,
                articleCount = articles.Count,
                summary = completion
            });
        });
    }

    public sealed record SimilarArticlesTextRequest(string Text);
    public sealed record DaySummaryRequest(string Query, string? OrganizationSlug);
}