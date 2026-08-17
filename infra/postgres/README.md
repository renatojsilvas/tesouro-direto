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

## 79-A.3 — aposentar a role legada

Última etapa da 79-A: depois que a aplicação já conecta como `td_app` (pós
79-A.2) e não há mais nenhum uso da credencial `app`, `app` pode ser
aposentada. **Esta é a ÚNICA fase destrutiva da 79-A e não tem rollback
automático.** Não existe `pg_dump`/`pg_restore` neste repositório (grep
zero) nem passo de backup no pipeline de deploy (`.github/workflows/deploy.yml`)
— por isso, fazer um dump manual antes de rodar este script é
**pré-requisito**, não zelo:

```bash
docker exec -e PGPASSWORD='<senha-do-admin>' tesouro-direto-db \
  pg_dump -U <admin> -d tesouro_direto -Fc -f /tmp/tesouro_direto.dump
docker cp tesouro-direto-db:/tmp/tesouro_direto.dump ./tesouro_direto-$(date +%Y%m%d).dump
```

`infra/postgres/sql/retire-legacy-app-role.sql` é o script (idempotente).
Em cluster legado, `app` ainda é o admin na hora de rodar isto — o comando
usa `-U app` de propósito:

```bash
docker exec -e PGPASSWORD='<senha admin>' tesouro-direto-db \
  psql -v ON_ERROR_STOP=1 \
  -U app -d tesouro_direto \
  -f /opt/td/sql/retire-legacy-app-role.sql
```

O script também cria/converge a role `postgres` (usando `POSTGRES_PASSWORD`
do ambiente do container) antes de transferir a ownership do database para
ela — ver o cabeçalho do arquivo para o porquê de `REASSIGN OWNED`,
`NOSUPERUSER` e `DROP ROLE app` não funcionarem contra a role de bootstrap.

**Estado final esperado:**

- **Cluster legado** (o que importa em produção): a role `app` continua
  existindo — é a role de bootstrap do cluster (OID 10), *pinned*, o
  Postgres não permite removê-la — mas fica `SUPERUSER` (obrigatório pelo
  Postgres) e `NOLOGIN` (aplicado por este script), sem nenhum objeto
  próprio em `public`, e o database passa a pertencer a `postgres`.
  Conectar como `app` dá `FATAL: role "app" is not permitted to log in`.
  Remover `app` literalmente exigiria criar um cluster novo (bootstrapado
  como `postgres`) e migrar os dados por dump/restore — fora do limite
  desta tarefa, porque move dados.
- **Ambiente novo** (volume provisionado depois da 79-A, já bootstrapado
  como `postgres`): a role `app` nunca existiu — o script é um no-op que
  só emite um `NOTICE`.

## Rollout em produção (VPS) — dois deploys com um passo manual entre eles

O `pgdata` da VPS **já está inicializado com bootstrap `app`**. Consequências que
governam a ordem: `POSTGRES_USER: postgres` é inerte lá, o hook do initdb **não
roda**, e o SQL só chega ao container pelo bind-mount — que só existe depois de
um deploy. Por isso a 79-A.1 precisa ir a produção **sozinha**.

**0. Antes de qualquer merge**

1. Cadastrar o secret `TD_APP_PASSWORD` no GitHub. Sem ele a interpolação do
   compose falha por inteiro e nenhum serviço é recriado.
2. Dump manual (não existe passo de backup no `deploy.yml`):

```bash
docker exec tesouro-direto-db pg_dump -U app -d tesouro_direto -Fc \
  > /root/td-pre79a-$(date +%F).dump
```

**1. Deploy A — só a 79-A.1.** Entram os bind-mounts e a variável; a aplicação
continua conectando como `app`, sem mudança de comportamento. O `db` é recriado
(a definição do serviço mudou). Conferir: `docker exec tesouro-direto-db ls /opt/td/sql`.

**2. Passo manual — provisionar `td_app`** (o initdb não faz isso aqui):

```bash
docker exec -e TD_APP_PASSWORD='<secret>' -e PGPASSWORD='<DB_PASSWORD atual>' \
  tesouro-direto-db psql -v ON_ERROR_STOP=1 -U app -d tesouro_direto \
  -f /opt/td/sql/td-app-role.sql
```

Só avance se: `rolsuper` de `td_app` = `f`, `datacl` sem `c` para PUBLIC, tabelas
de `public` com dono `td_app`, e uma conexão como `td_app` funcionando. É aqui
que se evita o modo de falha "a aplicação sobe e quebra na primeira escrita".

**3. Deploy B — 79-A.2 e 79-A.3.** A aplicação passa a conectar como `td_app`.
Conferir em `pg_stat_activity` que `usename = td_app`.

**4. Passo manual — aposentar a role legada** (destrutivo, só depois de soak):

```bash
docker exec -e PGPASSWORD='<DB_PASSWORD>' tesouro-direto-db \
  psql -v ON_ERROR_STOP=1 -U app -d tesouro_direto \
  -f /opt/td/sql/retire-legacy-app-role.sql
```

Cria a role admin `postgres` (que **não existe** na VPS hoje), passa a ownership
do database e aplica `NOLOGIN` em `app`.

**Rollback.** Antes do passo 4, reverter o commit da 79-A.2 basta — `app` ainda
loga. Depois do passo 4, é preciso `ALTER ROLE app LOGIN` como `postgres` antes
de reverter.

**Duas pegadinhas.** `DB_PASSWORD` passa a significar a senha do **admin** (o
valor não muda). E `tests/load/profiling/run-profile.sh` assume
`DB_ADMIN_USER=postgres`: **antes** do passo 4, rodá-lo com `DB_ADMIN_USER=app`.
