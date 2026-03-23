# Open Source Acknowledgements & Thank Yous

> The authoritative source for exact package versions is `Directory.Packages.props` in the repository root.

## .NET Platform

The Werkr project is built on the open source [.NET 10](https://dotnet.microsoft.com) platform.

### .NET Runtime & Extensions
Werkr runs on the [.NET runtime](https://github.com/dotnet/runtime) and its associated extensions (hosting, configuration, logging, dependency injection, resilience, service discovery).
The .NET runtime is licensed under the [MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).

### ASP.NET Core
Werkr Server and API use [ASP.NET Core](https://github.com/dotnet/aspnetcore) and its associated extensions (Identity, SignalR, OpenAPI, JWT Bearer authentication).
ASP.NET Core is licensed under an [MIT License](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt).

### .NET SDK
Werkr is written, tested, and published using the [.NET SDK](https://github.com/dotnet/sdk).
The .NET SDK is licensed under an [MIT License](https://github.com/dotnet/sdk/blob/main/LICENSE.TXT).

### .NET Aspire
Werkr uses [.NET Aspire](https://github.com/dotnet/aspire) for local development orchestration and service defaults (hosting, health checks, service discovery, resilience).
.NET Aspire is licensed under an [MIT License](https://github.com/dotnet/aspire/blob/main/LICENSE.TXT).

<br/>

## Build Process & Installers

### GitVersion & Conventional Commits
Werkr uses [GitVersion](https://gitversion.net/) for semver-based versioning and [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/#specification) for commit-driven version bumps.
GitVersion is licensed under an [MIT License](https://github.com/GitTools/GitVersion/blob/main/LICENSE). The Conventional Commits specification is licensed under [Creative Commons CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).

### WiX Toolset
The Werkr Server and Agent MSI installers are built with the [WiX Toolset](https://wixtoolset.org/) (including WiX UI and Util extensions). The installer implements a WiX custom action for deploying files and retrieving install parameters.
The WiX Toolset is licensed under the [Microsoft Reciprocal License (MS-RL)](https://github.com/wixtoolset/wix/blob/develop/LICENSE.TXT).

<br/>

## Database

### Entity Framework Core
Werkr uses [Entity Framework Core](https://github.com/dotnet/efcore) as its database abstraction layer, including the SQLite, Npgsql (PostgreSQL), and InMemory providers. Design-time tooling is used for migrations.
EF Core is licensed under an [MIT License](https://github.com/dotnet/efcore/blob/main/LICENSE.txt).

### EFCore.NamingConventions
Werkr uses [EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions) for snake_case column naming in PostgreSQL.
Licensed under an [Apache License, version 2.0](https://github.com/efcore/EFCore.NamingConventions/blob/main/LICENSE).

### SQLite
[SQLite](https://www.sqlite.org) is in the public domain. The Agent uses SQLite-compatible encrypted SQLite databases.

### PostgreSQL
[PostgreSQL](https://www.postgresql.org) is licensed under the [PostgreSQL License](https://www.postgresql.org/about/licence/).

### Npgsql
Werkr uses [Npgsql](https://github.com/npgsql/npgsql) as the .NET data provider for PostgreSQL, and [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) for EF Core integration.
Npgsql is licensed under the [PostgreSQL License](https://github.com/npgsql/npgsql/blob/main/LICENSE).

<br/>

## Logging & Telemetry

### Serilog
Werkr uses [Serilog](https://github.com/serilog/serilog) for structured logging, with sinks for console output, file output, and OpenTelemetry export.
Serilog is licensed under an [Apache License, version 2.0](https://github.com/serilog/serilog/blob/dev/LICENSE).

### OpenTelemetry
Werkr uses [OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet) for metrics, traces, and logging instrumentation.
OpenTelemetry .NET is licensed under an [Apache License, version 2.0](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/LICENSE).

<br/>

## Communication

### gRPC
Werkr uses [gRPC for .NET](https://github.com/grpc/grpc-dotnet) (`Grpc.AspNetCore`, `Grpc.Net.Client`, `Grpc.Net.ClientFactory`) and [gRPC Tools](https://github.com/grpc/grpc) for protobuf code generation.
gRPC is licensed under an [Apache License, version 2.0](https://github.com/grpc/grpc/blob/master/LICENSE).

### Google Protobuf
Werkr uses [Google.Protobuf](https://github.com/protocolbuffers/protobuf) for protocol buffer serialization.
Protobuf is licensed under a [BSD 3-Clause License](https://github.com/protocolbuffers/protobuf/blob/main/LICENSE).

### SignalR
Werkr Server uses [ASP.NET Core SignalR](https://github.com/dotnet/aspnetcore) for real-time UI communication.
Licensed under an [MIT License](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt) as part of ASP.NET Core.

<br/>

## Agent Packages

### PowerShell SDK
The Werkr Agent hosts PowerShell using the [Microsoft.PowerShell.SDK](https://www.nuget.org/packages/Microsoft.PowerShell.SDK/).
PowerShell is licensed under an [MIT License](https://github.com/PowerShell/PowerShell/blob/master/LICENSE.txt).

<br/>

## Security & Identity

### ASP.NET Core Identity
Werkr Server uses ASP.NET Core Identity for user management, authentication, and role-based authorization.
Licensed under an [MIT License](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt) as part of ASP.NET Core.

### QRCoder
Werkr uses [QRCoder](https://github.com/codebude/QRCoder) for generating TOTP 2FA QR codes.
QRCoder is licensed under an [MIT License](https://github.com/codebude/QRCoder/blob/master/LICENSE.txt).

### System.Security.Cryptography.ProtectedData
Werkr uses the [ProtectedData](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData) package for Windows DPAPI secret storage.
Licensed under an [MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) as part of the .NET runtime.

<br/>

## Utilities

### TimeZoneNames
Werkr uses [TimeZoneNames](https://github.com/mattjohnsonpint/TimeZoneNames) for human-readable time zone display names in scheduling.
TimeZoneNames is licensed under an [MIT License](https://github.com/mattjohnsonpint/TimeZoneNames/blob/main/LICENSE).

<br/>

## Testing

### MSTest
Werkr uses [MSTest](https://github.com/microsoft/testfx) as its test framework with the Microsoft.Testing.Platform runner.
MSTest is licensed under an [MIT License](https://github.com/microsoft/testfx/blob/main/LICENSE).

### Testcontainers
Werkr uses [Testcontainers for .NET](https://github.com/testcontainers/testcontainers-dotnet) (specifically the PostgreSQL module) for integration testing with disposable database containers.
Testcontainers is licensed under an [MIT License](https://github.com/testcontainers/testcontainers-dotnet/blob/develop/LICENSE).

### ASP.NET Core Mvc.Testing
Werkr uses `Microsoft.AspNetCore.Mvc.Testing` for in-process API testing via `WebApplicationFactory`.
Licensed under an [MIT License](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt) as part of ASP.NET Core.

<br/>

## Documentation & Hosting

### DocFX
[docs.werkr.app](https://docs.werkr.app) is generated with [DocFX](https://dotnet.github.io/docfx).
DocFX is licensed under the [MIT License](https://github.com/dotnet/docfx/blob/main/LICENSE).

### DarkFX Theme
[docs.werkr.app](https://docs.werkr.app) uses a modified version of the [DarkFX](https://github.com/steffen-wilke/darkfx) DocFX theme.
DarkFX is licensed under the [MIT License](https://github.com/steffen-wilke/darkfx/blob/master/LICENSE).

### Cascadia Code Font
[docs.werkr.app](https://docs.werkr.app) uses the [Cascadia Code](https://github.com/microsoft/cascadia-code) font.
Cascadia Code is licensed under the [SIL Open Font License](https://github.com/microsoft/cascadia-code/blob/main/LICENSE).

### GitHub Pages
[docs.werkr.app](https://docs.werkr.app) is hosted by [GitHub Pages](https://pages.github.com/). Thank you, GitHub!
