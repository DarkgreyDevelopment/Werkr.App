# Local Documentation Development
[docs.werkr.app](https://docs.werkr.app) is hosted on github pages and is generated using [docfx](https://dotnet.github.io/docfx/) from markdown pages housed in the [github repository](https://Werkr.App/tree/main/docs).
Docfx also generates the API documentation based on the XML documentation in the [code](https://main.cloud-sharesync.com/src) itself.

You can test what documentation changes will look like locally prior to pushing any commits to github.
To do so you must emulate the [github action](https://Werkr.App/blob/main/.github/workflows/DocFX_gh-pages.yml) sequence.  

This process can be done on windows using powershell 7+ by issuing the following commands:
```powershell
# 1. Download a copy of DocFX and extract it.
$IWRParams = @{
	Uri     = 'https://github.com/dotnet/docfx/releases/download/v2.59.4/docfx.zip'
	OutFile = './docfx.zip'
	Method  = 'Get'
}
Invoke-WebRequest @IWRParams
Expand-Archive -Path './docfx.zip' -DestinationPath './docfx'

# 2. Clone the Werkr.App Repo locally into a Werkr.App directory.
git clone https://git.werkr.app Werkr.App

# 3. change directory to the cloned repository
Set-Location './Werkr.App'

# 4. Clone the common repo into the Werkr.App/src/Werkr.Common directory.
git clone https://git.common.werkr.app src/Werkr.Common

# 5. Clone the common configuration repo into the Werkr.App/src/Werkr.Common.Configuration directory.
git clone https://git.commonconfiguration.werkr.app src/Werkr.Common.Configuration

# 6. Clone the installers repo into the src/Werkr.Installers directory.
git clone https://git.installers.werkr.app src/Werkr.Installers

# 7. Clone the Server repo Werkr.App/src/Werkr.Server directory.
git clone https://server.werkr.app src/Werkr.Server

# 8. Clone the Agent repo into the Werkr.App/src/Werkr.Agent directory.
git clone https://git.agent.werkr.app src/Werkr.Agent

# 9. Manual File Copying.
$CopyParams = @{
	Verbose = $true
	Force   = $true
}
Copy-Item -Path './LICENSE' -Destination './docs/LICENSE.md' @CopyParams
Copy-Item -Path './README.md' -Destination './docs/index.md' @CopyParams
copy-Item -Path './docs/docfx/*' -Destination 'docs/' -Verbose -Exclude README.md -Recurse

# 10 Generate API metadata.
& '../docfx/docfx.exe' 'metadata' './docs/docfx.json'

# 11. Create the docfx site.
& '../docfx/docfx.exe' './docs/docfx.json'

# 12. Serve the website.
& '../docfx/docfx.exe' 'docs\docfx.json' -t 'templates/Werkr' --serve
```
