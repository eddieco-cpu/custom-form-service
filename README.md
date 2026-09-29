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

## Local configuration

The root `.env` file belongs only to Docker Compose. Create it and replace its PostgreSQL password placeholder with a private local value:

```powershell
Copy-Item .env.example .env
notepad .env
```

Its supported keys are `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, and the optional `POSTGRES_PORT` host mapping. ASP.NET Core does not parse this file and it must not be imported into PowerShell.

```powershell
dotnet user-secrets set --project src/CustomFormService.Api/CustomFormService.Api.csproj "PostgreSql:Password" "<same-password-as-.env>"
dotnet user-secrets set --project src/CustomFormService.Api/CustomFormService.Api.csproj "Jwt:SigningKey" "<at-least-32-random-bytes>"
```

The PostgreSQL password is intentionally configured twice: `POSTGRES_PASSWORD` is the server credential used by Docker, while `PostgreSql:Password` is the client credential used by the API. They must have the same local value. User Secrets are stored outside the repository and are not encrypted; use development-only values.

Do not commit `.env`. The committed `.env.example` contains placeholders only. If you change `POSTGRES_PORT`, `POSTGRES_DB`, or `POSTGRES_USER`, also override the matching `PostgreSql:Port`, `PostgreSql:Database`, or `PostgreSql:Username` API setting through User Secrets or an OS environment variable.

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

The API and integration tests use the standard ASP.NET Core configuration pipeline, including Development User Secrets. No `.env` import step is required. The integration suite verifies both database readiness and the complete V1 PostgreSQL schema against the running container.

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
dotnet run --project src/CustomFormService.Api/CustomFormService.Api.csproj --launch-profile http --no-build
```

The standard Development browser topology uses the HTTP endpoint `http://localhost:5000` with a frontend at `http://localhost:3000`. The separate `https` launch profile remains available for non-browser development, but HTTPS API plus HTTP frontend is not the standard Cookie/CORS topology for this project.

Development endpoints:

- Health: `http://localhost:5000/health`
- Database readiness: `http://localhost:5000/health/ready`
- OpenAPI JSON: `http://localhost:5000/openapi/v1.json`
- Swagger UI: `http://localhost:5000/swagger/index.html`

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

ASP.NET Core uses its standard provider order: `appsettings.json`, `appsettings.{Environment}.json`, Development User Secrets, OS environment variables, and command-line overrides. Application code consumes bound options and does not know which provider supplied a value.

Reference documentation: [ASP.NET Core 10 configuration](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0), [Options pattern](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0), [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0), and [CORS](https://learn.microsoft.com/aspnet/core/security/cors?view=aspnetcore-10.0).

`appsettings.json` contains non-secret shared defaults. `appsettings.Development.json` contains localhost PostgreSQL metadata, `http://localhost:3000` as the allowed browser origin, and `AccessTokenCookie:Secure=false`. Staging and Production inherit `AccessTokenCookie:Secure=true`; setting it to false outside Development fails startup.

The JWT lifetime defaults to 1,440 minutes and accepts an explicit positive override such as `Jwt__LifetimeMinutes=60`. Passwords, signing keys, tokens, and provider credentials must be supplied through User Secrets or deployment environment variables and must not be committed.

For Railway Staging or Production, use the same ASP.NET Core keys with double underscores, for example:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:${PORT}
PostgreSql__Host=...
PostgreSql__Port=5432
PostgreSql__Database=...
PostgreSql__Username=...
PostgreSql__Password=...
Jwt__SigningKey=...
Cors__AllowedOrigins__0=https://app.example.com
```

Environment-specific values override committed defaults; defaults do not need to be repeated in Railway.

## Cookie, CORS, and CSRF boundary

The `access_token` cookie is HttpOnly, SameSite=Lax, Path `/`, and has no Domain attribute. It is Secure outside Development. Its absolute `Expires` value equals the JWT `exp`; no Cookie `Max-Age` is set. Login and Logout use the same Path, Domain, SameSite, Secure, and HttpOnly scope.

Browsers scope cookies by host rather than port. In Development, `localhost:3000` and `localhost:5000` share the `localhost` cookie host namespace even though they are different origins. HttpOnly prevents frontend JavaScript from reading the token, but it does not isolate the cookie by port. This is an accepted local trade-off. In Production, `app.example.com` and `api.example.com` are different hosts, so the API host-only cookie is not sent to the frontend host.

Frontend requests to the API must opt into credentials:

```javascript
fetch("http://localhost:5000/api/v1/users/me", { credentials: "include" })
```

or set Axios `withCredentials: true`. CORS origins must use canonical `scheme://host[:port]` form with no trailing slash, path, query, fragment, userinfo, or wildcard.

CORS is not CSRF protection. V1 relies on SameSite=Lax, a host-only cookie, same-scheme trusted same-site subdomains, and the rule that state-changing endpoints never use GET. This Round does not add an antiforgery protocol; cross-site deployment, SameSite=None, iframe embedding, or untrusted same-site subdomains require a fresh CSRF review.

## DBeaver connection

Use the following values after PostgreSQL reports healthy:

| Setting | Value |
| --- | --- |
| Host | `localhost` |
| Port | the `POSTGRES_PORT` value in local `.env` (`5432` by default) |
| Database | the `POSTGRES_DB` value in local `.env` |
| Username | the `POSTGRES_USER` value in local `.env` |
| Password | the `POSTGRES_PASSWORD` value in local `.env` |

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
