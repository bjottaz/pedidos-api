# Pedidos API

API de pedidos em ASP.NET Core que grava no SQL Server e publica eventos no Azure Service Bus, consumidos por um worker que atualiza o status do pedido de `Criado` para `Processado`.

## Arquitetura

```
Cliente ──> API ───────── INSERT ─────────> SQL Server
             │                                  ▲
             │ publica evento                   │ UPDATE Status
             ▼                                  │
        Service Bus ──── consome ────> Worker ──┘
  (fila pedidos-criados)
```

## Stack

- C# / .NET 10
- ASP.NET Core (Minimal APIs) e Worker Service (`BackgroundService`)
- Dapper
- SQL Server 2022 (Docker)
- Azure Service Bus

## Como rodar

Pré-requisitos: .NET 10 SDK, Docker, Azure CLI e uma conta no Azure. Os comandos partem da raiz do repositório.

**1. Senha do SQL Server**

```bash
cp .env.example .env
```

Defina `MSSQL_SA_PASSWORD` no `.env`. A senha precisa ter no mínimo 8 caracteres e usar 3 destas 4 categorias: maiúsculas, minúsculas, números e símbolos. Se não atender, o container encerra ao iniciar (veja `docker logs sqlserver`).

**2. Subir o SQL Server**

```bash
docker compose up -d
```

**3. Criar o banco e a tabela**

```bash
docker exec sqlserver sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -i /db/init.sql'
```

**4. Criar a fila no Azure**

```bash
az login
az group create --name rg-pedidos --location <regiao>
az servicebus namespace create --resource-group rg-pedidos --name <nome-unico> --location <regiao> --sku Basic
az servicebus queue create --resource-group rg-pedidos --namespace-name <nome-unico> --name pedidos-criados

# Connection string usada no próximo passo
az servicebus namespace authorization-rule keys list --resource-group rg-pedidos --namespace-name <nome-unico> --name RootManageSharedAccessKey --query primaryConnectionString --output tsv
```

**5. Configurar os user-secrets**

Cada projeto tem os próprios secrets, então os dois precisam ser configurados:

```bash
for p in src/Pedidos.Api src/Pedidos.Worker; do
  dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=Pedidos;User Id=sa;Password=<sua-senha-sql-server>;TrustServerCertificate=True" --project $p
  dotnet user-secrets set "ServiceBus:ConnectionString" "<connection-string-do-service-bus>" --project $p
done
```

**6. Rodar a API e o worker** (em terminais separados)

```bash
dotnet run --project src/Pedidos.Api
dotnet run --project src/Pedidos.Worker
```

**Testar**

```bash
curl -i -X POST http://localhost:5254/pedidos -H "Content-Type: application/json" -d '{"cliente": "Maria", "valor": 150.00}'
curl http://localhost:5254/pedidos/<id>
```

O `POST` responde `201 Created` com o `id`. Depois que o worker processar a mensagem, o `GET` mostra `"status": "Processado"`. A API também expõe `GET /pedidos`, que lista todos os pedidos.

**Limpeza**

```bash
az group delete --name rg-pedidos --yes --no-wait
docker compose down -v
```

## Decisões de design

- **`ServiceBusClient` como Singleton.** O client mantém a conexão com o Service Bus e foi feito para ser reutilizado durante toda a vida da aplicação, em vez de ser criado a cada requisição.
- **`AutoCompleteMessages = false`.** O worker só confirma a mensagem depois que o `UPDATE` no banco termina. Se der erro antes disso, a mensagem não é confirmada e o Service Bus a entrega de novo.
- **`UPDATE` idempotente.** Marcar o mesmo pedido como `Processado` duas vezes dá o mesmo resultado, então receber uma mensagem repetida não causa efeito colateral.

## Limitações e próximos passos

- **Padrão Outbox.** Hoje, se o envio para a fila falhar depois do `INSERT`, o pedido fica gravado sem mensagem, porque banco e fila não participam da mesma transação. O Outbox resolve isso gravando o evento numa tabela na mesma transação do pedido e publicando depois.
- **EF Core.** Implementar o mesmo acesso a dados com Entity Framework Core para comparar com o Dapper.
- **Testes.** Ainda não há testes automatizados.
- **Dockerfile.** Só o SQL Server roda em container. A API e o worker ainda rodam com `dotnet run`.
