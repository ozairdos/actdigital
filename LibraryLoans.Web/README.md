# LibraryLoans.Web

Frontend Angular standalone para a operação da biblioteca. A interface consome a API `LibraryLoans.Api` e organiza os fluxos em páginas independentes, com modelos e chamadas HTTP tipados e centralizados.

Este frontend foi desenvolvido por iniciativa própria como complemento à solução do desafio técnico da **ACT Digital**. **Não foi solicitado no desafio**; seu objetivo é facilitar a demonstração das operações da API. O backend pode ser executado e avaliado independentemente desta interface.

## Funcionalidades

| Rota | Fluxo |
| --- | --- |
| / | Visão geral do acervo, empréstimos ativos e atrasados, atividade recente e auditoria |
| /books | Busca, cadastro, edição e desativação de livros, com histórico de empréstimos |
| /loans | Criação de empréstimos com chave de idempotência, devolução e cancelamento |
| /readers | Busca e cadastro de leitores, com consulta de empréstimos por leitor |
| /audit | Eventos de auditoria filtrados por entidade e ID, com detalhes |

## Requisitos

- Node.js 22.12+ da linha 22 (compatível com Angular 21);
- npm;
- API executando no perfil HTTP em `http://localhost:5257`.

## Desenvolvimento

Primeiro, siga [Executar API e frontend em desenvolvimento](../README.md#executar-api-e-frontend-em-desenvolvimento) para iniciar PostgreSQL, Redis e a API. Depois, em outro terminal, na raiz do repositório:

```powershell
cd LibraryLoans.Web
npm ci
npm start
```

Abra `http://localhost:4200`. O proxy encaminha `/api/*` para `http://localhost:5257/*` e remove o prefixo `/api`; assim o navegador usa a mesma origem e não precisa de CORS. Se a porta da API mudar, atualize `proxy.conf.json`.

Inicie a API em `Development` com PostgreSQL e Redis disponíveis. Migrations e dados de demonstração são responsabilidade da API.

No PowerShell, se a política de execução bloquear `npm.ps1`, use `npm.cmd` nos mesmos comandos (por exemplo, `npm.cmd ci` e `npm.cmd start`), sem alterar a política do sistema.

## Estrutura

- `src/app/core/api`: contratos TypeScript e chamadas HTTP;
- `src/app/pages`: dashboard, catálogo, empréstimos, leitores e auditoria, carregados por rota;
- `src/app/shared`: apresentação compartilhada de status e prazos;
- `src/app/app.ts` e `app.shell.html`: navegação, indicador da API e shell responsivo.

Cada fluxo mantém seus estados de carregamento, erro e vazio. Para adicionar uma tela, crie um componente standalone em `pages`, registre uma rota em `app.routes.ts` e use `LibraryApiService` para falar com a API.

## Build e testes

```powershell
npm run build
npm test -- --watch=false
```

O build de produção gera os arquivos estáticos em `dist/library-loans-web/browser/`. Em produção, o servidor web deve servir esses arquivos, redirecionar rotas da interface para `index.html` e encaminhar `/api/*` à API removendo o prefixo `/api`; `proxy.conf.json` é apenas para desenvolvimento.

O build de produção baixa as fontes DM Sans e DM Mono do Google Fonts para incorporá-las ao CSS e requer acesso a `fonts.googleapis.com`. Em desenvolvimento, as fontes são solicitadas pelo navegador, com fallback para as fontes do sistema.

Os testes cobrem a navegação do shell e o envio de empréstimos: repetição com a mesma chave de idempotência após falha de rede, alteração do pedido, bloqueio de envio simultâneo, prazo inválido e atualização após sucesso.

O dashboard calcula seus indicadores sobre os 200 empréstimos mais recentes, e a tela de circulação lista até 500 registros, conforme o limite atual da API. Esses indicadores representam os registros carregados; para totais globais, a API precisará fornecer agregações ou paginação.

A listagem de leitores usa o limite padrão da API (100 registros), e a auditoria solicita até 200 eventos. As buscas e os filtros locais trabalham sobre os registros carregados. A interface não adiciona autenticação; as regras de estoque, concorrência, idempotência e auditoria continuam sob responsabilidade da API.
