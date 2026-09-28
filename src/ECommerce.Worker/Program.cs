using ECommerce.Worker.HealthChecks;
using ECommerce.Worker.Messaging;
using ECommerce.Worker.Notifications;
using ECommerce.Worker.Observability;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Processing;
using ECommerce.Worker.Resilience;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Serilog;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter(renderMessage: true)));

var connectionString = builder.Configuration.GetConnectionString("WorkerDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:WorkerDatabase is required.");

var runtimeOptions = builder.Configuration
    .GetSection(WorkerRuntimeOptions.SectionName)
    .Get<WorkerRuntimeOptions>() ?? new WorkerRuntimeOptions();
builder.Services.AddDbContextPool<WorkerDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WorkerDbContext.SchemaName);
        npgsql.CommandTimeout(Math.Clamp(runtimeOptions.DatabaseCommandTimeoutSeconds, 1, 300));
    }), Math.Clamp(runtimeOptions.DatabasePoolSize, 1, 256));
builder.Services.AddSingleton<WorkerOperationDrain>();
builder.Services.AddScoped<OrderIntegrationEventProcessor>();
builder.Services.AddScoped<StockIntegrationEventProcessor>();
builder.Services.AddScoped<EmailSentIntegrationEventProcessor>();
builder.Services.AddScoped<IntegrationEventProcessor>();
builder.Services.AddScoped<NotificationOutboxProcessor>();
builder.Services.AddScoped<WorkerIntegrationEventOutboxProcessor>();
builder.Services.AddScoped<ApiOutboxPublisher>();

builder.Services.AddOptions<WorkerRabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(WorkerRabbitMqOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.HostName), "RabbitMq:HostName is required.")
    .Validate(options => options.Port > 0, "RabbitMq:Port must be positive.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.UserName), "RabbitMq:UserName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "RabbitMq:Password is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ExchangeName), "RabbitMq:ExchangeName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.QueueName), "RabbitMq:QueueName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.RetryExchangeName), "RabbitMq:RetryExchangeName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.RetryQueueName), "RabbitMq:RetryQueueName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterExchangeName), "RabbitMq:DeadLetterExchangeName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterQueueName), "RabbitMq:DeadLetterQueueName is required.")
    .Validate(options => options.DeliveryLimit is >= 1 and <= 100, "RabbitMq:DeliveryLimit must be between 1 and 100.")
    .Validate(options => options.MaximumRetryAttempts is >= 1 and <= 20, "RabbitMq:MaximumRetryAttempts must be between 1 and 20.")
    .Validate(options => options.RetryDelaySeconds is >= 1 and <= 3600, "RabbitMq:RetryDelaySeconds must be between 1 and 3600.")
    .Validate(options => options.ReconnectDelaySeconds is >= 1 and <= 300, "RabbitMq:ReconnectDelaySeconds must be between 1 and 300.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkerRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(WorkerRuntimeOptions.SectionName))
    .Validate(options => options.ShutdownDrainSeconds is >= 1 and <= 300, "WorkerRuntime:ShutdownDrainSeconds must be between 1 and 300.")
    .Validate(options => options.ConsumerConcurrency is >= 1 and <= 32, "WorkerRuntime:ConsumerConcurrency must be between 1 and 32.")
    .Validate(options => options.DatabasePoolSize is >= 1 and <= 256, "WorkerRuntime:DatabasePoolSize must be between 1 and 256.")
    .Validate(options => options.DatabaseCommandTimeoutSeconds is >= 1 and <= 300, "WorkerRuntime:DatabaseCommandTimeoutSeconds must be between 1 and 300.")
    .Validate(options => options.RabbitMqConnectionTimeoutSeconds is >= 1 and <= 60, "WorkerRuntime:RabbitMqConnectionTimeoutSeconds must be between 1 and 60.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkerEmailOptions>()
    .Bind(builder.Configuration.GetSection(WorkerEmailOptions.SectionName))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.Host), "Email:Host is required.")
    .Validate(options => !options.Enabled || options.Port > 0, "Email:Port must be positive.")
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.FromAddress), "Email:FromAddress is required.")
    .Validate(options => options.TimeoutSeconds is >= 1 and <= 300, "Email:TimeoutSeconds must be between 1 and 300.")
    .ValidateOnStart();
builder.Services.AddOptions<NotificationProcessorOptions>()
    .Bind(builder.Configuration.GetSection(NotificationProcessorOptions.SectionName))
    .Validate(options => options.BatchSize is >= 1 and <= 500, "NotificationProcessor:BatchSize must be between 1 and 500.")
    .Validate(options => options.PollIntervalSeconds is >= 1 and <= 300, "NotificationProcessor:PollIntervalSeconds must be between 1 and 300.")
    .Validate(options => options.LockSeconds is >= 5 and <= 3600, "NotificationProcessor:LockSeconds must be between 5 and 3600.")
    .Validate(options => options.MaximumAttempts is >= 1 and <= 100, "NotificationProcessor:MaximumAttempts must be between 1 and 100.")
    .ValidateOnStart();
builder.Services.AddOptions<ApiOutboxPublisherOptions>()
    .Bind(builder.Configuration.GetSection(ApiOutboxPublisherOptions.SectionName))
    .Validate(options => options.BatchSize is >= 1 and <= 500, "ApiOutboxPublisher:BatchSize must be between 1 and 500.")
    .Validate(options => options.PollIntervalSeconds is >= 1 and <= 300, "ApiOutboxPublisher:PollIntervalSeconds must be between 1 and 300.")
    .Validate(options => options.LockSeconds is >= 5 and <= 3600, "ApiOutboxPublisher:LockSeconds must be between 5 and 3600.")
    .Validate(options => options.MaximumAttempts is >= 1 and <= 100, "ApiOutboxPublisher:MaximumAttempts must be between 1 and 100.")
    .Validate(options => options.BaseRetryDelaySeconds is >= 1 and <= 3600, "ApiOutboxPublisher:BaseRetryDelaySeconds must be between 1 and 3600.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkerObservabilityOptions>()
    .Bind(builder.Configuration.GetSection(WorkerObservabilityOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ServiceName),
        "Observability:ServiceName is required.")
    .Validate(options => string.IsNullOrWhiteSpace(options.OtlpEndpoint) ||
            Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out _),
        "Observability:OtlpEndpoint must be an absolute URI when configured.")
    .ValidateOnStart();

var rabbitMqOptions = builder.Configuration
    .GetSection(WorkerRabbitMqOptions.SectionName)
    .Get<WorkerRabbitMqOptions>() ?? new WorkerRabbitMqOptions();
builder.Services.AddSingleton(_ => new ConnectionFactory
{
    HostName = rabbitMqOptions.HostName,
    Port = rabbitMqOptions.Port,
    UserName = rabbitMqOptions.UserName,
    Password = rabbitMqOptions.Password,
    VirtualHost = rabbitMqOptions.VirtualHost,
    ClientProvidedName = rabbitMqOptions.ClientProvidedName,
    RequestedConnectionTimeout = TimeSpan.FromSeconds(
        Math.Clamp(runtimeOptions.RabbitMqConnectionTimeoutSeconds, 1, 60)),
    AutomaticRecoveryEnabled = false,
    ConsumerDispatchConcurrency = (ushort)Math.Clamp(runtimeOptions.ConsumerConcurrency, 1, 32)
});
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(
        Math.Clamp(runtimeOptions.ShutdownDrainSeconds, 1, 300) + 5));
builder.Services.AddSingleton<IWorkerIntegrationEventPublisher, RabbitMqWorkerIntegrationEventPublisher>();
var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<WorkerPostgresHealthCheck>(
        "postgres",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5))
    .AddCheck<WorkerRabbitMqHealthCheck>(
        "rabbitmq",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));

var observabilityOptions = builder.Configuration
    .GetSection(WorkerObservabilityOptions.SectionName)
    .Get<WorkerObservabilityOptions>() ?? new WorkerObservabilityOptions();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(observabilityOptions.ServiceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(WorkerTelemetry.InstrumentationName)
            .AddAspNetCoreInstrumentation(instrumentation =>
            {
                instrumentation.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/metrics") &&
                    !context.Request.Path.StartsWithSegments("/health");
            });
        if (TryGetOtlpEndpoint(observabilityOptions, out var endpoint))
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(WorkerTelemetry.InstrumentationName)
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation();
        if (observabilityOptions.EnablePrometheus)
            metrics.AddPrometheusExporter();
        if (TryGetOtlpEndpoint(observabilityOptions, out var endpoint))
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
    });

var emailOptions = builder.Configuration
    .GetSection(WorkerEmailOptions.SectionName)
    .Get<WorkerEmailOptions>() ?? new WorkerEmailOptions();
if (emailOptions.Enabled)
{
    builder.Services.AddSingleton<IOrderEmailSender, SmtpOrderEmailSender>();
    healthChecks.AddCheck<WorkerSmtpHealthCheck>(
        "smtp",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(emailOptions.TimeoutSeconds));
}
else
    builder.Services.AddSingleton<IOrderEmailSender, DisabledOrderEmailSender>();

builder.Services.AddHostedService<RabbitMqOrderConsumer>();
builder.Services.AddHostedService<NotificationDispatcher>();
var apiOutboxOptions = builder.Configuration
    .GetSection(ApiOutboxPublisherOptions.SectionName)
    .Get<ApiOutboxPublisherOptions>() ?? new ApiOutboxPublisherOptions();
if (apiOutboxOptions.Enabled)
    builder.Services.AddHostedService<ApiOutboxDispatcher>();

var app = builder.Build();
_ = app.Services.GetRequiredService<IOptions<WorkerRuntimeOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<WorkerRabbitMqOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<WorkerEmailOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<NotificationProcessorOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<ApiOutboxPublisherOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<WorkerObservabilityOptions>>().Value;
await app.Services.InitializeWorkerDatabaseAsync();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
if (app.Services.GetRequiredService<IOptions<WorkerObservabilityOptions>>().Value.EnablePrometheus)
    app.MapPrometheusScrapingEndpoint("/metrics");

await app.RunAsync();

static bool TryGetOtlpEndpoint(WorkerObservabilityOptions options, out Uri endpoint) =>
    Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out endpoint!);
