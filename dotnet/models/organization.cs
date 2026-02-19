using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace dotnet.models;

public sealed class Organization
{
    [Key]
    public Guid Id { get; set; }

    public required Uri Url { get; set; }

    public required string Name { get; set; }
}