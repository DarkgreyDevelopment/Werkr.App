#!/bin/bash
set -e

# Require root privileges — avoid using sudo within the script
if [ "$EUID" -ne 0 ]; then
    echo "This script must be run as root." >&2
    exit 1
fi

# Parse arguments for non-interactive mode and data removal
NON_INTERACTIVE=0
PURGE_DATA=0
for arg in "$@"; do
    case "$arg" in
        --non-interactive) NON_INTERACTIVE=1 ;;
        --purge-data|--remove-data) PURGE_DATA=1 ;;
    esac
done

echo "Uninstalling Werkr Agent..."

# Stop and unload service
PLIST="/Library/LaunchDaemons/app.werkr.agent.plist"
if [ -f "$PLIST" ]; then
    launchctl unload "$PLIST" 2>/dev/null || true
    rm -f "$PLIST"
fi

# Remove application files
rm -rf /Library/Werkr/Agent

# Optionally remove data
if [ "$NON_INTERACTIVE" -eq 1 ]; then
    if [ "$PURGE_DATA" -eq 1 ]; then
        rm -rf /etc/werkr /var/lib/werkr /var/log/werkr
    else
        echo "Non-interactive mode: leaving configuration, data, and logs in place."
    fi
else
    read -p "Remove configuration, data, and logs? [y/N] " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        rm -rf /etc/werkr /var/lib/werkr /var/log/werkr
    fi
fi

# Forget the package receipt
pkgutil --forget app.werkr.agent 2>/dev/null || true

echo "Werkr Agent uninstalled."
