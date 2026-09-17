# Claims Processing

A production-style portfolio app for submitting and adjudicating synthetic healthcare claims. It pairs a Blazor operations portal with an ASP.NET Core API, explicit business rules, SQL Server persistence, and end-to-end tests.

> Synthetic data only. This project demonstrates software design; it does not model real healthcare adjudication or claim compliance.

## What it demonstrates

- A complete `Submitted → Validated → Approved / Denied` workflow
- Structured denial decisions for eligibility, provider, date, line, and duplicate checks
- A Blazor dashboard, claim form, claim detail view, and Swagger/OpenAPI contract
- EF Core migrations, repeatable development seed data, and SQL Server concurrency handling
- Unit and integration coverage, including real Kestrel request-limit behavior
- Windows-native development, optional Docker Compose, and GitHub Actions CI

## Try the workflow

Run the app, open `http://localhost:5080`, and:

1. Submit a claim with one or more service lines.
2. Open the claim and process it.
3. Inspect the approval or structured denial decision.

The demo includes active, expired, and inactive member/provider records so both approval and denial paths are easy to exercise. Swagger is available at `http://localhost:5080/swagger`.

Duplicate detection is deliberately simple: a processed claim matches when member, provider, service date, and procedure code match. This portfolio rule is not a representation of real claim adjudication.

## Architecture

The solution keeps a focused four-layer structure:

- `Claims.Domain` — entities, invariants, status transitions, and denial concepts
- `Claims.Application` — contracts and use-case orchestration
- `Claims.Infrastructure` — EF Core, SQL Server, migrations, and seed data
- `Claims.Api` — REST endpoints, Blazor UI, OpenAPI, and error handling

Dependencies point inward. The project intentionally avoids CQRS, MediatR, message buses, and distributed infrastructure.

## Quick Start on Windows

Prerequisites: .NET 10 and SQL Server Express at `.\SQLEXPRESS`.

Open `ClaimsProcessing.sln`, select `Claims.Api`, and press F5. In Development, the app applies committed migrations, restores the documented synthetic seed records, starts at `http://localhost:5080`, and opens the portal.

For another SQL Server instance, override the connection string:

```powershell
$env:ConnectionStrings__Claims = 'Server=YOUR_SERVER;Database=ClaimsPortfolio;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project src/Claims.Api
```

Automatic migration and seeding are Development-only and controlled by `Database:Initialize`.

## API

- `POST /api/claims` — submit a claim
- `POST /api/claims/{id}/process` — adjudicate a submitted claim
- `GET /api/claims/{id}` — retrieve claim lines and decision details
- `GET /api/claims?status=Denied&page=1&pageSize=20` — filter and page claims
- `GET /api/members/{id}/claims?page=1&pageSize=20` — list a member's claims
- `GET /api/health` — database readiness

Validation uses Problem Details consistently. The claim request limit is 128 KiB; oversized fixed-length and chunked requests return HTTP 413.

## Running tests

```powershell
dotnet restore ClaimsProcessing.sln
dotnet build ClaimsProcessing.sln -c Release --no-restore
dotnet test ClaimsProcessing.sln -c Release --no-build
```

The normal suite uses isolated SQLite databases. Set `CLAIMS_TEST_SQLSERVER` to run the optional SQL Server migration and concurrency test against a disposable database.

## Docker/containerized startup

```powershell
Copy-Item .env.example .env
# Replace the example password in .env
docker compose up --build -d
```

The portal runs at `http://localhost:8080`; Swagger is at `/swagger`. Stop with `docker compose down`. Add `-v` only when you intend to remove the demo database volume.

## Database configuration

The base configuration contains no credentials. Development defaults live in `src/Claims.Api/appsettings.Development.json`, and standard ASP.NET Core environment variables override them:

```powershell
$env:ConnectionStrings__Claims = 'Server=(localdb)\MSSQLLocalDB;Database=ClaimsPortfolio;Integrated Security=True;Encrypt=False'
$env:Database__Initialize = 'true'
```

Production deployments should apply reviewed migration scripts separately with a least-privilege identity.

## Scope

This local demo has no authentication, authorization, audit trail, or rate limiting. Use synthetic data only; no HIPAA compliance claim is made.
