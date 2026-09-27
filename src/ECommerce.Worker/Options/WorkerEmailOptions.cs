namespace ECommerce.Worker.Options;

public sealed class WorkerEmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; init; } = true;
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;
    public bool UseSsl { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FromAddress { get; init; } = "no-reply@ecommerce.local";
    public string FromName { get; init; } = "ECommerce";
}
