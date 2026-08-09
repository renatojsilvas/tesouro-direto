export function requireEnv(name) {
  const value = __ENV[name];
  if (value === undefined || value === null || value.trim() === "") {
    throw new Error(
      `[load] variável obrigatória ${name} não definida — passe -e ${name}=... (sem default; jamais aponta produção)`
    );
  }
  return value;
}

export function assertNaoProd(url) {
  if (url.includes("dadosdotesourodireto.com.br") && __ENV.ALLOW_PROD !== "1") {
    throw new Error(
      `[load] URL aponta para produção (${url}) — defina ALLOW_PROD=1 explicitamente para permitir`
    );
  }
}

export const THRESHOLDS = {
  http_req_duration: ["p(95)<400"],
  http_req_failed: ["rate<0.01"],
};

export function apiBase() {
  const u = requireEnv("API_URL");
  assertNaoProd(u);
  return u.replace(/\/$/, "");
}

export function webBase() {
  const u = requireEnv("WEB_URL");
  assertNaoProd(u);
  return u.replace(/\/$/, "");
}
