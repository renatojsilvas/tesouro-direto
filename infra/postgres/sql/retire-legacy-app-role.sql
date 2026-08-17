-- retire-legacy-app-role.sql — aposenta a role legada `app` num cluster
-- JÁ INICIALIZADO. Tarefa 79-A.3 (docs/PLANO.md).
--
-- POR QUE ESTE SCRIPT NÃO SEGUE O DESENHO ORIGINAL DO PLANO (achado 3:
-- `REASSIGN OWNED BY app TO td_app` + `ALTER DATABASE ... OWNER TO postgres`
-- + `DROP ROLE app`) — cada passo abaixo foi medido contra o cluster real,
-- inicializado com `POSTGRES_USER=app`:
--
--   1) `REASSIGN OWNED BY app TO td_app`:
--        ERROR:  cannot reassign ownership of objects owned by role app
--        because they are required by the database system
--      `app` é a role de BOOTSTRAP (OID 10, criada pelo initdb a partir de
--      `POSTGRES_USER=app`), dona também dos catálogos de sistema —
--      REASSIGN OWNED é tudo-ou-nada e não aceita filtro por schema. A
--      79-A.1 já resolveu isso com um laço dirigido ao schema `public`
--      (`td-app-role.sql`); este script REUSA o mesmo laço (abaixo), NÃO
--      tenta `REASSIGN OWNED` de novo.
--
--   2) `ALTER DATABASE tesouro_direto OWNER TO postgres`: FUNCIONOU. Não é
--      um catálogo do sistema, é só metadado do database — sem problema.
--
--   3) `DROP ROLE app` (com o database já pertencendo a `postgres`):
--        ERROR:  cannot drop role app because it is required by the
--        database system
--      Confirma OID 10: role de bootstrap, *pinned* pelo Postgres — **não
--      pode ser removida em hipótese alguma** enquanto o cluster for este
--      mesmo cluster. A ÚNICA forma de eliminar literalmente a role `app`
--      seria criar um cluster NOVO (bootstrapado como `postgres` desde o
--      início) e migrar os dados para ele via `pg_dump`/`pg_restore` — isso
--      MOVE DADOS entre clusters/volumes e está fora do limite desta
--      tarefa (nem existe `pg_dump`/`pg_restore` no repositório — grep
--      zero, mesmo achado da 79-B.0 no PLANO).
--
--   4) `ALTER ROLE app NOSUPERUSER`:
--        ERROR:  permission denied to alter role
--        DETAIL:  The bootstrap user must have the SUPERUSER attribute.
--      Também bloqueado pelo Postgres — o bootstrap user é obrigado a
--      permanecer SUPERUSER.
--
--   5) `ALTER ROLE app NOLOGIN`: FUNCIONOU, e a partir daí conectar como
--      `app` dá `FATAL: role "app" is not permitted to log in`. Este é o
--      resultado ACEITÁVEL e o que este script produz: a role de bootstrap
--      continua existindo (o Postgres não permite outra coisa), mas fica
--      sem posse de nada em `public`, sem dono do database, e sem LOGIN —
--      ou seja, "aposentada": existe, mas não pode ser usada como
--      credencial nem tem nada para vazar se for usada.
--
-- Por isso o passo 5 abaixo TENTA `DROP ROLE app` primeiro (cobre o cenário
-- em que alguém tenha criado `app` como role comum, não-bootstrap, caso em
-- que o DROP funciona de verdade) e só cai para `NOLOGIN` se o DROP falhar
-- por ser a role pinned do sistema.
--
-- IDEMPOTENTE e um NO-OP LIMPO em ambiente NOVO: se a role `app` nunca
-- existiu neste cluster (volumes provisionados depois da 79-A, já
-- bootstrapados como `postgres`), o script emite um NOTICE e encerra sem
-- erro — não há nada a aposentar.
--
-- Mesmas convenções de `td-app-role.sql`: `\getenv`, guarda que converge
-- para o mesmo estado em caso de env ausente/vazia, `RAISE EXCEPTION`
-- dentro de bloco DO para abortar com `ON_ERROR_STOP=1`, `current_database()`
-- em vez de nome hardcoded (roda em `tesouro_direto` e `tesouro_direto_e2e`).

-- Passo 1: no-op limpo se `app` não existir neste cluster.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app') THEN
    RAISE NOTICE 'retire-legacy-app-role: role app não existe neste cluster — nada a aposentar (ambiente novo, já bootstrapado como postgres).';
  END IF;
END
$$;

-- Passo 2: garante a role admin `postgres`. Num cluster legado (bootstrap
-- foi `app`) ela NÃO existe até este ponto. Senha via variável de AMBIENTE
-- (POSTGRES_PASSWORD, que já existe no container `db` via compose), nunca
-- por argv — mesmo motivo do `td-app-role.sql`: evita aparecer em
-- `ps`/histórico de shell.
\getenv admin_password POSTGRES_PASSWORD
\if :{?admin_password}
\else
  \set admin_password ''
\endif

-- Publica a senha numa GUC de sessão para o bloco DO ler — variáveis psql
-- não são interpoladas dentro de `DO $$ ... $$`. `\o /dev/null` suprime o
-- eco do valor de retorno de `set_config` (a própria senha) no stdout do
-- psql, pelo mesmo motivo do `td-app-role.sql`.
\o /dev/null
SELECT set_config('retire_app.admin_password', :'admin_password', false);
\o

DO $$
DECLARE
  v_password text := current_setting('retire_app.admin_password');
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app') THEN
    RETURN;
  END IF;

  IF v_password = '' THEN
    RAISE EXCEPTION 'POSTGRES_PASSWORD não foi definida ou está vazia — obrigatória para provisionar/convergir a role admin postgres.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'postgres') THEN
    EXECUTE format('ALTER ROLE postgres WITH LOGIN SUPERUSER PASSWORD %L', v_password);
  ELSE
    EXECUTE format('CREATE ROLE postgres WITH LOGIN SUPERUSER PASSWORD %L', v_password);
  END IF;
END
$$;

-- Passo 3: transfere a ownership do DATABASE para `postgres`, só se ainda
-- pertencer a `app`. Medido: funciona sem restrição (metadado do database,
-- não catálogo do sistema).
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app') THEN
    RETURN;
  END IF;

  IF (SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = current_database()) = 'app' THEN
    EXECUTE format('ALTER DATABASE %I OWNER TO postgres', current_database());
  END IF;
END
$$;

-- Passo 4: varre objetos remanescentes de `app` em `public` para `td_app`.
-- MESMO laço dirigido do `td-app-role.sql` (tabelas → sequências avulsas →
-- views/matviews) — entre a 79-A.1 e esta fase a aplicação pode ter criado
-- objetos como `app` (janela de trânsito da 79-A.2), e `REASSIGN OWNED`
-- continua impossível pelo motivo do cabeçalho.
DO $$
DECLARE r record; n int := 0;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app') THEN
    RETURN;
  END IF;

  FOR r IN SELECT c.relname FROM pg_class c
             JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public' AND c.relkind IN ('r','p')
              AND pg_get_userbyid(c.relowner) = 'app'
  LOOP
    EXECUTE format('ALTER TABLE public.%I OWNER TO td_app', r.relname);
    n := n + 1;
  END LOOP;

  FOR r IN SELECT c.relname FROM pg_class c
             JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public' AND c.relkind = 'S'
              AND pg_get_userbyid(c.relowner) = 'app'
              AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype = 'a')
  LOOP
    EXECUTE format('ALTER SEQUENCE public.%I OWNER TO td_app', r.relname);
    n := n + 1;
  END LOOP;

  FOR r IN SELECT c.relname, c.relkind FROM pg_class c
             JOIN pg_namespace ns ON ns.oid = c.relnamespace
            WHERE ns.nspname = 'public' AND c.relkind IN ('v','m')
              AND pg_get_userbyid(c.relowner) = 'app'
  LOOP
    EXECUTE format('ALTER %s public.%I OWNER TO td_app',
                   CASE r.relkind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, r.relname);
    n := n + 1;
  END LOOP;

  RAISE NOTICE 'retire-legacy-app-role: % objeto(s) reassinado(s) de app para td_app em public', n;
END
$$;

-- Passo 5: tenta remover `app` de verdade; se for a role pinned de
-- bootstrap (o caso de produção), o DROP falha e o script cai para
-- NOLOGIN — credencial morta, role continua existindo porque o Postgres
-- não permite outra coisa (ver cabeçalho).
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app') THEN
    RETURN;
  END IF;

  BEGIN
    EXECUTE 'DROP ROLE app';
    RAISE NOTICE 'retire-legacy-app-role: role app removida (não era a role de bootstrap deste cluster).';
  EXCEPTION WHEN OTHERS THEN
    EXECUTE 'ALTER ROLE app NOLOGIN';
    RAISE NOTICE 'retire-legacy-app-role: app é a role de bootstrap deste cluster (pinned) e não pode ser removida — aplicado NOLOGIN; a credencial legada está morta (FATAL: role "app" is not permitted to log in).';
  END;
END
$$;

-- Passo 6: estado final.
DO $$
DECLARE v_super bool; v_login bool;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app') THEN
    RAISE NOTICE 'retire-legacy-app-role: estado final — role app não existe.';
    RETURN;
  END IF;

  SELECT rolsuper, rolcanlogin INTO v_super, v_login FROM pg_roles WHERE rolname = 'app';
  RAISE NOTICE 'retire-legacy-app-role: estado final — role app existe (rolsuper=%, rolcanlogin=%).', v_super, v_login;
END
$$;
