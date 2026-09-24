namespace ECommerce.Worker.Messaging;

public enum MessageProcessingResult
{
    Completed,
    Duplicate,
    Invalid
}
