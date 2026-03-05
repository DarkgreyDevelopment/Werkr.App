# Werkr — Open Source Task Automation & Workflow Orchestration

<a href="https://docs.werkr.app"><img src="https://docs.werkr.app/images/WerkrLogoWithText.png" alt="Werkr Logo & Text" width="300" height="360"></a>

Werkr is a task automation and workflow orchestration platform built on .NET 10. You can schedule individual tasks, chain them together into directed acyclic graph (DAG) workflows, and let Werkr handle the execution across your infrastructure.

The project has three core components — a **Server** (Blazor UI + Identity), an **API** (application data and gRPC services), and an **Agent** (task execution worker). Server-to-API and user-facing connections use HTTPS; API-to-Agent communication uses encrypted gRPC with AES-256-GCM envelope encryption.

Currently supported on **Windows 10+** and **Linux** (x64 and arm64). macOS support is planned.

<br/>

# Task Management

You can predefine tasks to run on a schedule, create ad-hoc tasks to run immediately, set start and end times, or combine tasks into workflow DAGs for more complex automation. Workflows support dependency-based execution, branching logic, and condition evaluation.

Visit [docs.werkr.app](https://docs.werkr.app) to explore the full documentation.

<br/>

# Downloads

- [Werkr Releases](https://github.com/DarkgreyDevelopment/Werkr.App/releases/latest)

Both Server and Agent are offered as MSI installers (Windows) and portable editions. Once installed, there is no difference between the portable and installed versions.

For Windows, download the latest MSI installer for your CPU architecture (most likely x64).

<br/><br/>

# Documentation and Support

- [Architecture Overview](docs/Architecture.md)
- [Developer Guide](docs/Development.md)
- [How-To Articles](https://docs.werkr.app/articles/HowTo/index.html)
- [Project Features](docs/articles/FeatureList.md)
- [API Documentation](https://docs.werkr.app/api/index.html)
- [Testing](docs/articles/Testing.md)
- [Contributors Guide](#contributing)

<br/><br/>

# Features

## Workflow-Centric Design

Werkr operates primarily on a workflow (DAG) model. You create tasks, link them together as workflow steps with dependency declarations, and Werkr handles topological ordering and execution. The `ConditionEvaluator` supports branching logic within workflows based on step outcomes.

See `src/Werkr.Core/Workflows/` for the workflow engine implementation.

<br/>

## Schedulable Tasks

Tasks are the building blocks of your automation. They can run standalone on a schedule or as steps within a workflow.

- **Standalone tasks** can be triggered on DateTime schedules or at recurring intervals (daily, weekly, monthly).
- **Workflow tasks** are additionally triggered by dependency completion within the DAG, using configurable `DependencyMode` settings.
- **Holiday Calendar** support lets you skip or shift scheduled occurrences on configured holidays, with audit logging for suppressed runs.

See `src/Werkr.Core/Scheduling/` for schedule calculation and holiday date handling.

<br/>

## Task Types

Werkr supports five task types (defined in the `TaskActionType` enum):

### Action
Built-in handlers for common operations — no scripting required. The current set of 11 actions covers file operations (copy, move, rename, create, delete, write content, clear content, test existence), directory creation, and process control (start, stop). Each action has consistent parameter handling and error reporting.

See `src/Werkr.Agent/Operators/Actions/` for the full set of action handlers.

### PowerShell Script
Run PowerShell scripts with an embedded PowerShell 7+ host. You get standard PowerShell output streams (output, error, debug, verbose, warning), exit codes, and exception information.

### PowerShell Command
Execute individual PowerShell commands with the same output handling as script execution.

### Shell Command
Run commands in your operating system's native shell (cmd on Windows, bash/sh on Linux) and receive the process exit code.

### Shell Script
Execute shell scripts with the same native shell and exit code handling as shell commands.

For complex multi-step automation, combine tasks into a **Workflow** (DAG) with dependency-based execution, branching, and condition evaluation.

<br/>

## Flexible Triggers

- **DateTime** — Run tasks at a specific date and time.
- **Interval/Cyclical** — Run tasks periodically (daily, weekly, monthly recurrence with repeat intervals).
- **Task Completion** — Within a workflow, trigger steps based on the completion state of their dependencies (via `ConditionEvaluator` and `DependencyMode`).
- **Holiday Calendar** — Automatically skip or shift occurrences on configured holidays.

<br/><br/>

# Security

Security is a core design concern — there are mandatory steps for initial setup, and multiple layers protect the system at runtime.

- **TLS certificates** are mandatory for all Server, API, and Agent connections.
- **Agent registration** uses an admin-bundle model: an administrator creates a registration bundle on the Server containing the Server's RSA public key, transfers it to the Agent out-of-band, and the Agent completes registration via an encrypted gRPC handshake using RSA+AES hybrid encryption. This establishes a shared AES-256 symmetric key for all subsequent communication.
- **Encrypted gRPC** — After registration, every gRPC payload is wrapped in an `EncryptedEnvelope` (AES-256-GCM). Key rotation is supported via the `RotateSharedKey` RPC.
- **RBAC** — The Server has built-in permission-based role authorization to control access to features and data.
- **TOTP 2FA** — Native two-factor authentication is built into the Server.
- **Path allowlisting** — Agents validate file paths against a configurable allowlist before execution.
- **Platform-native secret storage** — Secrets are stored using OS-native mechanisms (DPAPI on Windows, Keychain on macOS, file-based on Linux).

See [Architecture.md](docs/Architecture.md) for the full security model breakdown.

<br/><br/>

# Licensing and Support

The Werkr project is offered free of charge, without any warranties, under an [MIT license](https://docs.werkr.app/LICENSE.html).

Best effort support and triage is provided on a volunteer basis via [GitHub issues](https://github.com/DarkgreyDevelopment/Werkr.App/issues/new/choose).

<br/><br/>

# Quick Start Guide

For developer setup (building from source, running locally with Aspire, running tests), see [Development.md](docs/Development.md).

For end-user installation, see the [Windows Server Install](docs/articles/HowTo/WindowsServerInstall.md) and [Windows Agent Install](docs/articles/HowTo/WindowsAgentInstall.md) guides.

<br/><br/>

# Contributing

The Werkr project is in its early stages and we're excited that you're interested in contributing! We welcome contributions from developers, users, and anyone interested in task automation and workflow orchestration.

All official project collaboration happens via [GitHub issues](https://github.com/DarkgreyDevelopment/Werkr.App/issues/new/choose) or [discussions](https://github.com/DarkgreyDevelopment/Werkr.App/discussions).

## Project Structure

Werkr is a monorepo with all components under `src/`:

| Project | Purpose |
|---------|---------|
| `Werkr.Server` | Blazor Server UI, ASP.NET Identity, SignalR, user authentication |
| `Werkr.Api` | Application API, gRPC service host, schedule/task/workflow management |
| `Werkr.Agent` | Task execution engine, embedded PowerShell host, built-in actions |
| `Werkr.Core` | Shared business logic — scheduling, workflows, registration, cryptography |
| `Werkr.Common` | Shared models, protobuf definitions, auth policies |
| `Werkr.Common.Configuration` | Strongly-typed configuration classes |
| `Werkr.Data` | EF Core database contexts and entities (PostgreSQL + SQLite) |
| `Werkr.Data.Identity` | ASP.NET Identity database contexts and roles |
| `Werkr.AppHost` | .NET Aspire orchestrator for local development |
| `Werkr.ServiceDefaults` | Aspire service defaults (OpenTelemetry, health checks) |
| `Installer/Msi/` | WiX MSI installer projects and custom actions |
| `Test/Werkr.Tests` | Integration tests (Testcontainers + WebApplicationFactory) |
| `Test/Werkr.Tests.Data` | Data layer unit tests |
| `Test/Werkr.Tests.Server` | Server integration tests |
| `Test/Werkr.Tests.Agent` | Agent end-to-end tests |

See [Architecture.md](docs/Architecture.md) for the full architectural overview and [Development.md](docs/Development.md) for build/test/contribution instructions.

## Feedback, Suggestions, and Feature Requests

We'd love to hear your ideas! Submit a [feature request](https://github.com/DarkgreyDevelopment/Werkr.App/issues/new?template=feature_request.yaml) with a clear description of your proposal and its potential benefits.

## Documentation Improvements

Have suggestions or corrections for the documentation? Submit a [documentation improvement request](https://github.com/DarkgreyDevelopment/Werkr.App/issues/new?template=improve_documentation.yaml).

## Bug Reports

Please report any bugs, performance issues, or security vulnerabilities by opening a [bug report](https://github.com/DarkgreyDevelopment/Werkr.App/issues/new?template=bug_report.yaml). Include steps to reproduce the issue, error messages, your system configuration, and any additional context.

## Code Contributions

Fork the repository, create a new branch from `develop`, and submit a pull request with your changes. Please follow the coding conventions described in [Development.md](docs/Development.md) and include a detailed description in the pull request.

You will need to agree to the [Contribution License Agreement](https://github.com/DarkgreyDevelopment/Werkr.App/issues/new?template=cla_agreement.yml) before your PR is merged.

We appreciate all contributions and look forward to building a collaborative community around Werkr. Thank you for your support!
