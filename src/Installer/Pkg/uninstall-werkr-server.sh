#!/bin/bash
set -e

echo "Uninstalling Werkr Server..."

# Stop and unload services
for PLIST in \
    "/Library/LaunchDaemons/app.werkr.server.plist" \
    "/Library/LaunchDaemons/app.werkr.api.plist"; do
    if [ -f "$PLIST" ]; then
        sudo launchctl unload "$PLIST" 2>/dev/null || true
        sudo rm -f "$PLIST"
    fi
done

# Remove application files
sudo rm -rf /Library/Werkr/Server

# Optionally remove data (prompt user)
read -p "Remove configuration, data, and logs? [y/N] " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    sudo rm -rf /etc/werkr
    sudo rm -rf /var/lib/werkr
    sudo rm -rf /var/log/werkr
fi

# Forget the package receipt
sudo pkgutil --forget app.werkr.server 2>/dev/null || true

echo "Werkr Server uninstalled."
