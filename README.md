# Custom Form Service

This repository contains the ASP.NET Core backend for the V1 custom form service. The current implementation is the R1 application skeleton only: it exposes engineering endpoints for health and development API documentation, without database or business API behavior.

## Prerequisites

- .NET SDK 10.0.400 or a compatible patch selected by `global.json`
- A trusted ASP.NET Core development certificate for local HTTPS

Docker and PostgreSQL are not used in R1.

## Technology baseline

| Component | Version |
| --- | --- |
| Target framework | `net10.0` |
| .NET SDK | `10.0.400`, with `latestPatch` roll-forward |
| Microsoft.AspNetCore.OpenApi | `10.0.11` |
| Swashbuckle.AspNetCore | `10.2.3` |
| MSTest.Sdk | `4.0.2` |
| Microsoft.AspNetCore.Mvc.Testing | `10.0.11` |

Trust the local development certificate once, if needed:

```powershell
dotnet dev-certs https --trust
```

## Restore, build, and test

Run these commands from the repository root:

```powershell
dotnet restore CustomFormService.sln
dotnet build CustomFormService.sln --no-restore
dotnet test CustomFormService.sln --no-build
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
- OpenAPI JSON: `https://localhost:5001/openapi/v1.json`
- Swagger UI: `https://localhost:5001/swagger/index.html`

OpenAPI JSON and Swagger UI are available only when `ASPNETCORE_ENVIRONMENT=Development`. `/health` is an engineering endpoint and is not one of the 18 V1 business API endpoints under `/api/v1`.

## Configuration

`src/CustomFormService.Api/appsettings.Development.example.json` is a secret-free example for the standard local ports. `Properties/launchSettings.json` applies those ports for `dotnet run --launch-profile https`.

Future passwords, signing keys, connection strings, tokens, and provider credentials must be supplied through environment variables or a secret store and must not be committed.

## R1 boundaries

R1 intentionally contains no PostgreSQL connection, DbContext, Entity, Migration, Domain Table, Authentication, or V1 business endpoint. Those belong to later explicitly authorized rounds.
