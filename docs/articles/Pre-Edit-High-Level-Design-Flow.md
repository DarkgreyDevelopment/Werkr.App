# High-Level Development Phases

This document tracks the original development roadmap and marks what has been completed. For current architecture details, see [Architecture.md](../Architecture.md). For developer setup, see [Development.md](../Development.md).

---

## Phase 1 - Foundations

All foundational work has been completed:

- [ ] GitHub repository setup with issue templates, CI pipeline, and conventional commits
- [ ] Server, API, and Agent applications for Windows 10+ and Linux (x64 and arm64)
- [ ] MSI installers for Windows (WiX Toolset)
- [ ] Portable editions for Windows and Linux
- [ ] x64 and arm64 CPU architecture support
- [ ] Kestrel webserver (Server and API) with PostgreSQL and Sqlite database support
- [ ] EF Core database schema and data access layer with dual-provider architecture
- [ ] Agent component for remote task execution with embedded PowerShell host
- [ ] MIT license

## Phase 2 - Core Features

Core task and workflow functionality is implemented:

- [ ] Task management - creation, scheduling, ad-hoc execution
- [ ] Workflow DAG engine with topological ordering and dependency-based execution
- [ ] Schedule recurrence - daily, weekly, monthly patterns with time zone support
- [ ] Holiday Calendar system for skipping or shifting scheduled occurrences
- [ ] DateTime and Interval/Cyclical triggers
- [ ] Task completion state triggers within workflows (`ConditionEvaluator`, `DependencyMode`)
- [ ] 11 built-in action handlers (file operations, process control)
- [ ] PowerShell script and command execution with full output stream capture
- [ ] Shell command and script execution
- [ ] Task output handling - exit codes, output previews, full log retrieval
- [ ] Job result reporting from Agent to API via encrypted gRPC

## Phase 3 - Advanced Features

Some advanced capabilities are implemented, others remain on the roadmap:

- [ ] Branching logic and condition evaluation in workflows
- [ ] Dependency modes for flexible step triggering
- [ ] Control statements per workflow step
- [ ] DAG visualization UI
- [ ] Inter-step data passing / workflow variable system
- [ ] Task retry and failure recovery mechanisms
- [ ] Workflow import/export
- [ ] Task templates and workflow templates

## Phase 4 - Security

Security infrastructure is fully implemented:

- [ ] Permission-based role authorization (RBAC)
- [ ] TOTP two-factor authentication
- [ ] TLS mandatory for all connections
- [ ] Encrypted gRPC payloads (AES-256-GCM `EncryptedEnvelope`)
- [ ] Admin-bundle registration with RSA+AES hybrid encryption
- [ ] Shared key rotation via `RotateSharedKey` RPC
- [ ] Path allowlisting on agents
- [ ] Platform-native secret storage (DPAPI, Keychain, file-based)

## Phase 5 - Community and Documentation

- [ ] GitHub issue templates (bug report, feature request, documentation improvement, CLA)
- [ ] DocFX-generated documentation site (docs.werkr.app)
- [ ] Contribution License Agreement
- [ ] Monorepo consolidation
- [ ] Comprehensive tutorials and troubleshooting resources
- [ ] Audit log UI
- [ ] Localization / internationalization

## Phase 6 - Extensibility and Optimization

- [ ] Additional built-in actions (archive, network, notification, data transformation)
- [ ] File monitoring action (directory watching)
- [ ] Webhook notifications for workflow completion
- [ ] OS-specific actions (Windows Services, systemd)
- [ ] Distributed execution across multiple agents
- [ ] Performance dashboards
- [ ] RESTful API documentation for external integrations

## Phase 7 - Nice-to-Haves

- [ ] macOS support
- [ ] `.deb` Linux installers
- [ ] Secret/credential management
- [ ] SSO support
- [ ] Mobile monitoring app
- [ ] Cloud storage integration
- [ ] Workflow completion as external trigger
