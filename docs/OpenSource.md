# Open Source Acknowledgements:

## Dotnet: 
The Werkr project is primarily C# based and utilizes the open source [dotnet](https://dotnet.microsoft.com) 7 software framework.  

<br/>

### Dotnet Runtime & Dotnet Extensions:
Werkr runs because of the [dotnet runtime](https://github.com/dotnet/runtime) and its associated extensions.  
The dotnet runtime is licensed under the [MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).  

<br/>

### Dotnet AspNetCore: & AspNetCore Extensions
Werkr Server utilizes [dotnet aspnetcore](https://github.com/dotnet/aspnetcore) and its associated extensions.  
AspNetCore is licensed under an [MIT License](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)  

<br/>

### Dotnet SDK:
Werkr is written, tested, and published using the [dotnet SDK](https://github.com/dotnet/sdk).  
The Dotnet SDK is licensed under an [MIT License](https://github.com/dotnet/sdk/blob/main/LICENSE.TXT).  

<br/><br/>

## Build Process & Installers:

### GitVersion & Conventional Commits:
All of the Werkr code repos utilize [GitVersion](https://gitversion.net/) to version releases and artifacts. Semver style versioning occurs upon commit using the gitversion and [conventional commits](https://www.conventionalcommits.org/en/v1.0.0/#specification).  
GitVersion is licensed under an [MIT License](https://github.com/GitTools/GitVersion/blob/main/LICENSE). The conventional commit specification is licensed under an [Creative Commons - CC BY 3.0](https://creativecommons.org/licenses/by/3.0/) license.  

<br/>

### Wix ToolSet
The Werkr Server and Agent msi installers are generated utilizing the [Wix Toolset](https://wixtoolset.org/) and the Werkr Installer implements a wix custom action to deploy files and retrieve install parameters during installation.  
The Wix Toolset has been licensed under an [Microsoft Reciprocal License (MS-RL)](https://github.com/wixtoolset/wix/blob/develop/LICENSE.TXT)  

<br/><br/>


## Database: 

### Microsoft.EntityFrameworkCore:
Werkr utilizes [EntityFrameWorkCore](https://github.com/dotnet/efcore) for its database abstraction layer.  
EFCore is licensed under an [MIT License](https://github.com/dotnet/efcore/blob/main/LICENSE.txt).  

<br/>

### Sqlite:
[Sqlite](https://www.sqlite.org) - has been released under the public domain.  

<br/>

### PostgreSQL:
[PostgreSQL](https://www.postgresql.org) - PostgreSQL is licensed under the [PostgreSQL license](https://www.postgresql.org/about/licence/).  

<br/><br/>

## Logging and Telemetry:

### OpenTelemetry:
Werkr utilizes [OpenTelemetry](https://opentelemetry.io) ([dotnet](https://github.com/open-telemetry/opentelemetry-dotnet)) for telemetry/trace logging.  
OpenTelemetry dotnet is licensed under an [Apache License, version 2.0](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/LICENSE).  

### Log4Net:
Werkr utilizes [log4net](https://logging.apache.org/log4net/) for logging purposes.  
Log4net is licensed under the [Apache License, version 2.0](https://www.apache.org/licenses/LICENSE-2.0).  

<br/><br/>

## Server and Agent packages:

### Grpc
The Werkr Server and Agent utilize [grpc](https://github.com/grpc/grpc), specifically the [Grpc.Core](https://www.nuget.org/packages/Grpc.Core) and [Grpc.Tools](https://www.nuget.org/packages/Grpc.Tools) packages, for communications.  
Grpc is licensed under an [Apache License, version 2.0](https://github.com/grpc/grpc/blob/master/LICENSE).  

<br/>

### Grpc Dotnet
The Werkr Server and Agent utilize [grpc-dotnet](https://github.com/grpc/grpc-dotnet), in the form of the [Grpc.Net.Client](https://www.nuget.org/packages/Grpc.Net.Client), [Grpc.AspNetCore](https://www.nuget.org/packages/Grpc.AspNetCore), & [Grpc.Core.Api](https://www.nuget.org/packages/Grpc.Core.Api/) packages.  
Grpc-Dotnet is licensed under an [Apache License, version 2.0](https://github.com/grpc/grpc-dotnet/blob/master/LICENSE)  

<br/>

### PowerShell Sdk
The Werkr Agent hosts PowerShell utilizing the [Microsoft.PowerShell.SDK](https://www.nuget.org/packages/Microsoft.PowerShell.SDK/).  
PowerShell is licensed under an [MIT License](https://github.com/PowerShell/PowerShell/blob/master/LICENSE.txt)

<br/><br/>

## Documentation and Hosting

### DocFX
[docs.werkr.app](https://docs.werkr.app) utilizes [DocFX](https://dotnet.github.io/docfx) to generate public documentation.  
DocFX is licensed under the [MIT license](https://github.com/dotnet/docfx/blob/main/LICENSE)  

<br/>

### DocFX Theme - DarkFX
[docs.werkr.app](https://docs.werkr.app) utilizes a modified version of the [darkfx](https://github.com/steffen-wilke/darkfx) DocFX theme/template.  
DarkFX is licensed under the [MIT license](https://github.com/steffen-wilke/darkfx/blob/master/LICENSE)  

<br/>

### Cascadia Code Font
[docs.werkr.app](https://docs.werkr.app) utilizes the Cascadia Code font.  
Cascadia Code is licensed under the [SIL OPEN FONT LICENSE](https://github.com/microsoft/cascadia-code/blob/main/LICENSE).  

<br/>

### GitHub Pages
[docs.werkr.app](https://docs.werkr.app) is hosted for free by [github pages](https://pages.github.com/). Thank you [github](https://github.com/)!.  