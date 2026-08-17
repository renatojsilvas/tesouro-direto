-- td-app-role.sql — provisiona a role `td_app` (NOSUPERUSER) e fecha o
-- database para PUBLIC. Tarefa 79-A.1 (docs/PLANO.md).
--
-- IDEMPOTENTE de propósito: este arquivo roda por DOIS caminhos diferentes
-- e precisa produzir o mesmo estado final em qualquer um deles, quantas
-- vezes for executado:
--   1) `/docker-entrypoint-initdb.d/` — só em ambiente NOVO (volume pgdata
--      vazio); o script `infra/postgres/initdb/01-provision-td-app.sh` o
--      invoca, com a senha chegando via variável de ambiente TD_APP_PASSWORD.
--   2) `docker exec ... psql -f` como admin, em ambiente JÁ inicializado —
--      o caminho que a produção percorre de fato, porque o volume `pgdata`
--      é persistente e o initdb NÃO roda de novo sobre ele (mesma armadilha
--      de [[postgres_volume_password]]). Ver `infra/postgres/README.md`.
-- Por isso: `CREATE ROLE` vira `CREATE ou ALTER` conforme `pg_roles`, os
-- REVOKE/GRANT de DATABASE usam `current_database()` (nunca hardcoded —
-- este mesmo arquivo roda em `tesouro_direto` e em `tesouro_direto_e2e`),
-- e todo GRANT/ALTER DEFAULT PRIVILEGES é naturalmente idempotente (GRANT
-- repetido não erra).
--
-- Também IDEMPOTENTE quanto a OWNERSHIP: reassina para `td_app` os objetos
-- que ainda pertencerem à role legada `app`, se ela existir no cluster (ver
-- bloco REASSIGN, abaixo — sem ele, GRANT sozinho não basta para
-- ALTER TABLE/CREATE INDEX/DROP INDEX, que exigem dono, não privilégio).
-- CONSEQUÊNCIA OPERACIONAL, registrada em infra/postgres/README.md: num
-- cluster já inicializado, enquanto a aplicação ainda conectar como `app`
-- (79-A.1 e a janela de trânsito da 79-A.2), toda tabela NOVA que uma
-- migration criar nasce pertencendo a `app` de novo — este script precisa
-- ser reexecutado como admin sempre que isso acontecer, e é seguro
-- reexecutar exatamente porque é idempotente.
--
-- `td_app` NÃO recebe privilégio de extensão: nenhuma migration do
-- repositório cria extensão Postgres (grep por `CREATE EXTENSION`/
-- `HasPostgresExtension` em `src/` = zero — achado 6 do PLANO, tarefa
-- 79-A). O único consumidor de `pg_stat_statements` é o profiling, que
-- passa a rodar como a role admin.

-- Senha via variável de AMBIENTE (TD_APP_PASSWORD), nunca via argv do
-- psql — evita aparecer em `ps`/histórico de shell. `\getenv` deixa
-- `td_app_password` INDEFINIDA se a env var não existir; o `\if` abaixo
-- converge esse caso e o de string vazia para o MESMO estado (variável
-- psql = ''), e o único ponto de falha real é o `RAISE EXCEPTION` dentro
-- do bloco DO logo adiante — com `ON_ERROR_STOP=1` isso faz o psql sair
-- != 0 e com mensagem clara.
--
-- Não usamos `\quit <código>` aqui: `\quit` do psql NÃO aceita argumento
-- de código de saída (a versão com argumento é ignorada com um warning e
-- o psql sai 0 mesmo assim) — usá-lo faria a guarda disparar, pular todo
-- o provisionamento, e o psql concluir com sucesso aparente sem ter
-- criado role nenhuma. Falha silenciosa clássica, evitada aqui de
-- propósito.
\getenv td_app_password TD_APP_PASSWORD
\if :{?td_app_password}
\else
  \set td_app_password ''
\endif

-- Publica a senha recebida numa GUC de sessão para o bloco DO ler.
-- Variáveis psql (`:'nome'`) NÃO são interpoladas dentro de blocos
-- dollar-quoted (`DO $$ ... $$`), então o valor precisa entrar por fora
-- do bloco — este é o jeito canônico de passar um valor de psql para
-- dentro de PL/pgSQL.
--
-- `\o /dev/null` .. `\o` suprime a SAÍDA deste SELECT: sem isso, o psql
-- ecoa o valor de retorno de `set_config` (a própria senha, em claro) no
-- stdout. No caminho do initdb, esse stdout vai para `docker logs
-- tesouro-direto-db`, que o Alloy coleta e envia ao Grafana Cloud — senha
-- de aplicação em log externo é inaceitável. Confirmado por execução (ver
-- verificação da tarefa) que, com a supressão, a senha não aparece em
-- nenhum lugar da saída do psql.
\o /dev/null
SELECT set_config('td_app.provision_password', :'td_app_password', false);
\o

DO $$
DECLARE
  v_password text := current_setting('td_app.provision_password');
BEGIN
  IF v_password = '' THEN
    RAISE EXCEPTION 'TD_APP_PASSWORD não foi definida ou está vazia — obrigatória para provisionar a role td_app.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'td_app') THEN
    -- Converge o estado mesmo se `td_app` tiver sido promovida a algo
    -- diferente de NOSUPERUSER por engano — rebaixa de volta.
    EXECUTE format(
      'ALTER ROLE td_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L',
      v_password
    );
  ELSE
    EXECUTE format(
      'CREATE ROLE td_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L',
      v_password
    );
  END IF;
END
$$;

-- Fecha o database para qualquer role futura na instância: só quem recebe
-- CONNECT explícito conecta. `current_database()` evita hardcode do nome —
-- este arquivo roda tanto em `tesouro_direto` quanto em `tesouro_direto_e2e`.
DO $$
BEGIN
  EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO td_app', current_database());
END
$$;

-- OWNERSHIP dos objetos pré-existentes: reassina de `app` para `td_app`,
-- SE a role legada `app` existir neste cluster. GRANT (abaixo) cobre DML
-- e `CREATE` no schema, mas NÃO cobre `ALTER TABLE`/`CREATE INDEX`/
-- `DROP INDEX` sobre uma tabela já existente — essas operações exigem
-- OWNERSHIP, não privilégio. Sem este bloco, `td_app` sobe, serve leitura
-- e escrita normalmente, e quebra na primeira migration que toque uma
-- tabela pré-existente (medido: `ALTER TABLE titulos ADD COLUMN ... ->
-- ERROR: must be owner of table titulos`, com `app` dona das tabelas
-- pré-79-A — é exatamente o que a migration
-- `20260723171253_AddTituloIndexes` faz em `titulos`).
--
-- NÃO usamos `REASSIGN OWNED BY app TO td_app` aqui: em produção `app` é o
-- superuser de BOOTSTRAP do cluster (o role criado por `POSTGRES_USER=app`
-- na primeira inicialização do volume, OID 10), e por isso é também dono
-- dos catálogos de sistema, que são *pinned*. `REASSIGN OWNED` é
-- tudo-ou-nada — não aceita filtro por schema — e falha inteiro por causa
-- desses catálogos. Medido contra o cluster real:
--   ERROR:  cannot reassign ownership of objects owned by role app
--   because they are required by the database system
--   CONTEXT:  SQL statement "REASSIGN OWNED BY app TO td_app"
--   EXIT=3
-- Com `ON_ERROR_STOP=1`, essa falha abortava o script ali mesmo e todo
-- GRANT/ALTER DEFAULT PRIVILEGES abaixo nunca rodava — só em produção
-- (ambiente novo não tem `app` dona de nada, o bloco antigo era no-op lá
-- e o defeito não aparecia). Por isso o laço abaixo é dirigido: troca o
-- dono só dos objetos do schema `public`, um a um, e nunca toca nos
-- catálogos do sistema.
--
-- Tabelas primeiro, sequências avulsas depois: uma sequência ligada a uma
-- coluna `GENERATED .. AS IDENTITY`/`serial` (dependência `deptype = 'a'`,
-- "auto") segue o dono da tabela automaticamente e REJEITA troca de dono
-- isolada. Medido:
--   ERROR:  cannot change owner of sequence "tributo_faixas_id_seq"
--   DETAIL:  Sequence "tributo_faixas_id_seq" is linked to table
--   "tributo_faixas".
-- Por isso o laço reassina a tabela (o que já resolve a sequência
-- vinculada) e só depois procura sequências SEM esse vínculo.
--
-- Guardado por `pg_roles`: num cluster onde `app` nunca existiu (ou já foi
-- removida, pós-79-A.3), o bloco é NO-OP silencioso — o mesmo arquivo roda
-- em qualquer um dos ambientes.
--
-- Ownership do DATABASE em si (distinto da ownership dos objetos dentro
-- dele) NÃO é tratada aqui — o laço dirigido não mexe em objetos
-- compartilhados da instância. Fica para a 79-A.3, quando `app` for
-- removida (`ALTER DATABASE ... OWNER TO postgres` antes do `DROP ROLE
-- app`).
DO $$
DECLARE r record; n int := 0;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app') THEN
    RETURN;
  END IF;

  -- Tabelas primeiro: sequências "owned" (identity/serial) seguem a tabela
  -- automaticamente e NÃO podem ter dono trocado isoladamente.
  FOR r IN SELECT c.relname FROM pg_class c
             JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public' AND c.relkind IN ('r','p')
              AND pg_get_userbyid(c.relowner) = 'app'
  LOOP
    EXECUTE format('ALTER TABLE public.%I OWNER TO td_app', r.relname);
    n := n + 1;
  END LOOP;

  -- Sequências AVULSAS (sem vínculo de identity/serial a uma tabela).
  FOR r IN SELECT c.relname FROM pg_class c
             JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public' AND c.relkind = 'S'
              AND pg_get_userbyid(c.relowner) = 'app'
              AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype = 'a')
  LOOP
    EXECUTE format('ALTER SEQUENCE public.%I OWNER TO td_app', r.relname);
    n := n + 1;
  END LOOP;

  -- Views e materialized views, se algum dia existirem.
  FOR r IN SELECT c.relname, c.relkind FROM pg_class c
             JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public' AND c.relkind IN ('v','m')
              AND pg_get_userbyid(c.relowner) = 'app'
  LOOP
    EXECUTE format('ALTER %s public.%I OWNER TO td_app',
                   CASE r.relkind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, r.relname);
    n := n + 1;
  END LOOP;

  RAISE NOTICE 'td-app-role: % objeto(s) reassinado(s) de app para td_app', n;
END
$$;

-- GRANTs sobre os objetos hoje existentes em `public`. `CREATE` no schema é
-- necessário porque a aplicação roda migrations (EF) como `td_app` — a
-- 79-B, se e quando acontecer, substitui isso por um schema próprio.
GRANT USAGE, CREATE ON SCHEMA public TO td_app;
GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER
  ON ALL TABLES IN SCHEMA public TO td_app;
GRANT USAGE, SELECT, UPDATE
  ON ALL SEQUENCES IN SCHEMA public TO td_app;
GRANT EXECUTE
  ON ALL FUNCTIONS IN SCHEMA public TO td_app;

-- Mesmos GRANTs para objetos que o usuário corrente (admin) criar DEPOIS —
-- cobre o caso de uma migration futura rodada por admin em vez de td_app.
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER
  ON TABLES TO td_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE
  ON SEQUENCES TO td_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT EXECUTE
  ON FUNCTIONS TO td_app;
