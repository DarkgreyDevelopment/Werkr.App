# Local Documentation Development

[docs.werkr.app](https://docs.werkr.app) is hosted on GitHub Pages and generated using [DocFX](https://dotnet.github.io/docfx/) from the markdown pages and XML documentation in this repository.

You can preview documentation changes locally before pushing commits.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (already required for the project — see `global.json`)
- [DocFX](https://dotnet.github.io/docfx/) — install as a global tool:

```powershell
dotnet tool install -g docfx
```

---

## Building and Previewing Docs

From the repository root, run the following commands:

```powershell
# 1. Copy required files into the docs directory (emulates the GitHub Actions workflow).
Copy-Item -Path './LICENSE' -Destination './docs/LICENSE.md' -Force
Copy-Item -Path './README.md' -Destination './docs/index.md' -Force
Copy-Item -Path './docs/docfx/*' -Destination './docs/' -Exclude 'README.md' -Recurse -Force

# 2. Generate API metadata from the source projects.
docfx metadata docs/docfx.json

# 3. Build the DocFX site.
docfx build docs/docfx.json

# 4. Serve the site locally for preview.
docfx serve docs/_site
```

The site will be available at `http://localhost:8080` by default.

---

## How It Works

- **`docs/docfx/docfx.json`** defines which projects generate API metadata and which markdown files are included in the site build. The `metadata` section points to project files under `src/` and the `build` section pulls content from `docs/articles/`, `docs/api/`, and root markdown files.
- **`docs/docfx/filterConfig.yml`** controls which types and members are included or excluded from the API documentation.
- **`docs/docfx/templates/Werkr/`** contains the custom DocFX theme (based on DarkFX).
- **`docs/articles/`** contains the user-facing documentation articles.
- **`docs/images/`** contains screenshots and logos referenced by articles.

---

## Notes

- The DocFX `src` path in `docfx.json` is relative to the `docfx.json` file location (`docs/docfx/`). The path `../../src` resolves to the repository's `src/` directory.
- If you add a new project to the solution that should appear in API documentation, add its `.csproj` path to the `metadata[0].src.files` array in `docfx.json`.
- The custom template in `templates/Werkr` overrides default DocFX styles. See the [DarkFX](https://github.com/steffen-wilke/darkfx) repository for the base theme.
