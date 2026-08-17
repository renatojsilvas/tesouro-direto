#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.e2e.yml"
E2E_DIR="$SCRIPT_DIR/tests/TesouroDireto.E2E.Tests"

cleanup() {
  rc=$?
  echo "Destroying E2E environment..."
  docker compose -f "$COMPOSE_FILE" down -v >/dev/null 2>&1 || true
  exit $rc
}
trap cleanup EXIT

echo "Starting E2E environment..."
docker compose -f "$COMPOSE_FILE" up -d --build

echo "Waiting for API..."
timeout 120 bash -c 'until curl -sf http://localhost:5000/health > /dev/null 2>&1; do sleep 2; done'
echo "API healthy."

echo "Seeding database..."
# Seed roda como a role ADMIN (postgres) de proposito: TRUNCATE/INSERT direto
# em tabelas que passam a pertencer a td_app (79-A.2) -- nunca com a
# credencial da aplicacao.
docker exec tesouro-direto-e2e-db psql -U postgres -d tesouro_direto_e2e -f /seed.sql

echo "Waiting for Web..."
timeout 120 bash -c 'until curl -sf http://localhost:5275/ > /dev/null 2>&1; do sleep 2; done'
echo "Web healthy."

echo "Running E2E tests..."
cd "$E2E_DIR"
npx playwright test
