# Testing

Werkr uses [MSTest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-intro) with the `Microsoft.Testing.Platform` runner (configured in `global.json`). Tests run automatically in CI via GitHub Actions and must all pass before a pull request can be merged.

---

## Test Projects

| Project | Scope | Key Patterns |
|---------|-------|--------------|
| `src/Test/Werkr.Tests/` | Integration tests for the API layer. Covers schedules, workflows, actions, holiday calendars, and action dispatch end-to-end. | `AppHostFixture`, `WebApplicationFactory`, Testcontainers |
| `src/Test/Werkr.Tests.Data/` | Unit tests for the data layer — entity validation, EF Core query behavior, collection and range utilities. | In-memory or SQLite provider |
| `src/Test/Werkr.Tests.Server/` | Integration tests for the Server (Blazor UI) endpoints and identity flows. | Server-specific test harness |
| `src/Test/Werkr.Tests.Agent/` | End-to-end tests for the Agent's task execution pipeline — action handlers, shell execution, PowerShell host. | Agent-specific test harness |

---

## AppHostFixture Pattern

The `Werkr.Tests` project uses a shared `AppHostFixture` (assembly-level setup/teardown) that:

1. **Starts a disposable PostgreSQL container** via [Testcontainers](https://dotnet.testcontainers.org/) (`postgres:17-alpine`).
2. **Creates an in-process API server** via `WebApplicationFactory<Werkr.Api.Program>`, replacing the database registrations with the Testcontainer's connection string.
3. **Runs EF Core migrations** for both the application database (`WerkrDbContext`) and the identity database (`WerkrIdentityDbContext`).
4. **Seeds identity roles and permissions** — replicates the minimal role/permission seed so permission-based auth resolves correctly in tests.
5. **Generates an authenticated HTTP client** with a JWT admin token for making authorized API calls.

All integration test classes in `Werkr.Tests` use `AppHostFixture.ApiClient` and `AppHostFixture.JsonOptions` to interact with the API.

---

## Running Tests Locally

Run all tests:

```shell
dotnet test Werkr.slnx
```

Run a specific test project:

```shell
dotnet test --project src/Test/Werkr.Tests/Werkr.Tests.csproj
```

> **Prerequisite:** Docker must be running for integration tests that use Testcontainers.

VS Code tasks are also available — check `.vscode/tasks.json` for `verify:test-unit`, `verify:test-integration`, `verify:test-e2e`, and `verify:test-shared`.

---

## CI Pipeline

The GitHub Actions pipeline (`.github/workflows/ci.yml`) runs on every push and PR to `main` and `develop`:

1. **Setup** — Checks out the repository with full history (for GitVersion), installs .NET 10 SDK.
2. **Version** — Runs GitVersion to determine the semantic version from commit history.
3. **Restore** — `dotnet restore Werkr.slnx --locked-mode` (ensures `packages.lock.json` files are current).
4. **Build** — `dotnet build` in Release configuration with GitVersion-derived version properties.
5. **Test** — `dotnet test` with TRX logger output.
6. **Upload** — Test result `.trx` files are uploaded as build artifacts.

The CI runs on `ubuntu-latest`. Concurrency is managed per-ref with `cancel-in-progress: true`.
