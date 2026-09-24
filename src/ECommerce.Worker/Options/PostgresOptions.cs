using System.ComponentModel.DataAnnotations;

namespace ECommerce.Worker.Options;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    [Required] public string ConnectionString { get; init; } = string.Empty;
}
