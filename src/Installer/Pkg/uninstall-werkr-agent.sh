#!/bin/bash
set -e

echo "Uninstalling Werkr Agent..."

# Stop and unload service
PLIST="/Library/LaunchDaemons/app.werkr.agent.plist"
if [ -f "$PLIST" ]; then
    sudo launchctl unload "$PLIST" 2>/dev/null || true
    sudo rm -f "$PLIST"
fi

# Remove application files
sudo rm -rf /Library/Werkr/Agent

# Optionally remove data (prompt user)
read -p "Remove configuration, data, and logs? [y/N] " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    sudo rm -rf /etc/werkr
    sudo rm -rf /var/lib/werkr
    sudo rm -rf /var/log/werkr
fi

# Forget the package receipt
sudo pkgutil --forget app.werkr.agent 2>/dev/null || true

echo "Werkr Agent uninstalled."
