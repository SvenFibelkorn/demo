using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Pgvector;

namespace scheduler.models;

public sealed class Article
{
    [Key]
    public Guid Id { get; set; }

    public required Uri Link { get; set; }

    public required Guid OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public string? Headline { get; set; }

    public string? Description { get; set; }

    public string? Summary { get; set; }

    public string? Content { get; set; }

    public DateTime? PublicationDate { get; set; }

    [JsonIgnore]
    public Vector? Embedding { get; set; }
}