# Local verification

Verified on 2026-09-17 with .NET SDK 10.0.401 on Windows.

## Passed

- Solution restore completed with all projects up to date.
- Release build completed with zero warnings and zero errors.
- Normal test suite: 59 passed, zero failed, one optional SQL Server test skipped because `CLAIMS_TEST_SQLSERVER` was not set.
- Tests include domain decision rules; approval and structured denials; duplicate decisions; immutable reprocessing; full claim-line retrieval; second/empty pagination pages; consistent validation responses; OpenAPI required fields and error responses; and fixed-length plus chunked oversized bodies through a real Kestrel TCP listener.
- EF Core reports no pending model changes.
- Docker Compose configuration validation passed.
- The existing Docker files and GitHub Actions SQL Server service remain intact.
- SQL Server Express `MSSQL$SQLEXPRESS` is installed and running on this machine. SQL Server LocalDB is not installed.
- Before the processing changes, the existing real SQL Server migration/repository test completed successfully against `.\\SQLEXPRESS`.
- User-confirmed Windows/Visual Studio smoke test after the processing changes: F5 startup connected to SQL Server Express, applied Development initialization, restored the synthetic seed records, submitted claim `8da0dade-c8a3-40a1-b600-9d9c3bc02f97`, and processed it to `Approved` with persisted validation/processing timestamps and no denial reason.

## Environment limitation

The current managed execution environment cannot complete Windows SSPI/TLS negotiation with the local SQL Server Express instance. Both the API Development startup and the expanded SQL Server test fail before authentication with an environment-level encryption/SSPI error. The installed command-line SQL client fails in the same way. This prevents final live API verification through the Windows-native database path from this environment.

The failure is not hidden or converted into a passing result. The Windows profile is configured for the installed `.\\SQLEXPRESS` instance with Windows authentication and Development-only migration/seeding. It should be run once from the user's normal Visual Studio/F5 context, which owns the interactive Windows credentials. The expanded SQL Server test still needs a clean post-change run there or in GitHub Actions; it now covers real migrations and concurrent duplicate processing.

Docker container startup was not rerun in this phase. Compose syntax passed, and Docker remains optional.

## Commands

```powershell
dotnet restore ClaimsProcessing.sln
dotnet build ClaimsProcessing.sln -c Release --no-restore
dotnet test ClaimsProcessing.sln -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/Claims.Infrastructure --startup-project src/Claims.Api --configuration Release --no-build
```

Windows SQL Server verification command:

```powershell
$env:CLAIMS_TEST_SQLSERVER = 'Server=.\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test tests/Claims.IntegrationTests/Claims.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SqlServerTests
```
