using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ECommerce.Worker.Observability;

public static class WorkerTelemetry
{
    private static long _apiOutboxBacklog;
    private static long _notificationBacklog;
    private static long _integrationOutboxBacklog;
    public const string InstrumentationName = "ECommerce.Worker";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);
    public static readonly Meter Meter = new(InstrumentationName);
    public static readonly Counter<long> ConsumedEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.consumed");
    public static readonly Counter<long> DuplicateEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.duplicate");
    public static readonly Counter<long> FailedEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.failed");
    public static readonly Counter<long> RetriedEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.retried");
    public static readonly Counter<long> DeadLetteredEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.dead_lettered");
    public static readonly Counter<long> SentNotifications = Meter.CreateCounter<long>(
        "ecommerce.worker.notifications.sent");
    public static readonly Counter<long> FailedNotifications = Meter.CreateCounter<long>(
        "ecommerce.worker.notifications.failed");
    public static readonly Counter<long> PublishedEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.published");
    public static readonly Counter<long> FailedEventPublications = Meter.CreateCounter<long>(
        "ecommerce.worker.events.publication_failed");
    public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "ecommerce.worker.operation.duration",
        "s");
    public static readonly Histogram<double> MessageAge = Meter.CreateHistogram<double>(
        "ecommerce.worker.message.age",
        "s");
    public static readonly ObservableGauge<long> ApiOutboxBacklog = Meter.CreateObservableGauge(
        "ecommerce.worker.api_outbox.backlog",
        () => Volatile.Read(ref _apiOutboxBacklog));
    public static readonly ObservableGauge<long> NotificationBacklog = Meter.CreateObservableGauge(
        "ecommerce.worker.notification_outbox.backlog",
        () => Volatile.Read(ref _notificationBacklog));
    public static readonly ObservableGauge<long> IntegrationOutboxBacklog = Meter.CreateObservableGauge(
        "ecommerce.worker.integration_outbox.backlog",
        () => Volatile.Read(ref _integrationOutboxBacklog));

    public static void SetApiOutboxBacklog(long value) =>
        Interlocked.Exchange(ref _apiOutboxBacklog, value);

    public static void SetNotificationBacklog(long value) =>
        Interlocked.Exchange(ref _notificationBacklog, value);

    public static void SetIntegrationOutboxBacklog(long value) =>
        Interlocked.Exchange(ref _integrationOutboxBacklog, value);
}
