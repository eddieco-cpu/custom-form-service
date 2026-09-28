# Custom Form Service

This repository contains the ASP.NET Core backend for the V1 custom form service. The current implementation provides the R3 EF Core domain schema, PostgreSQL migration, local database infrastructure, and engineering health endpoints without business API behavior.

## Prerequisites

- .NET SDK 10.0.400 or a compatible patch selected by `global.json`
- A trusted ASP.NET Core development certificate for local HTTPS
- Docker Desktop with Docker Compose

## Technology baseline

| Component | Version |
| --- | --- |
| Target framework | `net10.0` |
| .NET SDK | `10.0.400`, with `latestPatch` roll-forward |
| Entity Framework Core | `10.0.11` |
| repository-local dotnet-ef | `10.0.11` |
| Microsoft.AspNetCore.OpenApi | `10.0.11` |
| Npgsql.EntityFrameworkCore.PostgreSQL | `10.0.3` |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | `10.0.11` |
| Swashbuckle.AspNetCore | `10.2.3` |
| MSTest.Sdk | `4.0.2` |
| Microsoft.AspNetCore.Mvc.Testing | `10.0.11` |

Trust the local development certificate once, if needed:

```powershell
dotnet dev-certs https --trust
```

## Local PostgreSQL configuration

Create the ignored local environment file and replace its password placeholder with a private local value:

```powershell
Copy-Item .env.example .env
notepad .env
```

The `.env` file contains the Database, Username, Password, and host Port used by Docker Compose. It also uses ASP.NET Core's `Section__Key` environment-variable naming, so the same values configure the API after they are imported into the current PowerShell process:

```powershell
Get-Content .env | ForEach-Object {
    if ($_ -match '^(?!#)([^=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
    }
}
```

Do not commit `.env`. The committed `.env.example` contains placeholders only.

## Start and inspect PostgreSQL

```powershell
docker compose config --quiet
docker compose up -d
docker compose ps
```

The Compose project has one `postgres` service. It uses `postgres:17.11-alpine`, publishes the configured host port to container port 5432, and stores data in the `custom-form-service-postgres-data` named volume.

## Restore, build, and test

Run these commands from the repository root:

```powershell
dotnet tool restore
dotnet restore CustomFormService.sln
dotnet ef database update `
  --project src/CustomFormService.Api/CustomFormService.Api.csproj `
  --startup-project src/CustomFormService.Api/CustomFormService.Api.csproj
dotnet build CustomFormService.sln --no-restore --configuration Debug
dotnet test CustomFormService.sln --no-build --configuration Debug
dotnet format CustomFormService.sln --verify-no-changes --no-restore
```

The environment variables from `.env` must be imported before the EF Core and test commands. The integration suite verifies both database readiness and the complete V1 PostgreSQL schema against the running container.

## Database schema and migration

`InitialV1Schema` creates the seven V1 Domain Tables:

- `users`
- `user_identities`
- `forms`
- `questions`
- `options`
- `responses`
- `answers`

EF Core also creates `__EFMigrationsHistory`. The migration contains schema only and does not insert seed data. Use the repository-local tool to inspect migration state:

```powershell
dotnet tool restore
dotnet ef migrations list `
  --project src/CustomFormService.Api/CustomFormService.Api.csproj `
  --startup-project src/CustomFormService.Api/CustomFormService.Api.csproj
```

## Run locally

```powershell
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile https --no-build
```

The `https` launch profile binds both local endpoints:

- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`

Development endpoints:

- Health: `https://localhost:5001/health`
- Database readiness: `https://localhost:5001/health/ready`
- OpenAPI JSON: `https://localhost:5001/openapi/v1.json`
- Swagger UI: `https://localhost:5001/swagger/index.html`

OpenAPI JSON and Swagger UI are available only when `ASPNETCORE_ENVIRONMENT=Development`. `/health` and `/health/ready` are engineering endpoints and are not among the 18 V1 business API endpoints under `/api/v1`. `/health/ready` returns HTTP 200 with `Healthy` only when the API can connect to PostgreSQL.

## Configuration

`src/CustomFormService.Api/appsettings.Development.example.json` is a secret-free example for the standard API ports. `Properties/launchSettings.json` applies those ports for `dotnet run --launch-profile https`.

Future passwords, signing keys, connection strings, tokens, and provider credentials must be supplied through environment variables or a secret store and must not be committed.

## DBeaver connection

Use the following values after PostgreSQL reports healthy:

| Setting | Value |
| --- | --- |
| Host | `localhost` |
| Port | the `PostgreSql__Port` value in local `.env` (`5432` by default) |
| Database | the `PostgreSql__Database` value in local `.env` |
| Username | the `PostgreSql__Username` value in local `.env` |
| Password | the `PostgreSql__Password` value in local `.env` |

In DBeaver, create a PostgreSQL connection, enter these values on the Main tab, choose **Test Connection**, and accept the driver download if DBeaver requests it. Keep SSL disabled for this loopback-only local container unless you separately configure PostgreSQL TLS.

## Stop or rebuild PostgreSQL

Stop the container while preserving all database data:

```powershell
docker compose stop
```

Start it again:

```powershell
docker compose up -d
```

Recreate the container while preserving the named volume:

```powershell
docker compose up -d --force-recreate postgres
```

`docker compose down` removes the container and network but preserves the named volume. `docker compose down -v` permanently removes the Compose volumes and their database data; do not use `-v` for the regular development database.

## R3 boundaries

R3 contains the seven formal Domain Entities, Fluent API mappings, PostgreSQL enums, and the `InitialV1Schema` migration. It includes no seed data, Authentication implementation, Google Authentication flow, Form behavior, Submission behavior, Statistics behavior, or `/api/v1` business endpoint.
