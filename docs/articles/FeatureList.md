# Werkr Project Features

This document describes features that are currently implemented in the codebase and features on the roadmap. For architectural context, see [Architecture.md](../Architecture.md).

---

## Implemented Features

### Task Management
- Predefine tasks to run on a schedule, or create ad-hoc tasks to run on demand.
- Configure start dates, end times, and maximum run durations.
- Link tasks together into workflows (viewable as DAGs [directed acyclic graphs]) for complex automation.

### Workflow Engine
- Directed acyclic graph (DAG) model with topological ordering of steps.
- Dependency-based execution - steps declare dependencies on other steps and specify a `DependencyMode`.
- Branching logic via `ConditionEvaluator` - conditionally execute steps based on dependency outcomes.
- Control statements and condition expressions per step.
- Workflow run tracking.
- See `src/Werkr.Core/Workflows/` for the executor, condition evaluator, and run tracker.

### Scheduling
- Daily, weekly, and monthly recurrence patterns with configurable intervals.
- Repeat intervals within a duration window.
- Time zone-aware scheduling with start and expiration dates.
- **Holiday Calendar** support - skip or shift occurrences on configured holidays, with audit logging for suppressed runs.
- See `src/Werkr.Core/Scheduling/` for the schedule calculator and holiday date service.

### Task Types
Five task types as defined by the `TaskActionType` enum:

| Type | Description |
|------|-------------|
| **Action** | 11 built-in handlers for common operations (no scripting required). |
| **PowerShell Script** | Execute PowerShell scripts with full output stream capture. |
| **PowerShell Command** | Execute individual PowerShell commands. |
| **Shell Command** | Run native OS shell commands with exit code capture. |
| **Shell Script** | Execute shell scripts with native OS shell. |

### Built-in Actions
The current set of action handlers in `src/Werkr.Agent/Operators/Actions/`:

| Action | Operation |
|--------|-----------|
| CopyFile | Copy a file to a new location. |
| MoveFile | Move a file to a new location. |
| RenameFile | Rename a file. |
| CreateFile | Create a new file. |
| DeleteFile | Delete a file. |
| CreateDirectory | Create a new directory. |
| WriteContent | Write content to a file. |
| ClearContent | Clear the content of a file. |
| TestExists | Check whether a file or directory exists. |
| StartProcess | Start an OS process. |
| StopProcess | Stop a running OS process. |

### Triggers
- **DateTime** - Run at a specific date and time.
- **Interval/Cyclical** - Run periodically (daily, weekly, monthly recurrence).
- **Task Completion** - Within workflows, trigger steps based on dependency completion states.
- **Holiday Calendar** - Automatically skip or shift scheduled occurrences on holidays.

### Security
- **TLS** - Mandatory for all connections.
- **Encrypted gRPC** - All payloads wrapped in `EncryptedEnvelope` (AES-256-GCM) after registration.
- **Admin-bundle registration** - RSA+AES hybrid encryption for agent registration handshake.
- **Key rotation** - `RotateSharedKey` RPC with grace period for in-flight messages.
- **RBAC** - Permission-based role authorization with configurable policies.
- **TOTP 2FA** - Built-in two-factor authentication.
- **Path allowlisting** - Agents validate file paths before execution.
- **Platform-native secret storage** - DPAPI (Windows), Keychain (macOS), file-based (Linux).
- See `src/Werkr.Core/Cryptography/`, `src/Werkr.Core/Security/`, and `src/Werkr.Common/Auth/`.

### Platform Support
- **Server + API + Agent** on Windows 10+ and Linux (x64 and arm64).
- MSI installers for Windows.
- Portable editions (no difference from installed version).

### Database
- **PostgreSQL** and **SQLite** are both supported for all components. The API and Server default to PostgreSQL; the Agent defaults to SQLite.
- Dual-provider EF Core architecture with shared entity model.
- See `src/Werkr.Data/` and `src/Werkr.Data.Identity/`.

### Observability
- **.NET Aspire** integration for local development orchestration (see `src/Werkr.AppHost/`).
- **Serilog** structured logging (console, file, OpenTelemetry sinks).
- **OpenTelemetry** metrics, traces, and logging (see `src/Werkr.ServiceDefaults/`).
- Health check endpoints (`/health` and `/alive`).

### Community & Licensing
- MIT license.
- GitHub issue templates for bugs, feature requests, documentation improvements, and CLA agreements.
- Contribution License Agreement process.

---

## Roadmap

The following items are planned or not yet implemented. These are described at a category level - specific implementation details may change.

- **Additional built-in actions** - Archive operations, network connectivity checks, file introspection, notification dispatch, data transformation.
- **File monitoring** - Directory watching for file-drop workflows.
- **Workflow completion trigger** - Trigger tasks or workflows based on the completion state of an external workflow.
- **DAG visualization UI** - Visual display and editing of workflow graphs.
- **Inter-step data passing** - Workflow variable system for passing data between steps.
- **macOS support** — Cross-platform secret store infrastructure is already in place. Installer and official platform support are on the roadmap.
- **Linux installers** — `.deb` packages are on the roadmap. Currently, Linux deployment uses portable archives.
- **Webhook notifications** - Task and workflow completion notifications to external services.
- **OS-specific actions** - Windows Service management, systemd unit management.
- **Secret/credential management** - Secure storage and injection of credentials into task execution contexts.
- **Database Encryption** - Database encryption for defense in depth in addition to filesystem permissions.
