# Contratos de eventos de integração v1

Os eventos publicados no exchange tópico `ecommerce.events` usam propriedades
AMQP como envelope e JSON UTF-8 como payload.

## Envelope AMQP

| Propriedade | Regra |
| --- | --- |
| `content_type` | `application/json` |
| `message_id` | GUID único do Outbox |
| `type` | Tipo do evento e routing key |
| `timestamp` | Instante Unix de ocorrência |
| `x-contract-version` | `1` |

A ausência de `x-contract-version` é aceita temporariamente como v1 para drenar
mensagens legadas. Qualquer outra versão é rejeitada e segue a política de DLQ.

## Eventos de pedido v1

Tipos: `order.created`, `order.updated`, `order.paid`, `payment.failed`,
`order.refunded` e `order.cancelled`.

Todos usam `OrderIntegrationEventPayload`:

```json
{
  "OrderId": "11111111-1111-1111-1111-111111111111",
  "CustomerId": "22222222-2222-2222-2222-222222222222",
  "CustomerEmail": "customer@example.com",
  "Status": "PendingPayment",
  "Total": 99.90,
  "OccurredAt": "2026-09-27T12:00:00+00:00",
  "Items": [
    {
      "ProductId": "33333333-3333-3333-3333-333333333333",
      "ProductName": "Product",
      "UnitPrice": 99.90,
      "Quantity": 1,
      "Subtotal": 99.90
    }
  ]
}
```

## Estoque v1

O tipo `stock.updated` usa `StockUpdatedIntegrationEventPayload`:

```json
{
  "ProductId": "33333333-3333-3333-3333-333333333333",
  "AvailableStock": 9,
  "OrderId": "11111111-1111-1111-1111-111111111111",
  "Reason": "reserved",
  "OccurredAt": "2026-09-27T12:00:00+00:00"
}
```

## Entrega de e-mail v1

O tipo `email.sent` usa `EmailSentIntegrationEventPayload` e não contém endereço,
nome, conteúdo ou token:

```json
{
  "DeliveryId": "44444444-4444-4444-4444-444444444444",
  "Category": "email_confirmation",
  "OccurredAt": "2026-09-27T12:00:00+00:00"
}
```

Campos e semântica desta versão são imutáveis. Uma alteração incompatível exige
uma nova versão do contrato e período explícito de convivência.
