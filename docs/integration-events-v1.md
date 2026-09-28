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

## Semântica de entrega, Inbox e DLQ

A entrega é *at least once*. O Worker usa `messageId` como chave primária da
Inbox e adquire um advisory lock transacional antes dos efeitos. Inbox,
projeções, invoice, analytics e intenções de notificação são confirmadas na
mesma transação; uma redelivery já concluída recebe ACK sem repetir efeitos.

O header `x-retry-count` registra tentativas transitórias. O retry é republicado
com publisher confirm no exchange `ecommerce.events.retry`, aguarda o TTL da
fila `ecommerce.worker.orders.retry` e retorna ao exchange principal com o mesmo
`messageId`. Eventos inválidos usam diretamente a DLQ; eventos transitórios usam
a DLQ depois do limite configurado.

A mensagem na DLQ preserva envelope e propriedades AMQP originais e acrescenta
apenas metadados operacionais: `x-failure-reason` (`invalid_event` ou
`retry_exhausted`), `x-retry-count`, `x-original-exchange`,
`x-original-routing-key` e `x-dead-lettered-at`. Detalhes de exceção não são
copiados para headers. Consulte [`dlq-runbook.md`](dlq-runbook.md) antes de fazer
replay.
