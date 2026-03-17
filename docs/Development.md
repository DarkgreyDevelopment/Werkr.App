# Outdated
This document is outdated and needs to be revised.

# Development

This guide covers how to build, run, test, and contribute to the Werkr project. For architectural context, see [Architecture.md](Architecture.md).

---

## Prerequisites

| Requirement | Details |
|-------------|---------|
| **.NET 10 SDK** | See `global.json` for the exact version (`10.0.100` with `latestFeature` roll-forward). |
| **Docker** | Required for running PostgreSQL locally (via Aspire) and for integration tests (Testcontainers). |
| **PostgreSQL 17** | Provided automatically by the Aspire AppHost or Docker Compose. No manual install needed if you have Docker. |
| **PowerShell 7+** | The Agent embeds a PowerShell host — the SDK is useful for running project scripts. |
| **Git** | Conventional commits are used for versioning via GitVersion. |

---

## Repository Structure

```
Werkr_Complete/
├── .config/             # .NET local tools (GitVersion)
├── .github/workflows/   # CI pipeline (ci.yml)
├── docs/                # User-facing documentation, DocFX config, images
├── scripts/             # Build and publish scripts
├── src/
│   ├── Werkr.Agent/                 # Task execution worker
│   ├── Werkr.Api/                   # Application API
│   ├── Werkr.AppHost/               # .NET Aspire orchestrator
│   ├── Werkr.Common/                # Shared models, protos, auth
│   ├── Werkr.Common.Configuration/  # Shared config classes
│   ├── Werkr.Core/                  # Business logic (scheduling, workflows, crypto)
│   ├── Werkr.Data/                  # EF Core contexts + entities
│   ├── Werkr.Data.Identity/         # ASP.NET Identity EF Core contexts
│   ├── Werkr.Server/                # Blazor Server UI + Identity
│   ├── Werkr.ServiceDefaults/       # Aspire service defaults
│   ├── Installer/Msi/               # WiX MSI projects + custom actions
│   └── Test/                        # Test projects
├── Directory.Build.props       # Shared build properties (net10.0, nullable, etc.)
├── Directory.Packages.props    # Central package management
├── GitVersion.yml              # Versioning configuration
├── global.json                 # SDK version pinning
├── docker-compose.yml          # Docker Compose for local development
└── Werkr.slnx                  # Solution file
```

See [Architecture.md](Architecture.md) for project roles and the communication model.

---

## Building

Build the entire solution:

```shell
dotnet build Werkr.slnx
```

> **Note:** The WiX installer projects (`src/Installer/Msi/`) require the WiX Toolset and only build on Windows. They are excluded from the default build on other platforms. If WiX is not installed, you can skip them with `dotnet build Werkr.slnx --no-restore /p:ExcludeWixProjects=true` or simply ignore the warning.

### Central Package Management

All NuGet package versions are managed centrally in `Directory.Packages.props`. Individual project files reference packages without specifying versions. To add or update a dependency, edit `Directory.Packages.props`.

### Build Properties

`Directory.Build.props` applies to all projects:
- Target framework: `net10.0`
- Nullable reference types: enabled
- Implicit usings: enabled
- XML documentation generation: enabled
- Warnings as errors: enabled
- Deterministic builds with embedded debug symbols
- Locked-mode package restore (`RestorePackagesWithLockFile`)

---

## Running Locally

The easiest way to run all components locally is with the .NET Aspire AppHost:

```shell
dotnet run --project src/Werkr.AppHost
```

This starts PostgreSQL (in a Docker container), creates two databases (`werkrdb` and `werkridentitydb`), and launches the API, Agent, and Server with proper service discovery. The Aspire dashboard opens automatically in your browser.

See `src/Werkr.AppHost/AppHost.cs` for the orchestration configuration.

### Docker Compose

Alternatively, you can use `docker-compose.yml` at the repository root to run the full stack in containers. See `scripts/docker-build.ps1` for the Docker build workflow.

---

## Testing

The project has four test projects under `src/Test/`:

| Project | Scope |
|---------|-------|
| `Werkr.Tests` | Integration tests — spins up the full API with a Testcontainers PostgreSQL instance using `AppHostFixture`. Tests schedules, workflows, actions, and holiday calendars end-to-end. |
| `Werkr.Tests.Data` | Unit tests for data layer logic, entity validation, and EF Core query behavior. |
| `Werkr.Tests.Server` | Integration tests for the Server (Blazor UI) endpoints and identity flows. |
| `Werkr.Tests.Agent` | End-to-end tests for the Agent's task execution pipeline. |

### Running Tests

Run all tests:

```shell
dotnet test Werkr.slnx
```

Run a specific test project:

```shell
dotnet test --project src/Test/Werkr.Tests/Werkr.Tests.csproj
```

### Test Infrastructure

The `Werkr.Tests` project uses an `AppHostFixture` pattern:
1. Starts a disposable PostgreSQL container via **Testcontainers**
2. Creates an in-process API server via `WebApplicationFactory<Werkr.Api.Program>`
3. Runs EF Core migrations and seeds identity roles/permissions
4. Generates a JWT admin token for authenticated API calls

Tests use **MSTest** with the `Microsoft.Testing.Platform` runner (configured in `global.json`).

### CI

The GitHub Actions CI pipeline (`.github/workflows/ci.yml`) runs on `ubuntu-latest`:
1. Restores with `--locked-mode` to ensure `packages.lock.json` files are current
2. Builds in Release configuration with GitVersion-derived version numbers
3. Runs all tests and uploads `.trx` result files as artifacts

See [Testing.md](articles/Testing.md) for more detail.

---

## Database Migrations

EF Core migrations are split by database provider.

| Context | Provider | Migration directory |
|---------|----------|-------------------|
| `PostgresWerkrDbContext` | PostgreSQL | `src/Werkr.Data/Migrations/Postgres/` |
| `SqliteWerkrDbContext` | SQLite | `src/Werkr.Data/Migrations/Sqlite/` |
| `PostgresWerkrIdentityDbContext` | PostgreSQL | `src/Werkr.Data.Identity/Migrations/Postgres/` |
| `SqliteWerkrIdentityDbContext` | SQLite | `src/Werkr.Data.Identity/Migrations/Sqlite/` |

VS Code tasks are available for generating new migrations — check `.vscode/tasks.json` for the `ef:migrations:postgres`, `ef:migrations:sqlite`, and `ef:migrations:identity` tasks.

---

## DocFX Documentation

The project website ([docs.werkr.app](https://docs.werkr.app)) is generated with DocFX from `docs/docfx/`.

To build the documentation locally:

```shell
# Install DocFX (if not already installed)
dotnet tool install -g docfx

# Generate API metadata
docfx metadata docs/docfx/docfx.json

# Build the site
docfx build docs/docfx/docfx.json

# Serve locally for preview
docfx serve docs/docfx/_site
```

See [How To: Local Doc Development](articles/HowTo/LocalDocDev.md) for a more detailed walkthrough.

---

## Coding Conventions

- **Formatting** — Defined in `.editorconfig`. Run `dotnet format Werkr.slnx` to auto-format.
- **Nullable reference types** — Enabled project-wide. All new code should handle nullability correctly.
- **Warnings as errors** — All compiler warnings are treated as errors. Fix warnings before committing.
- **XML documentation** — Required for all public types and members (`GenerateDocumentationFile` is enabled).
- **Conventional commits** — Use [Conventional Commits](https://www.conventionalcommits.org/) for commit messages. GitVersion derives version numbers from commit history: `feat:` = minor bump, `fix:` = patch bump, breaking changes = major bump.
- **Package lock files** — `RestorePackagesWithLockFile` is enabled. Run `dotnet restore` to update `packages.lock.json` when dependencies change. CI restores with `--locked-mode`.

---

## Contribution Workflow

1. **Fork** the repository and create a feature branch from `develop`.
2. Make your changes following the coding conventions above.
3. Run `dotnet format Werkr.slnx` and `dotnet test Werkr.slnx` before pushing.
4. Submit a **pull request** targeting `develop`.
5. All tests must pass in CI before the PR can be merged.
6. You will need to agree to the [Contribution License Agreement](ContributionLicenseAgreement.md) before your PR is merged.

For feedback, feature requests, bug reports, and documentation improvements, please open a [GitHub issue](https://github.com/DarkgreyDevelopment/Werkr.App/issues/new/choose).
