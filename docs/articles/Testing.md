# Testing

Werkr uses [MSTest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-intro) with the `Microsoft.Testing.Platform` runner (configured in `global.json`). The TypeScript graph-ui uses [Vitest](https://vitest.dev/). Tests run automatically in CI via GitHub Actions and must all pass before a pull request can be merged.

---

## Prerequisites

| Requirement | Details |
|-------------|---------|
| **.NET 10 SDK** | Required for all .NET test projects. Pinned in `global.json`. |
| **Docker** | Required for `Werkr.Tests` integration tests — Testcontainers spins up PostgreSQL 17 Alpine. Docker Desktop or Docker Engine must be running. |
| **Node.js 22+** | Required for graph-ui TypeScript tests. See the `engines` field in `src/Werkr.Server/graph-ui/package.json`. |
| **npm** | Comes with Node.js. Used for `npm ci` (dependency install) and `npm test` (Vitest runner). |
| **PowerShell 7+** | Required for `Werkr.Tests.Agent` tests that exercise the embedded PowerShell host. |

---

## Test Projects

| Project | Scope | Key Patterns |
|---------|-------|--------------|
| `src/Test/Werkr.Tests/` | API integration tests. Covers schedules, workflows, actions, holiday calendars, and action dispatch end-to-end. | `AppHostFixture`, `WebApplicationFactory`, Testcontainers (PostgreSQL 17 Alpine) |
| `src/Test/Werkr.Tests.Data/` | Unit tests for data layer — entity validation, EF Core query behavior, collection utilities, range types, cryptography, scheduling logic, workflow services, registration. | In-memory EF Core provider, no external dependencies |
| `src/Test/Werkr.Tests.Server/` | Blazor component tests and Server integration tests — identity flows (seeding, JWT, cookies, API keys, permissions, user management), authorization, page rendering, action parameter editors. | bunit (`BunitContext` base class), in-memory identity stores |
| `src/Test/Werkr.Tests.Agent/` | Agent tests — action handlers (27+ handlers), operator execution (PowerShell, shell), output streaming, scheduling, security (path allowlist, URL validation). | Test doubles (`SuccessHandler`, `FailHandler`, `SlowHandler`), mock gRPC contexts |
| `src/Werkr.Server/graph-ui/` | TypeScript frontend tests — DAG changeset logic, cycle detection, draft storage, clipboard handling, timeline styles, timeline item construction. | Vitest, direct module imports (no browser DOM) |

---

## AppHostFixture Pattern

The `Werkr.Tests` project uses a shared `AppHostFixture` (assembly-level setup/teardown via `[AssemblyInitialize]` / `[AssemblyCleanup]`) that:

1. **Starts a disposable PostgreSQL container** via [Testcontainers](https://dotnet.testcontainers.org/) (`postgres:17-alpine`).
2. **Creates an in-process API server** via `WebApplicationFactory<Werkr.Api.Program>`, replacing the database registrations with the Testcontainer's connection string via `ConfigureServices`.
3. **Runs EF Core migrations** for both the application database (`WerkrDbContext`) and the identity database (`WerkrIdentityDbContext`).
4. **Seeds identity roles and permissions** — replicates the minimal role/permission seed so permission-based auth resolves correctly in tests.
5. **Generates an authenticated HTTP client** with a JWT admin token for making authorized API calls.

All integration test classes in `Werkr.Tests` use `AppHostFixture.ApiClient` and `AppHostFixture.JsonOptions` to interact with the API.

Test parallelization is disabled (`[assembly: DoNotParallelize]` in `AssemblyAttributes.cs`) because all tests share a single Testcontainer database instance.

See `src/Test/Werkr.Tests/AppHostFixture.cs` for the implementation.

---

## Blazor Component Testing (bunit)

`Werkr.Tests.Server` uses [bunit](https://bunit.dev/) for Blazor component testing. Test classes extend `BunitContext` (from bunit for MSTest), rendering components in isolation with mock services registered in the test context.

Current bunit test classes:
- `ActionParameterEditorTests`
- `ConditionBuilderTests`
- `IntArrayEditorTests`
- `KeyValueMapEditorTests`
- `ObjectArrayEditorTests`
- `StringArrayEditorTests`
- `TaskSetupModalTests`

The same project also contains non-bunit tests for identity services, authorization, and page-level logic that use standard MSTest patterns without bunit rendering.

---

## Graph-UI TypeScript Tests (Vitest)

The graph-ui TypeScript frontend has its own test suite using Vitest 3.x.

- **Location:** `src/Werkr.Server/graph-ui/`
- **Test files:** `test/` directory, pattern `test/**/*.test.ts`
- **Configuration:** `vitest.config.ts` at the graph-ui root
- **Coverage:** V8 provider with 90% line threshold on `changeset.ts`, `cycle-detection.ts`, `draft-storage.ts`
- **Dependencies:** `@antv/x6` (DAG rendering), `dagre` (graph layout), `vis-data` + `vis-timeline` (timeline/Gantt rendering)

Current test suites:
- `test/dag/changeset.test.ts`
- `test/dag/clipboard-handler.test.ts`
- `test/dag/cycle-detection.test.ts`
- `test/dag/draft-storage.test.ts`
- `test/smoke.test.ts`
- `test/timeline/timeline-items.test.ts`
- `test/timeline/timeline-styles.test.ts`

### Running graph-ui tests locally

```shell
cd src/Werkr.Server/graph-ui
npm ci          # Install dependencies (first time or after package-lock changes)
npm test        # Run tests once (CI mode)
npx vitest      # Run in watch mode (development)
```

Bundle size checks run in CI via `scripts/check-bundle-size.mjs` after the production build.

---

## Running Tests Locally

### All .NET tests

```shell
dotnet test Werkr.slnx
```

> **Prerequisite:** Docker must be running for `Werkr.Tests` integration tests (Testcontainers).

### Specific .NET test project

```shell
dotnet test --project src/Test/Werkr.Tests/Werkr.Tests.csproj
```

### Graph-UI tests

```shell
npm test --prefix src/Werkr.Server/graph-ui
```

> **Prerequisite:** Run `npm ci --prefix src/Werkr.Server/graph-ui` first to install dependencies.

### Graph-UI watch mode

```shell
cd src/Werkr.Server/graph-ui && npx vitest
```

### VS Code Tasks

VS Code tasks are available in `.vscode/tasks.json`:

| Task Label | Test Project |
|------------|-------------|
| `verify:test-unit` | `Werkr.Tests.Data` (data layer unit tests) |
| `verify:test-integration` | `Werkr.Tests.Server` (Server integration tests) |
| `verify:test-server` | `Werkr.Tests.Server` (Server tests) |
| `verify:test-api` | `Werkr.Tests` (API integration tests, requires Docker) |
| `verify:test-e2e` | `Werkr.Tests.Agent` (Agent e2e tests) |
| `verify:test-e2e-verbose` | `Werkr.Tests.Agent` (verbose output) |
| `verify:test-e2e-failures` | `Werkr.Tests.Agent` (failures only) |
| `verify:test-graphui` | graph-ui TypeScript tests (Vitest) |

---

## CI Pipeline

The GitHub Actions pipeline (`.github/workflows/ci.yml`) runs on every push and PR to `main` and `develop`. Runs on `ubuntu-latest` with concurrency per-ref (`cancel-in-progress: true`).

### Steps

1. **Checkout** — Full history clone (`fetch-depth: 0`) for GitVersion.
2. **Setup .NET 10** — Installs .NET 10 SDK.
3. **Restore tools** — `dotnet tool restore` (GitVersion, etc.).
4. **Determine version** — Runs GitVersion to derive `SemVer`, `AssemblySemVer`, `AssemblySemFileVer`, `InformationalVersion`.
5. **Setup Node.js 22** — Installs Node.js 22 with npm cache keyed on `graph-ui/package-lock.json`.
6. **Install graph-ui dependencies** — `npm ci --prefix src/Werkr.Server/graph-ui`.
7. **Run JS tests** — `npm test --prefix src/Werkr.Server/graph-ui` (Vitest).
8. **Build JS bundles** — `npm run build:prod --prefix src/Werkr.Server/graph-ui` (production esbuild).
9. **Check bundle sizes** — `node src/Werkr.Server/graph-ui/scripts/check-bundle-size.mjs`.
10. **Restore .NET dependencies** — `dotnet restore Werkr.slnx --force-evaluate` with lock file validation via `Test-LockFileChanges.ps1` (skips Windows-only Installer projects).
11. **Build** — `dotnet build Werkr.slnx -c Release` with GitVersion-derived version properties.
12. **Test** — `dotnet test --solution Werkr.slnx -c Release --no-build` with TRX logger.
13. **Upload test results** — `.trx` files uploaded as build artifacts (runs even on failure via `if: always()`).

---

## Test Infrastructure Details

| Technology | Used By | Purpose |
|------------|---------|---------|
| **MSTest 4.x** | All .NET test projects | Test framework, configured with `Microsoft.Testing.Platform` runner in `global.json` |
| **Testcontainers** | `Werkr.Tests` | Disposable PostgreSQL 17 Alpine instances for integration tests; container lifecycle managed by `AppHostFixture` |
| **bunit** | `Werkr.Tests.Server` | Blazor component rendering tests; test classes extend `BunitContext` |
| **Vitest 3.x** | `graph-ui` | TypeScript unit tests with V8 coverage provider and line thresholds |
| **In-memory EF Core** | `Werkr.Tests.Data` | Fast unit tests with no database dependency |

### Test Parallelization

- `Werkr.Tests` disables parallelization (`[assembly: DoNotParallelize]`) because all tests share a single Testcontainer database instance.
- Other .NET test projects run in parallel by default.
- graph-ui Vitest tests run in parallel by default.
