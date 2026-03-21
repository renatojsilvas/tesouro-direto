#!/bin/bash

PROJECT_NAME="TesouroDireto"
ROOT_DIR=$(pwd)
ERRORS=0

run() {
    if ! "$@"; then
        echo "⚠️  Falhou: $*"
        ERRORS=$((ERRORS + 1))
    fi
}

echo "🚀 Criando projeto $PROJECT_NAME em $ROOT_DIR"
echo "================================================"

# ═══ FASE 1: Infra files ═══

cat > docker-compose.yml << 'EOF'
services:
  app:
    build: .
    container_name: tesouro-direto-app
    ports:
      - "5000:8080"
    environment:
      - ConnectionStrings__DefaultConnection=Host=db;Port=5432;Database=tesouro_direto;Username=app;Password=app123
      - ASPNETCORE_ENVIRONMENT=Production
      - Serilog__WriteTo__1__Args__uri=http://loki:3100
    depends_on:
      db:
        condition: service_healthy

  db:
    image: postgres:16-alpine
    container_name: tesouro-direto-db
    environment:
      POSTGRES_DB: tesouro_direto
      POSTGRES_USER: app
      POSTGRES_PASSWORD: app123
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app -d tesouro_direto"]
      interval: 5s
      timeout: 5s
      retries: 5

  # Observabilidade
  grafana:
    image: grafana/grafana:latest
    container_name: tesouro-direto-grafana
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
    volumes:
      - grafana-data:/var/lib/grafana

  loki:
    image: grafana/loki:latest
    container_name: tesouro-direto-loki
    ports:
      - "3100:3100"

  prometheus:
    image: prom/prometheus:latest
    container_name: tesouro-direto-prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml

  # Analise estatica
  sonarqube:
    image: sonarqube:lts-community
    container_name: tesouro-direto-sonar
    ports:
      - "9000:9000"
    environment:
      - SONAR_ES_BOOTSTRAP_CHECKS_DISABLE=true
    volumes:
      - sonar-data:/opt/sonarqube/data

volumes:
  pgdata:
  grafana-data:
  sonar-data:
EOF

cat > prometheus.yml << 'EOF'
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'tesouro-direto-api'
    metrics_path: /metrics
    static_configs:
      - targets: ['app:8080']
EOF

cat > sonar-project.properties << 'EOF'
sonar.projectKey=tesouro-direto
sonar.projectName=TesouroDireto
sonar.sources=src/
sonar.tests=tests/
sonar.exclusions=**/bin/**,**/obj/**,**/Migrations/**
sonar.cs.opencover.reportsPaths=**/coverage.opencover.xml
sonar.host.url=http://localhost:9000
EOF

cat > .dockerignore << 'EOF'
**/bin/
**/obj/
**/.vs/
**/.vscode/
**/node_modules/
.git/
.github/
*.md
!CLAUDE.md
EOF

cat > Dockerfile << 'DOCKERFILE'
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.sln .
COPY src/*/*.csproj ./src-projects/
COPY tests/*/*.csproj ./test-projects/
RUN for f in src-projects/*.csproj; do \
      [ -f "$f" ] || continue; \
      name=$(basename $f .csproj); \
      dir="src/$name"; \
      mkdir -p "$dir" && mv "$f" "$dir/"; \
    done && \
    for f in test-projects/*.csproj; do \
      [ -f "$f" ] || continue; \
      name=$(basename $f .csproj); \
      dir="tests/$name"; \
      mkdir -p "$dir" && mv "$f" "$dir/"; \
    done && \
    rm -rf src-projects test-projects
RUN dotnet restore
COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/TesouroDireto.API/TesouroDireto.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TesouroDireto.API.dll"]
DOCKERFILE

mkdir -p .github/workflows

cat > .github/workflows/deploy.yml << 'GHACTION'
name: Deploy to VPS

on:
  push:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_DB: tesouro_direto_test
          POSTGRES_USER: app
          POSTGRES_PASSWORD: app123
        ports: ["5432:5432"]
        options: >-
          --health-cmd pg_isready
          --health-interval 5s
          --health-timeout 5s
          --health-retries 5
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
      - name: SonarQube Scan
        if: env.SONAR_TOKEN != ''
        uses: SonarSource/sonarqube-scan-action@v5
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
          SONAR_HOST_URL: ${{ secrets.SONAR_HOST_URL }}

  deploy:
    needs: test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Deploy via SSH
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.VPS_HOST }}
          username: ${{ secrets.VPS_USER }}
          key: ${{ secrets.VPS_SSH_KEY }}
          script: |
            cd /opt/tesouro-direto
            git pull origin main
            docker compose build --no-cache
            docker compose up -d
            docker image prune -f
GHACTION

cat > .gitignore << 'EOF'
bin/
obj/
*.user
*.suo
*.cache
*.log
node_modules/
test-results/
playwright-report/
.vs/
.vscode/
.idea/
*.swp
.DS_Store
Thumbs.db
pgdata/
grafana-data/
.sonarqube/
EOF

# ═══ FASE 2: Folder structure ═══

mkdir -p specs
mkdir -p docs/arch

# Warnings como erros — vale pra todos os projetos da solution
cat > Directory.Build.props << 'EOF'
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF

mkdir -p src/$PROJECT_NAME.Domain/Common
mkdir -p src/$PROJECT_NAME.Application/Common/DTOs
mkdir -p src/$PROJECT_NAME.Application/Common/Interfaces
mkdir -p src/$PROJECT_NAME.Infrastructure/Persistence/Configurations
mkdir -p src/$PROJECT_NAME.Infrastructure/Persistence/Repositories
mkdir -p src/$PROJECT_NAME.Infrastructure/Observability
mkdir -p src/$PROJECT_NAME.Infrastructure/CsvImport
mkdir -p src/$PROJECT_NAME.API/Endpoints
mkdir -p src/$PROJECT_NAME.API/Contracts
mkdir -p src/$PROJECT_NAME.API/Extensions
mkdir -p src/$PROJECT_NAME.API/Middleware
mkdir -p src/$PROJECT_NAME.Web/Pages
mkdir -p src/$PROJECT_NAME.Web/Components
mkdir -p src/$PROJECT_NAME.Web/Services
mkdir -p tests/$PROJECT_NAME.E2E.Tests/tests

# ═══ FASE 3: Playwright ═══

cat > tests/$PROJECT_NAME.E2E.Tests/package.json << 'EOF'
{
  "name": "tesouro-direto-e2e",
  "private": true,
  "scripts": { "test": "npx playwright test" },
  "devDependencies": { "@playwright/test": "^1.42.0" }
}
EOF

# ═══ FASE 4: .NET Solution ═══

run dotnet new sln -n $PROJECT_NAME
run dotnet new classlib -n $PROJECT_NAME.Domain -o src/$PROJECT_NAME.Domain --framework net8.0
run dotnet new classlib -n $PROJECT_NAME.Application -o src/$PROJECT_NAME.Application --framework net8.0
run dotnet new classlib -n $PROJECT_NAME.Infrastructure -o src/$PROJECT_NAME.Infrastructure --framework net8.0
run dotnet new web -n $PROJECT_NAME.API -o src/$PROJECT_NAME.API --framework net8.0
run dotnet new blazorserver -n $PROJECT_NAME.Web -o src/$PROJECT_NAME.Web --framework net8.0
run dotnet new xunit -n $PROJECT_NAME.Domain.Tests -o tests/$PROJECT_NAME.Domain.Tests --framework net8.0
run dotnet new xunit -n $PROJECT_NAME.Application.Tests -o tests/$PROJECT_NAME.Application.Tests --framework net8.0
run dotnet new xunit -n $PROJECT_NAME.API.Tests -o tests/$PROJECT_NAME.API.Tests --framework net8.0

run dotnet sln add src/$PROJECT_NAME.Domain src/$PROJECT_NAME.Application src/$PROJECT_NAME.Infrastructure src/$PROJECT_NAME.API src/$PROJECT_NAME.Web
run dotnet sln add tests/$PROJECT_NAME.Domain.Tests tests/$PROJECT_NAME.Application.Tests tests/$PROJECT_NAME.API.Tests

# Clean Architecture references
run dotnet add src/$PROJECT_NAME.Application reference src/$PROJECT_NAME.Domain
run dotnet add src/$PROJECT_NAME.Infrastructure reference src/$PROJECT_NAME.Domain
run dotnet add src/$PROJECT_NAME.Infrastructure reference src/$PROJECT_NAME.Application
run dotnet add src/$PROJECT_NAME.API reference src/$PROJECT_NAME.Application
run dotnet add src/$PROJECT_NAME.API reference src/$PROJECT_NAME.Infrastructure
run dotnet add src/$PROJECT_NAME.Web reference src/$PROJECT_NAME.Application
run dotnet add tests/$PROJECT_NAME.Domain.Tests reference src/$PROJECT_NAME.Domain
run dotnet add tests/$PROJECT_NAME.Application.Tests reference src/$PROJECT_NAME.Domain
run dotnet add tests/$PROJECT_NAME.Application.Tests reference src/$PROJECT_NAME.Application
run dotnet add tests/$PROJECT_NAME.API.Tests reference src/$PROJECT_NAME.API

# ═══ FASE 5: NuGet ═══

# Application
run dotnet add src/$PROJECT_NAME.Application package MediatR --version 12.5.0

# Infrastructure — EF Core + Dapper + Serilog
run dotnet add src/$PROJECT_NAME.Infrastructure package Microsoft.EntityFrameworkCore --version "8.0.*"
run dotnet add src/$PROJECT_NAME.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL --version "8.0.*"
run dotnet add src/$PROJECT_NAME.Infrastructure package Microsoft.EntityFrameworkCore.Design --version "8.0.*"
run dotnet add src/$PROJECT_NAME.Infrastructure package Dapper
run dotnet add src/$PROJECT_NAME.Infrastructure package Npgsql

# API — MediatR + Serilog + Metrics
run dotnet add src/$PROJECT_NAME.API package MediatR --version 12.5.0
run dotnet add src/$PROJECT_NAME.API package Serilog.AspNetCore
run dotnet add src/$PROJECT_NAME.API package Serilog.Sinks.Grafana.Loki
run dotnet add src/$PROJECT_NAME.API package prometheus-net.AspNetCore

# Tests
run dotnet add tests/$PROJECT_NAME.Domain.Tests package FluentAssertions
run dotnet add tests/$PROJECT_NAME.Application.Tests package FluentAssertions
run dotnet add tests/$PROJECT_NAME.Application.Tests package NSubstitute
run dotnet add tests/$PROJECT_NAME.API.Tests package FluentAssertions
run dotnet add tests/$PROJECT_NAME.API.Tests package Microsoft.AspNetCore.Mvc.Testing --version "8.0.*"
run dotnet add tests/$PROJECT_NAME.API.Tests package Testcontainers.PostgreSql

# ═══ FASE 6: Cleanup + config ═══

rm -f src/$PROJECT_NAME.Domain/Class1.cs src/$PROJECT_NAME.Application/Class1.cs src/$PROJECT_NAME.Infrastructure/Class1.cs
rm -f tests/$PROJECT_NAME.Domain.Tests/UnitTest1.cs tests/$PROJECT_NAME.Application.Tests/UnitTest1.cs tests/$PROJECT_NAME.API.Tests/UnitTest1.cs

cat > src/$PROJECT_NAME.API/appsettings.json << EOF
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=tesouro_direto;Username=app;Password=app123" },
  "Serilog": {
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "GrafanaLoki", "Args": { "uri": "http://localhost:3100" } }
    ],
    "Enrich": ["FromLogContext", "WithCorrelationId"]
  },
  "CsvImport": {
    "Url": "https://www.tesourotransparente.gov.br/ckan/dataset/df56aa42-484a-4a59-8184-7676580c81e3/resource/796d2059-14e9-44e3-80c9-2d9e30b405c1/download/precotaxatesourodireto.csv"
  }
}
EOF

# ═══ FASE 7: Build + Git ═══

run dotnet build --verbosity quiet
git init && git add -A && git commit -m "chore: initial project setup" --quiet

echo "================================================"
if [ $ERRORS -gt 0 ]; then echo "⚠️  Setup concluido com $ERRORS erro(s)."; else echo "✅ Setup completo!"; fi
echo "1. docker compose up -d"
echo "2. claude"
echo "3. /pipeline foundation Configurar Result, Error e classes base do dominio"
echo "Grafana: http://localhost:3000 (admin/admin)"
echo "SonarQube: http://localhost:9000 (admin/admin)"
echo "================================================"
