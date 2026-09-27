using System.Text.Json;
using ECommerce.Worker.Contracts;
using FluentAssertions;

namespace ECommerce.Worker.Tests.Contracts;

public sealed class IntegrationEventContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(IntegrationEventContract.Version1)]
    public void Version1AndLegacyMessages_AreSupported(string? version)
    {
        IntegrationEventContract.IsSupportedVersion(version).Should().BeTrue();
    }

    [Theory]
    [InlineData("2")]
    [InlineData("v1")]
    public void UnknownVersions_AreRejected(string version)
    {
        IntegrationEventContract.IsSupportedVersion(version).Should().BeFalse();
    }

    [Fact]
    public void OrderPayload_UsesTheDocumentedVersion1Shape()
    {
        var payload = new OrderIntegrationEventPayload(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "customer@example.com",
            "PendingPayment",
            99.90m,
            DateTimeOffset.Parse("2026-09-27T12:00:00+00:00"),
            [new OrderIntegrationEventItem(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Product",
                99.90m,
                1,
                99.90m)]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "OrderId",
                "CustomerId",
                "CustomerEmail",
                "Status",
                "Total",
                "OccurredAt",
                "Items");
        document.RootElement.GetProperty("Items")[0]
            .EnumerateObject().Select(property => property.Name)
            .Should().Equal("ProductId", "ProductName", "UnitPrice", "Quantity", "Subtotal");
    }

    [Fact]
    public void StockAndEmailPayloads_UseTheDocumentedVersion1Shape()
    {
        var occurredAt = DateTimeOffset.Parse("2026-09-27T12:00:00+00:00");
        var stock = new StockUpdatedIntegrationEventPayload(
            Guid.NewGuid(), 9, Guid.NewGuid(), StockUpdateReasons.Reserved, occurredAt);
        var email = new EmailSentIntegrationEventPayload(
            Guid.NewGuid(), EmailDeliveryCategories.EmailConfirmation, occurredAt);

        using var stockDocument = JsonDocument.Parse(JsonSerializer.Serialize(stock));
        using var emailDocument = JsonDocument.Parse(JsonSerializer.Serialize(email));

        stockDocument.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("ProductId", "AvailableStock", "OrderId", "Reason", "OccurredAt");
        emailDocument.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("DeliveryId", "Category", "OccurredAt");
    }
}
