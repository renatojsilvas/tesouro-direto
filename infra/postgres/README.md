# Postgres — provisionamento da role `td_app`

Tarefa 79-A.1 (`docs/PLANO.md`). O objetivo é a role `td_app` (NOSUPERUSER,
NOCREATEDB, NOCREATEROLE) e o database fechado para `PUBLIC` — **nesta
fase a aplicação ainda conecta como `app`**; a troca de credencial é a
79-A.2.

`infra/postgres/sql/td-app-role.sql` é a fonte única da verdade, IDEMPOTENTE,
e roda por dois caminhos:

## Caminho 1 — ambiente novo (initdb)

`infra/postgres/initdb/01-provision-td-app.sh` é montado em
`/docker-entrypoint-initdb.d/` e roda automaticamente na primeira
inicialização de um volume `pgdata` **vazio**, invocando o SQL com
`TD_APP_PASSWORD` do ambiente. Não exige nenhum passo manual: basta
`docker compose up` (ou `down -v && up`) num volume novo.

## Caminho 2 — ambiente já inicializado (a armadilha do pgdata persistente)

O hook de `/docker-entrypoint-initdb.d/` **só roda uma vez, na criação do
cluster**. Um volume `pgdata` que já existe (como o da produção, ou o de
qualquer ambiente local que já rodou `docker compose up` antes desta fase)
**não executa o initdb de novo** — trocar `docker-compose.yml` ou adicionar
o hook não tem efeito nenhum sobre um volume que já foi inicializado (mesma
armadilha registrada em [[postgres_volume_password]]).

Para esses ambientes, rode o SQL manualmente como admin. A senha entra por
variável de AMBIENTE (`TD_APP_PASSWORD`), nunca por `-v` na linha de
comando — evita a senha aparecer em `ps`/histórico de shell; o SQL a lê
com `\getenv`:

```bash
docker exec -e TD_APP_PASSWORD='<senha-da-td_app>' -e PGPASSWORD='<senha-do-admin>' tesouro-direto-db \
  psql -v ON_ERROR_STOP=1 \
  -U <admin> -d tesouro_direto \
  -f /opt/td/sql/td-app-role.sql
```

Troque `<admin>` pelo usuário de bootstrap do cluster (hoje, em produção,
ainda é `app` — a promoção do bootstrap para `postgres` só tem efeito em
volumes novos, ver achado 3 da tarefa 79-A no `docs/PLANO.md`) e
`<senha-da-td_app>` pelo valor de `TD_APP_PASSWORD`.

O mesmo comando roda no e2e (`tesouro-direto-e2e-db`, database
`tesouro_direto_e2e`) — o SQL usa `current_database()`, nunca o nome
hardcoded.

## Reexecução obrigatória na 79-A.2 (troca de connection string)

O `td-app-role.sql` não é "rode uma vez e esqueça" num cluster já
inicializado. Enquanto a aplicação ainda conectar como `app` — o que vale
durante toda a 79-A.1 e a janela de trânsito da 79-A.2, até a connection
string mudar de fato — toda tabela NOVA criada por uma migration nasce
pertencendo a `app`, não a `td_app`. O bloco `REASSIGN OWNED BY app TO
td_app` do script cobre isso, mas só no momento em que roda: se uma
migration criar uma tabela DEPOIS da última execução do script, aquela
tabela específica volta a pertencer a `app` e `td_app` não consegue
`ALTER TABLE`/`CREATE INDEX`/`DROP INDEX` nela até o script rodar de novo.

Por isso: **rode o mesmo comando acima novamente, como admin, no momento
exato em que a 79-A.2 trocar a `DefaultConnection` da aplicação para
`td_app`** — e sempre que, depois disso, houver dúvida sobre ownership. É
seguro reexecutar quantas vezes for preciso porque o script inteiro
(criação/senha de `td_app`, REVOKE/GRANT de database, REASSIGN,
GRANT/ALTER DEFAULT PRIVILEGES de schema) é idempotente.

## Verificação rápida

```sql
SELECT rolname, rolsuper, rolcanlogin FROM pg_roles WHERE rolname = 'td_app';
```

`rolsuper` deve ser `f`.
