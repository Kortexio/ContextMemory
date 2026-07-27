# ContextMemory API
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_XMLDOC_MODE=skip

COPY ContextMemory.sln ./
COPY src/ContextMemory.Api/ContextMemory.Api.csproj src/ContextMemory.Api/
COPY src/ContextMemory.Adapters/ContextMemory.Adapters.csproj src/ContextMemory.Adapters/
COPY src/ContextMemory.Core/ContextMemory.Core.csproj src/ContextMemory.Core/
COPY src/ContextMemory.Infrastructure/ContextMemory.Infrastructure.csproj src/ContextMemory.Infrastructure/
COPY src/ContextMemory.ServiceDefaults/ContextMemory.ServiceDefaults.csproj src/ContextMemory.ServiceDefaults/
RUN dotnet restore src/ContextMemory.Api/ContextMemory.Api.csproj

COPY src/ContextMemory.Api/ src/ContextMemory.Api/
COPY src/ContextMemory.Adapters/ src/ContextMemory.Adapters/
COPY src/ContextMemory.Core/ src/ContextMemory.Core/
COPY src/ContextMemory.Infrastructure/ src/ContextMemory.Infrastructure/
COPY src/ContextMemory.ServiceDefaults/ src/ContextMemory.ServiceDefaults/
RUN dotnet publish src/ContextMemory.Api/ContextMemory.Api.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app
RUN apk add --no-cache icu-libs curl && adduser -D -u 10001 appuser
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app/publish .
RUN mkdir -p /app/data /app/wikis && chown -R appuser:appuser /app
USER appuser
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "ContextMemory.Api.dll"]
