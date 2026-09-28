# ECommerce Worker

Worker .NET responsável por consumir eventos do ecossistema ECommerce no RabbitMQ.

## Limites arquiteturais

Este repositório é a implementação canônica e independente do Worker. Ele possui
ciclo próprio de build, testes, imagem, versionamento e implantação; não deve ser
incorporado à solução da API nem depender de projetos ou assemblies internos de
`ECommerceApi`.

A integração com a API ocorre somente por contratos de transporte explícitos e
versionados, pelo PostgreSQL e pelo RabbitMQ. Entidades de domínio e casos de uso
da API não são contratos. Pequenos DTOs de transporte podem ser representados
localmente no Worker para preservar a independência entre os processos.

O antigo Worker interno do repositório `ECommerceApi` foi migrado e removido após
validação dos contratos, testes e execução da imagem independente.

## Fluxo implementado

O Worker consome eventos de pedido, pagamento, estoque e entrega de e-mail da
fila quorum durável `ecommerce.worker.orders`, vinculada ao exchange tópico
`ecommerce.events`.

O processamento oferece:

1. contratos locais v1 documentados em
   [`docs/integration-events-v1.md`](docs/integration-events-v1.md);
2. Inbox PostgreSQL e advisory lock por `MessageId` para idempotência concorrente;
3. projeções locais de pedidos, estoque disponível e entregas de e-mail;
4. geração idempotente de nota fiscal simulada para pedidos pagos;
5. Outbox durável de notificações, envio SMTP e Outbox de `email.sent`;
6. ACK manual, fila quorum, retry limitado pelo delivery limit e DLQ;
7. logs JSON, métricas Prometheus, traces OpenTelemetry e health checks;
8. publicação da Outbox de integração da API com claim PostgreSQL concorrente,
   lease, backoff e publisher confirms.

Todo evento novo usa o envelope v1 com `messageId`, `eventType`, `version`,
`occurredAt`, `correlationId` e `payload`. O consumer valida o envelope contra
os metadados AMQP e inclui a correlação nos logs e traces. Routing keys, schemas,
compatibilidade legada e evolução estão em
[`docs/integration-events-v1.md`](docs/integration-events-v1.md).

Inbox, projeções, nota e intenção de notificação são confirmadas antes do ACK.
Mensagens inválidas são rejeitadas para a DLQ; falhas transitórias são
reenfileiradas até o limite configurado pelo RabbitMQ.

Quando `ApiOutboxPublisher:Enabled=true`, o Worker deve usar o mesmo banco da
API. Ele lê somente os tipos de evento de integração da tabela pública
`OutboxMessages`; intenções locais de pagamento e e-mail continuam sob
responsabilidade da API. O claim usa `FOR UPDATE SKIP LOCKED`, mas nenhum lock de
banco permanece aberto durante a publicação no RabbitMQ. A entrega é
*at least once*, portanto os consumidores continuam deduplicando por
`MessageId`.

## Executar com Docker

```bash
docker compose up --build
```

O compose expõe o RabbitMQ em `localhost:5673`, a interface de administração em
`http://localhost:15673`, o PostgreSQL em `localhost:5434`, health checks em
`http://localhost:8082/health` e métricas em `http://localhost:8082/metrics`.

Para consumir os eventos da ECommerce API, ambos os projetos devem apontar para
o mesmo RabbitMQ e usar o exchange `ecommerce.events`.

## Executar localmente

```bash
dotnet restore ECommerceWorker.slnx
dotnet user-secrets set "ConnectionStrings:WorkerDatabase" \
  "Host=localhost;Port=5432;Database=ecommerce;Username=postgres;Password=<password>" \
  --project src/ECommerce.Worker
dotnet user-secrets set "RabbitMq:UserName" "ecommerce" \
  --project src/ECommerce.Worker
dotnet user-secrets set "RabbitMq:Password" "<password>" \
  --project src/ECommerce.Worker
dotnet run --project src/ECommerce.Worker
```

Endpoints operacionais:

- `/health/live`: processo iniciado;
- `/health/ready`: PostgreSQL e RabbitMQ disponíveis;
- `/metrics`: métricas Prometheus, quando habilitadas.

Para operações EF que precisam conectar ao banco, configure
`ECOMMERCE_WORKER_DESIGN_TIME_CONNECTION_STRING`. A factory não contém senha
embutida.

## Testes

```bash
dotnet test ECommerceWorker.slnx
```

Para executar também as integrações com PostgreSQL efêmero via Testcontainers:

```bash
RUN_POSTGRES_INTEGRATION_TESTS=true dotnet test ECommerceWorker.slnx
```
