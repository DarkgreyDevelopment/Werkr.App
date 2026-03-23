# Linux Server Installation

On Linux, deploy the Werkr Server using the portable archive. A `.deb` installer is planned for a future release.

To run the Werkr Server on Linux:

1. Download the latest portable release from the [GitHub releases](https://github.com/DarkgreyDevelopment/Werkr.App/releases/latest) page (select the Linux x64 or arm64 archive).
2. Extract the archive to your preferred installation directory.
3. Configure `appsettings.json` with your TLS certificate, database connection, and allowed hosts settings.
4. Register the application as a systemd service, or run it directly.

For building from source and detailed configuration, see [Development.md](../../Development.md).
