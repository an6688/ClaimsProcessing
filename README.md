# Synthetic Claims Processing

A compact portfolio backend built with C#, ASP.NET Core 10, EF Core 10, SQL Server, xUnit, Docker Compose and GitHub Actions. It uses synthetic data only and does not represent production healthcare adjudication.

## Quick Start on Windows

This machine has SQL Server Express installed as `.\\SQLEXPRESS`. LocalDB is not installed, so the checked-in Development profile uses SQL Server Express with Windows authentication.

Prerequisites:

- Visual Studio 2026 or the .NET 10 SDK
- A running SQL Server Express instance named `SQLEXPRESS`

Open `ClaimsProcessing.sln`, select `Claims.Api`, and press F5. The Development profile:

1. connects to `.\\SQLEXPRESS` using Windows authentication;
2. applies committed EF Core migrations;
3. adds repeatable synthetic seed data;
4. starts the API at `http://localhost:5080`; and
5. opens `/swagger`.

Automatic migration and seeding run only in the Development environment when `Database:Initialize` is true. The repeatable seeder restores the documented values of its three known synthetic members and providers while preserving claims and unrelated data. Production configuration does not migrate or seed at application startup.

If LocalDB is installed on another Windows machine, override the connection string with:

```powershell
$env:ConnectionStrings__Claims = 'Server=(localdb)\MSSQLLocalDB;Database=ClaimsPortfolio;Integrated Security=True;Encrypt=False'
dotnet run --project src/Claims.Api
```

If the SQL Server Express instance name differs, update the Development connection string or provide the same environment variable.

## Docker/containerized startup

Docker remains an optional path. Copy the example environment file, replace its password, then start both SQL Server and the API:

```powershell
Copy-Item .env.example .env
docker compose up --build -d
docker compose logs -f api
```

Swagger is at `http://localhost:8080/swagger`. Compose binds the API and database ports to loopback. The SQL health check gates API startup, and Development startup applies migrations and seed data.

Stop with `docker compose down`. Data remains in the `claims-sql` volume. `docker compose down -v` deliberately removes that local demo data.

## Domain and processing rules

A claim belongs to one member and provider and contains 1–100 service lines. Submission validates IDs, dates, procedure codes, quantities and prices. Submission rejects malformed claims; processing evaluates valid stored claims.

`POST /api/claims/{id}/process` transitions a Submitted claim through Validated to Approved or Denied. A decision is immutable: repeating the processing request returns the stored decision.

A claim is denied when:

- the member or provider is unavailable during processing;
- the provider is inactive;
- the member is inactive or any line's service date lies outside the inclusive coverage interval;
- a stored line is invalid or has a future date; or
- an already processed claim for the same member and provider contains a line with the same service date and case-insensitive procedure code.

That duplicate rule is deliberately simplified for this portfolio. It is not a representation of real healthcare claims adjudication. A matching line denies the entire new claim. Both Approved and Denied claims count as processed for future duplicate checks. The response includes a structured `denialReason` with a code, message and, for duplicates, the original claim ID.

Input validation normally prevents future dates, empty claims, invalid codes and nonpositive amounts from being stored. Processing defensively checks those invariants again for legacy or direct database writes.

SQL Server processing serializes decisions per member before duplicate detection. This prevents two matching claims processed concurrently by different API instances from both being approved.

## API

- `POST /api/claims` — submit a synthetic claim; returns 201.
- `POST /api/claims/{id}/process` — make or retrieve its decision; returns 200.
- `GET /api/claims/{id}` — retrieve lines and decision details.
- `GET /api/members/{id}/claims?page=1&pageSize=20` — member claims.
- `GET /api/claims?status=Denied&page=1&pageSize=20` — optional status filter.
- `GET /api/health` — database readiness; returns 200 or 503.

The request body limit for submission is 128 KiB; larger fixed-length and chunked requests return 413 Problem Details. All 400 validation responses use Validation Problem Details with an `errors` dictionary and trace ID. Unexpected failures return a sanitized 500 response.

Development seed IDs end in `000000000001`, `000000000002` and `000000000003`:

- Member 1 and Provider 1 are active with open-ended coverage.
- Member 2 has coverage ending 2024-12-31.
- Member 3 is inactive.
- Provider 3 is inactive.

Example:

```json
{
  "memberId": "10000000-0000-0000-0000-000000000001",
  "providerId": "20000000-0000-0000-0000-000000000001",
  "lines": [
    {
      "procedureCode": "SYN-EXAM",
      "serviceDate": "2025-01-15",
      "quantity": 2,
      "unitPrice": 75.25
    }
  ]
}
```

## Architecture

- `Claims.Domain`: entities, invariants, status transitions and structured denial concepts.
- `Claims.Application`: API contracts, orchestration and the use-case-specific repository contract.
- `Claims.Infrastructure`: EF Core mappings, SQL Server persistence, migrations and development seeding.
- `Claims.Api`: controllers, dependency injection, OpenAPI and centralized error handling.
- `Claims.UnitTests`: domain invariants and processing decisions.
- `Claims.IntegrationTests`: HTTP behavior through ASP.NET Core, real Kestrel request-limit tests, and an optional SQL Server migration/concurrency test.

Dependencies point inward. The design intentionally omits CQRS, MediatR, message buses and distributed infrastructure.

## Running tests

```powershell
dotnet restore ClaimsProcessing.sln
dotnet build ClaimsProcessing.sln -c Release --no-restore
dotnet test ClaimsProcessing.sln -c Release --no-build
```

The normal suite uses isolated relational SQLite databases for fast HTTP tests. The body-limit regression starts a real Kestrel TCP listener rather than using only the in-memory test server.

Run the SQL Server migration/repository/concurrent-processing test against a disposable local server:

```powershell
$env:CLAIMS_TEST_SQLSERVER = 'Server=.\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test tests/Claims.IntegrationTests/Claims.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SqlServerTests
```

The test creates a uniquely named database, applies real migrations, verifies no model drift, exercises concurrent duplicate decisions, and deletes only that database. GitHub Actions supplies an ephemeral SQL Server service and runs this test on pushes and pull requests.

## Database configuration

Configuration precedence follows ASP.NET Core conventions. The checked-in base settings contain no credentials. Development defaults live in `src/Claims.Api/appsettings.Development.json`; environment variables can override them:

```powershell
$env:ConnectionStrings__Claims = 'Server=YOUR_SERVER;Database=ClaimsPortfolio;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:Database__Initialize = 'true'
```

Committed migrations are under `src/Claims.Infrastructure/Migrations`. Common commands:

```powershell
dotnet tool restore
dotnet ef migrations list --project src/Claims.Infrastructure --startup-project src/Claims.Api
dotnet ef migrations has-pending-model-changes --project src/Claims.Infrastructure --startup-project src/Claims.Api
dotnet ef migrations script --idempotent --project src/Claims.Infrastructure --startup-project src/Claims.Api --output migration.sql
```

For production, review and apply migration scripts separately with an appropriately privileged identity. Do not grant schema modification rights to the normal runtime identity.

## Security and scope

This local demonstration has no authentication, member-level authorization, rate limiting, audit trail or transport-level retry key. Use synthetic data only. No HIPAA compliance claim is made.

EF Core parameterizes queries; the API does not log request bodies; exception responses omit stack traces; Swagger and automatic migrations are Development-only. A real deployment would require authentication and authorization, managed secrets, validated TLS, least-privilege database credentials, auditing, monitoring, retention policy and threat modeling.
