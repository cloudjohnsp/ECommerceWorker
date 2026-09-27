using ECommerce.Worker.HealthChecks;
using ECommerce.Worker.Messaging;
using ECommerce.Worker.Notifications;
using ECommerce.Worker.Observability;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Processing;
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

builder.Services.AddDbContext<WorkerDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WorkerDbContext.SchemaName)));
builder.Services.AddScoped<OrderIntegrationEventProcessor>();
builder.Services.AddScoped<StockIntegrationEventProcessor>();
builder.Services.AddScoped<EmailSentIntegrationEventProcessor>();
builder.Services.AddScoped<IntegrationEventProcessor>();
builder.Services.AddScoped<NotificationOutboxProcessor>();
builder.Services.AddScoped<WorkerIntegrationEventOutboxProcessor>();

builder.Services.AddOptions<WorkerRabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(WorkerRabbitMqOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.HostName), "RabbitMq:HostName is required.")
    .Validate(options => options.Port > 0, "RabbitMq:Port must be positive.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.UserName), "RabbitMq:UserName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "RabbitMq:Password is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ExchangeName), "RabbitMq:ExchangeName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.QueueName), "RabbitMq:QueueName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterExchangeName), "RabbitMq:DeadLetterExchangeName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterQueueName), "RabbitMq:DeadLetterQueueName is required.")
    .Validate(options => options.DeliveryLimit > 0, "RabbitMq:DeliveryLimit must be positive.")
    .Validate(options => options.RetryDelaySeconds > 0, "RabbitMq:RetryDelaySeconds must be positive.")
    .Validate(options => options.ReconnectDelaySeconds > 0, "RabbitMq:ReconnectDelaySeconds must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkerEmailOptions>()
    .Bind(builder.Configuration.GetSection(WorkerEmailOptions.SectionName))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.Host), "Email:Host is required.")
    .Validate(options => !options.Enabled || options.Port > 0, "Email:Port must be positive.")
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.FromAddress), "Email:FromAddress is required.")
    .ValidateOnStart();
builder.Services.AddOptions<NotificationProcessorOptions>()
    .Bind(builder.Configuration.GetSection(NotificationProcessorOptions.SectionName))
    .Validate(options => options.BatchSize > 0, "NotificationProcessor:BatchSize must be positive.")
    .Validate(options => options.PollIntervalSeconds > 0, "NotificationProcessor:PollIntervalSeconds must be positive.")
    .Validate(options => options.LockSeconds > 0, "NotificationProcessor:LockSeconds must be positive.")
    .Validate(options => options.MaximumAttempts > 0, "NotificationProcessor:MaximumAttempts must be positive.")
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
    AutomaticRecoveryEnabled = false,
    ConsumerDispatchConcurrency = 1
});
builder.Services.AddSingleton<IWorkerIntegrationEventPublisher, RabbitMqWorkerIntegrationEventPublisher>();
builder.Services.AddHealthChecks()
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
    builder.Services.AddSingleton<IOrderEmailSender, SmtpOrderEmailSender>();
else
    builder.Services.AddSingleton<IOrderEmailSender, DisabledOrderEmailSender>();

builder.Services.AddHostedService<RabbitMqOrderConsumer>();
builder.Services.AddHostedService<NotificationDispatcher>();

var app = builder.Build();
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
