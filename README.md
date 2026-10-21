# actdigital: LibraryLoans.Api

API REST de catálogo e empréstimos para o desafio de empréstimos concorrentes e auditáveis. A aplicação usa .NET 10, PostgreSQL, EF Core/Npgsql e Redis.

## Pré-requisitos

- .NET 10 SDK;
- Docker Desktop com contêineres Linux;
- Docker Compose.

## Executar localmente

Na raiz do repositório:

```powershell
cd LibraryLoans.Api
docker compose up -d postgres redis
dotnet tool restore
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5432;Database=library_loans;Username=library;Password=local-development-only"
dotnet tool run dotnet-ef database update --project LibraryLoans.Api/LibraryLoans.Api.csproj
Remove-Item Env:ConnectionStrings__Postgres
docker compose up --build api
```

A API fica em `http://localhost:8080`. No perfil `Development`, o Swagger UI fica em `/swagger` e o documento OpenAPI em `/openapi/v1.json`.

O Compose usa `local-development-only` apenas como senha descartável de desenvolvimento. Sobrescreva `POSTGRES_PASSWORD` no ambiente local se necessário. Não use esses valores em ambientes compartilhados ou produção. A aplicação recebe `ConnectionStrings__Postgres`, `Redis__ConnectionString` e, opcionalmente, `OTEL_EXPORTER_OTLP_ENDPOINT` por configuração/ambiente; não há credenciais reais versionadas.

Para encerrar os serviços sem apagar os dados: `docker compose down`. O comando `docker compose down -v` também remove o volume local do PostgreSQL.

## Dados de demonstração

Ao iniciar em `Development`, a aplicação aplica migrations pendentes e cria dados de demonstração de forma idempotente. Os registros não são removidos nem sobrescritos. A carga não roda em `Production`.

| Registro | ID | Informação útil |
| --- | --- | --- |
| Livro com empréstimo ativo | `d8a65cb2-c871-4a9f-b5e2-6f713d35b001` | ISBN `9780000000009`, 5 exemplares totais |
| Livro com histórico devolvido | `d8a65cb2-c871-4a9f-b5e2-6f713d35b002` | ISBN `9780000000018` |
| Livro com último exemplar livre | `d8a65cb2-c871-4a9f-b5e2-6f713d35b003` | ISBN `9780000000027`, 1 exemplar |
| Leitor de demonstração 1 | `d8a65cb2-c871-4a9f-b5e2-6f713d35c001` | Tem um empréstimo ativo |
| Leitor de demonstração 2 | `d8a65cb2-c871-4a9f-b5e2-6f713d35c002` | Tem um empréstimo devolvido no histórico |
| Leitor de demonstração 3 | `d8a65cb2-c871-4a9f-b5e2-6f713d35c003` | Sem empréstimos |

Use `GET /books`, `GET /books/{id}/history`, `GET /users/{id}/loans` e `GET /books/{id}/availability` no Swagger para explorar os dados. O seed adiciona também dois eventos de histórico para o empréstimo devolvido e um empréstimo ativo para experimentar devolução e cancelamento.

## Migrations e testes

A ferramenta `dotnet-ef` é fixada em `LibraryLoans.Api/dotnet-tools.json` para manter a versão alinhada ao EF Core do projeto. Depois de definir `ConnectionStrings__Postgres`, migrations podem ser criadas com:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add NomeDaMigration --project LibraryLoans.Api/LibraryLoans.Api.csproj
```

As migrations existentes podem ser aplicadas ao banco local com `dotnet tool run dotnet-ef database update --project LibraryLoans.Api/LibraryLoans.Api.csproj`. Os testes de integração usam PostgreSQL e Redis reais por Testcontainers:

```powershell
dotnet test tests/LibraryLoans.Api.Tests/LibraryLoans.Api.Tests.csproj
```

O Docker precisa estar em execução para os testes de integração.

## Decisões técnicas

- **Concorrência:** o PostgreSQL é a fonte de verdade. O empréstimo decrementa `AvailableCopies` com um único `UPDATE` condicional (`AvailableCopies > 0`) dentro de uma transação, antes de inserir empréstimo, idempotência e auditoria. O banco serializa a disputa pela linha; só uma requisição consegue consumir a última unidade. A unicidade de ISBN e da `Idempotency-Key` também é garantida por índices/chaves no banco. Essa estratégia coordena réplicas sem locks em memória.
- **Idempotência:** a chave é persistida com fingerprint SHA-256 da solicitação e o empréstimo resultante, na mesma transação. Repetição igual retorna o mesmo empréstimo; reuso da chave com conteúdo diferente retorna `409 Conflict`. A chave não expira automaticamente nesta versão.
- **Auditoria:** eventos de livros e empréstimos são gravados na mesma transação da mudança de negócio. Sem autenticação no escopo do desafio, o ator é registrado como `api`; timestamps são UTC e o correlation ID vem de `X-Correlation-ID` (gerado quando ausente ou inválido para o tamanho aceito).
- **Cache:** Redis armazena o catálogo e a disponibilidade por até dois minutos. Escritas invalidam as chaves afetadas; falhas de cache usam PostgreSQL como fallback. Redis nunca decide se um empréstimo pode ser criado.
- **Cancelamento:** cancelar um empréstimo ativo encerra o registro existente e devolve a unidade ao estoque. Devolver ou cancelar outra vez retorna conflito, preservando o histórico.
- **Observabilidade:** `/health/live` verifica somente o processo; `/health/ready` verifica PostgreSQL e Redis. Há métricas de empréstimos, indisponibilidade, repetição idempotente e duração, além de instrumentação ASP.NET Core/HTTP/runtime. O exportador OTLP é habilitado quando `OTEL_EXPORTER_OTLP_ENDPOINT` estiver configurado.

## Kubernetes

Os manifests mínimos estão em `LibraryLoans.Api/deploy/kubernetes`. Crie a Secret `library-loans-api` com a chave `postgres-connection-string` no namespace escolhido, forneça a imagem da API e aplique `configmap.yaml`, `deployment.yaml` e `service.yaml`. Nenhum valor de Secret real é versionado. O Deployment define duas réplicas, requests/limits e probes HTTP distintas.

A correção com 2 a 11 réplicas depende de transações, atualização condicional e restrições no PostgreSQL compartilhado; Redis é compartilhado e serve apenas para leituras. Não há estado de empréstimos mantido em memória. Migrations devem ser aplicadas uma vez por implantação, antes de liberar tráfego, e não automaticamente por cada réplica.

## Limitações e evolução

- Não há autenticação/autorização; por isso o ator de auditoria é técnico.
- O modelo mantém contadores por livro, não identidade individual de cada exemplar.
- A idempotência não tem política de expiração ou escopo por usuário autenticado.
- Para produção, definir retenção/particionamento de auditoria, autenticação, política de expiração de idempotência, alertas/retentativas de invalidação de cache e execução controlada de migrations.
- A disponibilidade foi coberta por testes de integração no PostgreSQL via Testcontainers; requer Docker em execução para validar localmente.
