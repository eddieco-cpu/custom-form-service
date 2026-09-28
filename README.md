# Custom Form Service

This repository contains the ASP.NET Core backend for the V1 custom form service. The current implementation includes the R3 PostgreSQL schema and the R4 Email/Password authentication and current-user APIs.

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
| Microsoft.AspNetCore.Authentication.JwtBearer | `10.0.11` |
| Npgsql.EntityFrameworkCore.PostgreSQL | `10.0.3` |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | `10.0.11` |
| Swashbuckle.AspNetCore | `10.2.3` |
| MSTest.Sdk | `4.0.2` |
| Microsoft.NET.Test.Sdk / runner | `18.0.1` / VSTest |
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

Replace `Jwt__SigningKey` with a private random value of at least 32 bytes. `Jwt__Issuer` and `Jwt__Audience` have non-secret defaults in `appsettings.json`. The formal V1 access-token lifetime is 24 hours, represented by `Jwt__LifetimeMinutes=1440`; startup rejects a different value. `Cors__AllowedOrigins__0` is the explicit local browser-client origin; add more numbered entries only for known clients. Credentialed CORS never accepts a wildcard origin.

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

## R4 Email Authentication and User APIs

R4 provides these formal V1 endpoints:

- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/logout`
- `GET /api/v1/users/me`
- `PATCH /api/v1/users/me`

Registration creates an active user but does not sign in. Login returns the public User resource and sets the system JWT in the `access_token` HttpOnly cookie; the token is never returned in the JSON body. Protected requests accept this cookie only and recheck that the user still exists and is active. Logout clears the client cookie even when the incoming cookie is missing, invalid, or expired.

The following PowerShell example uses synthetic data and keeps the cookie in an in-memory web session:

```powershell
$body = @{ email = 'local-test@example.test'; password = 'local password'; displayName = 'Local Test' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/v1/auth/register -ContentType application/json -Body $body

$login = @{ email = 'local-test@example.test'; password = 'local password' } | ConvertTo-Json
Invoke-WebRequest -Method Post -Uri http://localhost:5000/api/v1/auth/login -ContentType application/json -Body $login -SessionVariable authSession
Invoke-RestMethod -Uri http://localhost:5000/api/v1/users/me -WebSession $authSession

$profile = @{ displayName = 'Updated Local Test' } | ConvertTo-Json
Invoke-RestMethod -Method Patch -Uri http://localhost:5000/api/v1/users/me -ContentType application/json -Body $profile -WebSession $authSession
Invoke-WebRequest -Method Post -Uri http://localhost:5000/api/v1/auth/logout -WebSession $authSession
```

Delete the synthetic user from the local database after manual verification if you do not want to retain it. Google Authentication remains part of the formal V1 contract and is deferred to the separate G1 round; R4 does not expose a placeholder Google endpoint.

## Configuration

`src/CustomFormService.Api/appsettings.Development.example.json` is a secret-free example for the standard API ports. `Properties/launchSettings.json` applies those ports for `dotnet run --launch-profile https`.

Passwords, signing keys, connection strings, tokens, and provider credentials must be supplied through environment variables or a secret store and must not be committed.

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

## Current boundaries

The database still contains the seven formal Domain Entities and the unchanged `InitialV1Schema` migration. R4 adds no migration or seed data. Google Authentication, Form behavior, Submission behavior, Statistics behavior, refresh tokens, email verification, password reset, sessions, and RBAC are not implemented in this round.
