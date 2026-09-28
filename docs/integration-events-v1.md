# Contratos de eventos de integração v1

Cada mensagem publicada no exchange tópico durável `ecommerce.events` contém um
envelope JSON camelCase. As propriedades AMQP repetem os campos usados para
roteamento e validação:

| Envelope JSON | Propriedade AMQP | Regra |
| --- | --- | --- |
| `messageId` | `message_id` | GUID da mensagem de Outbox |
| `eventType` | `type` e routing key | Nome estável do evento |
| `version` | `x-contract-version` | `1` |
| `occurredAt` | `timestamp` | UTC |
| `correlationId` | `x-correlation-id` | correlação ponta a ponta, até 128 caracteres |
| `payload` | corpo interno | DTO próprio do transporte |

O consumer valida a igualdade entre envelope e metadados AMQP. Durante a
convivência iniciada em 2026-09-27, mensagens legadas sem envelope continuam
aceitas como v1; a retirada desse fallback exige telemetria sem mensagens
legadas e um novo ADR.

## Routing keys e payloads

- `order.created`, `order.updated`, `order.deleted`, `order.paid`, `payment.failed`,
  `order.refunded` e `order.cancelled` usam `OrderIntegrationEventPayload`.
  `order.paid` é o equivalente interno de `PaymentCompleted`; o webhook externo
  correspondente é `payment.approved`.
- `stock.updated` usa `StockUpdatedIntegrationEventPayload`.
- `email.sent` usa `EmailSentIntegrationEventPayload`, sem destinatário, nome,
  corpo ou token.

O contrato canônico completo de `order.paid` está em
[`contracts/v1/order-paid.example.json`](contracts/v1/order-paid.example.json).
O payload de pedido contém `OrderId`, `CustomerId`, `CustomerEmail`, `Status`,
`Total`, `ExpiresAt`, `CancellationReason`, `OccurredAt` e `Items`. O payload de
estoque contém `ProductId`, `AvailableStock`, `OrderId`, `Reason` e
`OccurredAt`. O payload de e-mail contém `DeliveryId`, `Category` e
`OccurredAt`.

Esta versão é imutável. Campos novos compatíveis devem ser opcionais e
consumers ignoram campos desconhecidos. Mudança incompatível cria uma nova
versão e exige período explícito de publicação/consumo simultâneos antes da
retirada da versão anterior.
