using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ECommerce.Worker.Observability;

public static class WorkerTelemetry
{
    public const string InstrumentationName = "ECommerce.Worker";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);
    public static readonly Meter Meter = new(InstrumentationName);
    public static readonly Counter<long> ConsumedEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.consumed");
    public static readonly Counter<long> DuplicateEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.duplicate");
    public static readonly Counter<long> FailedEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.failed");
    public static readonly Counter<long> SentNotifications = Meter.CreateCounter<long>(
        "ecommerce.worker.notifications.sent");
    public static readonly Counter<long> FailedNotifications = Meter.CreateCounter<long>(
        "ecommerce.worker.notifications.failed");
    public static readonly Counter<long> PublishedEvents = Meter.CreateCounter<long>(
        "ecommerce.worker.events.published");
    public static readonly Counter<long> FailedEventPublications = Meter.CreateCounter<long>(
        "ecommerce.worker.events.publication_failed");
}
