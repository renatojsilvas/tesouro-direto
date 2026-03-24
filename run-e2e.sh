#!/bin/bash
set -e

COMPOSE_FILE="docker-compose.e2e.yml"
E2E_DIR="tests/TesouroDireto.E2E.Tests"

cleanup() {
  echo "Destroying E2E environment..."
  docker compose -f "$COMPOSE_FILE" down -v 2>/dev/null
}
trap cleanup EXIT

echo "Starting E2E environment..."
docker compose -f "$COMPOSE_FILE" up -d --build

echo "Waiting for API..."
until curl -sf http://localhost:5000/health > /dev/null 2>&1; do sleep 2; done
echo "API healthy."

echo "Seeding database..."
docker exec tesouro-direto-e2e-db psql -U app -d tesouro_direto_e2e -f /seed.sql

echo "Waiting for Web..."
until curl -sf http://localhost:5275/ > /dev/null 2>&1; do sleep 2; done
echo "Web healthy."

echo "Running E2E tests..."
cd "$E2E_DIR"
npx playwright test
