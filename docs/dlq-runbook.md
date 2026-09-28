# Runbook de DLQ e replay

Este procedimento cobre a fila durável `ecommerce.worker.orders.dead`. Use-o
quando a métrica `ecommerce.worker.events.dead_lettered` aumentar ou houver
mensagens pendentes na DLQ.

## Alertas recomendados

- **Warning:** qualquer incremento de `ecommerce.worker.events.retried` por 5
  minutos; investigar dependências e latência.
- **Critical:** qualquer incremento de
  `ecommerce.worker.events.dead_lettered` ou profundidade da DLQ maior que zero
  por 5 minutos.
- Agrupe os contadores por `messaging.event.type`, `retry.attempt` e
  `failure.reason`. Não inclua payloads nem segredos em labels.

## Diagnóstico

1. Pause o replay e preserve a mensagem na DLQ enquanto investiga.
2. Confira `message_id`, `type`, `x-contract-version`, `x-correlation-id`,
   `x-failure-reason`, `x-retry-count`, `x-original-exchange` e
   `x-original-routing-key`.
3. Use `correlationId` nos logs e traces. O motivo no header é deliberadamente
   sanitizado; o detalhe técnico permanece apenas nos logs protegidos.
4. Corrija primeiro a causa. `invalid_event` normalmente exige corrigir o
   produtor ou migrar o contrato; `retry_exhausted` normalmente indica falha de
   dependência ou ordenação temporária.

## Replay seguro

1. Exporte o corpo e todas as propriedades da mensagem antes de removê-la.
2. Republique o corpo **sem alteração** no valor de `x-original-exchange`, usando
   `x-original-routing-key` como routing key.
3. Preserve obrigatoriamente o `message_id` original. Preserve também `type`,
   `timestamp`, `x-contract-version` e `x-correlation-id`.
4. Remova os headers operacionais `x-failure-reason`, `x-dead-lettered-at` e
   `x-retry-count` para iniciar uma nova janela limitada de tentativas. Não gere
   um novo `MessageId`: a Inbox depende do original para impedir efeitos
   duplicados.
5. Confirme que a publicação foi roteada antes de dar ACK na mensagem da DLQ.
   Se a publicação falhar ou não for roteada, mantenha a mensagem na DLQ.
6. Verifique o ACK, a ausência de nova entrada na DLQ e os efeitos esperados. Se
   a Inbox já contiver o `MessageId`, o replay será reconhecido sem repetir
   invoice, e-mail, mudança de projeção ou analytics.

Não faça replay em massa sem limitar taxa e acompanhar banco, SMTP e RabbitMQ.
Para eventos financeiros, confirme antes que o efeito externo use a chave
idempotente persistida; este Worker não executa débito ou crédito diretamente.
