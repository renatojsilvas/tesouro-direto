FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/*/*.csproj ./src-projects/
RUN for f in src-projects/*.csproj; do \
      [ -f "$f" ] || continue; \
      name=$(basename $f .csproj); \
      dir="src/$name"; \
      mkdir -p "$dir" && mv "$f" "$dir/"; \
    done && \
    rm -rf src-projects
RUN dotnet restore src/TesouroDireto.API/TesouroDireto.API.csproj -r linux-x64 -p:PublishReadyToRun=true
COPY src/ src/
RUN dotnet publish src/TesouroDireto.API/TesouroDireto.API.csproj -c Release -r linux-x64 --self-contained false -p:PublishReadyToRun=true -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TesouroDireto.API.dll"]
