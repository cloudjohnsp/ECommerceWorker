using ECommerce.Worker.Messaging;
using ECommerce.Worker.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<PostgresOptions>()
    .Bind(builder.Configuration.GetSection(PostgresOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var rabbitMqOptions = builder.Configuration
    .GetSection(RabbitMqOptions.SectionName)
    .Get<RabbitMqOptions>()
    ?? throw new InvalidOperationException("RabbitMq configuration is missing.");

builder.Services.AddSingleton(new ConnectionFactory
{
    HostName = rabbitMqOptions.HostName,
    Port = rabbitMqOptions.Port,
    UserName = rabbitMqOptions.UserName,
    Password = rabbitMqOptions.Password,
    VirtualHost = rabbitMqOptions.VirtualHost,
    ClientProvidedName = rabbitMqOptions.ClientProvidedName,
    AutomaticRecoveryEnabled = true,
    TopologyRecoveryEnabled = true
});
builder.Services.AddSingleton<IProcessedMessageStore, PostgresProcessedMessageStore>();
builder.Services.AddSingleton<IOrderCreatedHandler, LoggingOrderCreatedHandler>();
builder.Services.AddSingleton<OrderCreatedMessageProcessor>();
builder.Services.AddHostedService<RabbitMqOrderConsumer>();

await builder.Build().RunAsync();
