namespace ECommerce.Worker.Processing;

public sealed class InvalidIntegrationEventException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class MissingOrderProjectionException(Guid orderId)
    : Exception($"Order projection '{orderId}' has not been created yet.");
