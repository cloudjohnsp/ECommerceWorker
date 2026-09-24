# ECommerce Worker

Worker .NET responsável por consumir eventos do ecossistema ECommerce no RabbitMQ.

## Fluxo implementado

O worker consome mensagens `order.created` da fila durável
`ecommerce.worker.orders`, vinculada ao exchange tópico `ecommerce.events`.

Para cada pedido criado ele:

1. valida o identificador e o payload da mensagem;
2. consulta a inbox PostgreSQL para eliminar mensagens já processadas;
3. simula o envio do e-mail de confirmação e a geração da nota fiscal;
4. registra o `MessageId` como processado;
5. confirma a mensagem manualmente no RabbitMQ.

Mensagens inválidas seguem para `ecommerce.worker.orders.dead-letter`. Falhas
transitórias são reenfileiradas. Como os efeitos são processados com semântica
*at-least-once*, handlers reais de e-mail e nota fiscal também devem ser
idempotentes.

## Executar com Docker

```bash
docker compose up --build
```

O compose expõe o RabbitMQ em `localhost:5673`, a interface de administração em
`http://localhost:15673` e o PostgreSQL em `localhost:5434`.

Para consumir os eventos da ECommerce API, ambos os projetos devem apontar para
o mesmo RabbitMQ e usar o exchange `ecommerce.events`.

## Executar localmente

```bash
dotnet restore ECommerceWorker.slnx
dotnet run --project src/ECommerce.Worker
```

## Testes

```bash
dotnet test ECommerceWorker.slnx
```
