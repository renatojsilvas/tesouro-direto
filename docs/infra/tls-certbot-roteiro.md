# TLS/HTTPS via Certbot — roteiro manual no VPS

Roteiro para rodar como `root` no VPS (`157.230.148.98`), fora do pipeline de deploy. Idempotente: pode ser executado mais de uma vez sem efeito colateral — cada passo tem guarda própria.

## Pré-condições

DNS já precisa resolver o domínio para o VPS antes de começar:

```
dig +short @8.8.8.8 dadosdotesourodireto.com.br
```

Esperado: `157.230.148.98`.

Variáveis usadas nos blocos abaixo (exportar no shell antes de rodar):

```
DOMAIN=dadosdotesourodireto.com.br
EMAIL=renatojsilvas@gmail.com
```

## Passo 1 — firewall

```
ufw allow 80/tcp
ufw allow 443/tcp
```

Idempotente: `ufw` deduplica regras repetidas. Manter a porta `3080` aberta — ela continua servindo como break-glass durante e depois da migração.

## Passo 2 — instalar certbot

```
apt-get update && apt-get install -y certbot
```

Idempotente: `apt-get install` não reinstala se já presente.

## Passo 3 — webroot

```
mkdir -p /var/www/certbot
```

Idempotente: `mkdir -p` não falha se o diretório já existe.

## Passo 4 — emitir o certificado (antes do deploy do conf novo)

Roda **antes** do merge do `tesouro-direto.conf` novo, porque hoje o nginx do host só escuta `3080` — a porta 80 está livre e o `--standalone` do certbot pode bindar nela sem conflito.

Guarda de idempotência: só emite se o certificado ainda não existe.

```
if [ ! -f /etc/letsencrypt/live/$DOMAIN/fullchain.pem ]; then
  certbot certonly --standalone --non-interactive --agree-tos -m $EMAIL --preferred-challenges http -d $DOMAIN -d www.$DOMAIN
fi
```

Atenção: se este bloco for executado de novo **depois** que o nginx já estiver ocupando a porta 80 (passo 5 em diante), o `--standalone` vai falhar ao tentar bindar 80 — é exatamente por isso que a guarda `if [ ! -f ... ]` existe: depois da primeira emissão bem-sucedida, o bloco vira no-op.

## Passo 5 — merge/deploy do conf novo

Só agora fazer o merge que leva o `infra/nginx/tesouro-direto.conf` novo para o `main`. O pipeline de deploy faz `cp infra/nginx/tesouro-direto.conf /etc/nginx/sites-enabled/tesouro-direto` e em seguida `nginx -t && systemctl reload nginx`. Como o certificado já existe (passo 4), `nginx -t` encontra os arquivos `fullchain.pem`/`privkey.pem` referenciados no `ssl_certificate`/`ssl_certificate_key` e passa.

Fallback manual, se quiser aplicar sem esperar o CI:

```
cp /opt/tesouro-direto/infra/nginx/tesouro-direto.conf /etc/nginx/sites-enabled/tesouro-direto && nginx -t && systemctl reload nginx
```

## Passo 6 — converter renovação para webroot

Depois do passo 5, o nginx passa a ocupar a porta 80 (redirect 80→443), então o `--standalone` do certbot não consegue mais bindar nela para renovar. Converter a renovação para `--webroot` (usa o `location /.well-known/acme-challenge/` já presente no conf) e fixar um `--deploy-hook` que recarrega o nginx após renovar.

Guarda de idempotência: só converte se a renovação ainda não estiver configurada como webroot.

```
if ! grep -q "authenticator = webroot" /etc/letsencrypt/renewal/$DOMAIN.conf 2>/dev/null; then
  certbot certonly --webroot -w /var/www/certbot --non-interactive --agree-tos -m $EMAIL --force-renewal -d $DOMAIN -d www.$DOMAIN --deploy-hook "systemctl reload nginx"
fi
```

## Passo 7 — verificar renovação e timer

```
certbot renew --dry-run
```

Espera-se que o dry-run passe usando o desafio via webroot (não standalone).

```
systemctl status certbot.timer
```

Espera-se `active`/`enabled`. Se não estiver:

```
systemctl enable --now certbot.timer
```

## Smoke test pós-deploy

```
curl -sI http://dadosdotesourodireto.com.br
```

Esperado: `301` redirecionando para `https://`.

```
curl -sI https://dadosdotesourodireto.com.br
```

Esperado: `200` e o header `strict-transport-security: max-age=300` presente.

Conferir também no browser que o cadeado aparece válido (cadeia completa, sem aviso).

## Rollback

1. O certificado é emitido **antes** do conf novo (passo 4 antes do passo 5) — se a emissão falhar, o conf de produção não muda e nada quebra.
2. Se `nginx -t` falhar no passo do deploy (por exemplo, cert ausente), o `reload` é pulado e o nginx continua rodando com o conf antigo.
3. A porta `3080` continua servindo durante toda a migração como break-glass.
4. Para reverter o conf manualmente:

```
git -C /opt/tesouro-direto checkout <commit-anterior> -- infra/nginx/tesouro-direto.conf
cp /opt/tesouro-direto/infra/nginx/tesouro-direto.conf /etc/nginx/sites-enabled/tesouro-direto
nginx -t && systemctl reload nginx
```

5. O HSTS está com `max-age=300` (5 minutos) de propósito: se o HTTPS quebrar depois do deploy, o browser para de forçar `https://` em 5 minutos, evitando lockout prolongado dos clientes.

## Checklist de consumidores

- [obsoleto] Secret `GRAFANA_ROOT_URL` no GitHub → não existe mais. A tarefa 77 removeu o Grafana local (e o secret `GRAFANA_ROOT_URL`/`GRAFANA_PASSWORD` junto); não há mais rota `/grafana/` no nginx, nem Grafana rodando na VPS para apontar um root URL.
- [NÃO muda] `ApiSettings__BaseUrl` do Web = `http://app:8080` — é rede interna do compose, não passa pelo nginx do host.
- [NÃO muda] Healthcheck do deploy (`curl localhost:5000/health/ready`) — bypassa o nginx.
- [NÃO muda] E2E (`run-e2e.sh` usa `localhost:5000` e `localhost:5275`) — bypassa o nginx.
- [obsoleto] Túneis SSH para `/grafana/`/`/prometheus/`: as duas rotas saíram de `infra/nginx/tesouro-direto.conf` na tarefa 77 (não há mais Grafana nem Prometheus locais para tunelar até — Grafana e alerting vivem no Grafana Cloud, e o Prometheus local só sobe efêmero sob `--profile load` para teste de carga). Um túnel para essas rotas hoje bate em 404.

## Follow-ups (não fazer agora, só registrar)

- Subir o HSTS de `max-age=300` para `max-age=31536000` depois de validado em produção por um período razoável.
- Remover o server `listen 3080` e eliminar a duplicação de `location`s entre os blocos `443` e `3080`.
- Avaliar `includeSubDomains`/`preload` no header HSTS, opcional.
