# Custom Form Service

This repository contains the local Email/Password backend (A-stage) for the V1 custom form service. R3–R10 implement the complete seven-table schema and 17 business endpoints, including Draft editing, lifecycle, submissions, Latest Response, respondent count, and Closed Statistics. R11 independent integration acceptance is in progress; implementation does not establish acceptance. See the [paired R11 plan](docs/test-plan/R11_Acceptance_Test.md) and [historical status sources](docs/implementation/R11_Historical_Status.md). Google Authentication remains pending in G1; frontend and deployment have separate plans.

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

The project deliberately separates Docker Compose infrastructure settings from ASP.NET Core application settings. The root `.env` belongs only to Docker Compose; API secrets belong in Development User Secrets. See the [environment configuration guide and audit](docs/configuration/environment-configuration.md) for the complete key inventory, precedence, validation, migration, and deployment notes.

Create the local Compose file and replace its PostgreSQL password placeholder with a private development value:

```powershell
Copy-Item .env.example .env
notepad .env
```

Its supported keys are `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, and the optional `POSTGRES_PORT` host mapping. ASP.NET Core does not parse this file; do not import it into PowerShell.

```powershell
dotnet user-secrets set --project src/CustomFormService.Api/CustomFormService.Api.csproj "PostgreSql:Password" "<same-password-as-.env>"
dotnet user-secrets set --project src/CustomFormService.Api/CustomFormService.Api.csproj "Jwt:SigningKey" "<at-least-32-random-bytes>"
```

`POSTGRES_PASSWORD` and `PostgreSql:Password` must have the same local value. User Secrets are stored outside the repository and are not encrypted; use development-only values. Do not commit `.env`. If the file still contains pre-split `PostgreSql__*` or `Jwt__*` keys, follow the guide's migration procedure before running Compose.

## Start and inspect PostgreSQL

```powershell
docker compose config --quiet
docker compose up -d
docker compose ps
```

The Compose project has one `postgres` service. It uses `postgres:17.11-alpine`, publishes the configured host port to container port 5432, and stores data in the `custom-form-service-postgres-data` named volume.

If the container is healthy but the host cannot connect, check the actual published port and Windows TCP exclusions; container health alone does not verify the host mapping. See the [Windows port troubleshooting procedure](docs/configuration/environment-configuration.md#windows-postgresql-host-port-troubleshooting).

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

Retain synthetic acceptance users and their fixtures. Use a fresh RunId for another run; do not delete historical evidence or the regular database volume. Google Authentication remains part of the formal V1 contract and is deferred to G1.

## R5 Foundational Form APIs

R5 provides these formal V1 endpoints:

- `POST /api/v1/forms`
- `GET /api/v1/forms`
- `GET /api/v1/forms/{formId}`
- `DELETE /api/v1/forms/{formId}`

Create accepts a Draft Form and an optional nested Question/Option tree. The complete tree is validated as a Draft definition and persisted in one transaction: an invalid nested definition leaves no Form, Question, or Option row behind. Form IDs are server-generated; Create rejects client-provided IDs and other read-only fields.

`GET /api/v1/forms` is a paginated Owner-only list, with `page`, `pageSize`, and optional `status` query parameters. It excludes soft-deleted Forms and sorts by `updatedAt DESC, id DESC`. Form Detail requires an active signed-in user: the Owner can read a Draft, while another member receives `404 FORM_NOT_FOUND` for that Draft. Active members can read Published and Closed Forms.

Delete is Owner-only and performs a soft delete by setting `deleted_at`; children and submitted history remain. A deleted Form is excluded from the list and returns `404 FORM_NOT_FOUND` from Detail, Submit, Latest, Count, Statistics, or a repeated Delete.

## R6–R10 workflow and endpoint index

Use the [R11 HTTP companion](docs/test-plan/R11_Manual_Tests.http) for the complete sequential Email → Cookie Login → Draft → five Question types → Publish → Submit → Latest/Count → Close → Statistics → Logout workflow. Generate new synthetic users and a RunId, keep A/B cookies separate, and bind IDs by Question type and Option value. Anonymous requests carry no saved cookie. Never reuse historical tokens.

| Capability | Endpoint under `/api/v1` |
| --- | --- |
| Email auth | POST `/auth/register`, `/auth/login`, `/auth/logout` |
| Current User | GET/PATCH `/users/me` |
| Form | POST/GET `/forms`; GET/PATCH/DELETE `/forms/{formId}` |
| Lifecycle/policy | POST `/forms/{formId}/publish`, `/close`; PATCH `/forms/{formId}/result-visibility` |
| Submission/queries | POST `/forms/{formId}/responses`; GET `/forms/{formId}/responses/me/latest`, `/respondent-count` |
| Closed Statistics | GET `/forms/{formId}/statistics` |

Draft PATCH applies the whole candidate atomically. Publish freezes the definition; Close stops submissions. `single` permits one successful response per member, while `multiple` retains history. Latest and Statistics select each member's latest complete response without filling omitted optional answers from older responses. Count counts distinct respondents. Closed Statistics enforce owner_only/respondents/public; anonymous omits respondents, identified uses current display names. See [formal contracts](docs/specs/03c-v1-api-contract-submission-statistics.md) for precise shapes and errors.

## Configuration

The canonical reference is [Environment Configuration Design, Operations, and Audit](docs/configuration/environment-configuration.md). In short, ASP.NET Core loads shared JSON defaults, environment-specific JSON, Development User Secrets, OS environment variables, and command-line overrides. Hierarchical environment-variable keys use double underscores.

For Railway Staging or Production, a minimal template is:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:${{ PORT }}
PostgreSql__Host=...
PostgreSql__Port=5432
PostgreSql__Database=...
PostgreSql__Username=...
PostgreSql__Password=...
Jwt__SigningKey=...
Cors__AllowedOrigins__0=https://app.example.com
```

`${{ PORT }}` uses Railway's same-service reference syntax. Environment-specific values override committed defaults, so defaults do not need to be repeated. This is a configuration template only; Railway networking, public domain, reverse proxy, and live HTTPS verification belong to the separate deployment plan.

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

The database contains all seven formal Domain Tables, including `user_identities`, and the unchanged schema-only `InitialV1Schema` migration. Google Authentication is pending in G1. Frontend, Railway, domain and live production HTTPS are pending separate delivery plans. Refresh tokens, email verification, password reset, sessions and RBAC are outside V1. R11 AI results await Owner confirmation after execution; neither A-stage acceptance nor full Backend V1 acceptance is implied.
