#!/bin/sh
# thermi's pre-removal, of the .deb, the .rpm and the AUR's package alike: on a removal, not an update, the overlay stops
# in each session it runs in, and the .deb's AppArmor profile unloads where it loaded. The .deb says remove, the .rpm and
# the AUR's 0 left installed.
set -e
if [ "$1" = remove ] || [ "$1" = 0 ]; then
    if [ -f /etc/apparmor.d/thermi ] && [ -d /sys/kernel/security/apparmor ] && command -v apparmor_parser >/dev/null; then
        apparmor_parser -R /etc/apparmor.d/thermi >/dev/null 2>&1 || true
    fi
    if command -v loginctl >/dev/null && [ -d /run/systemd/system ]; then
        loginctl list-users --no-legend 2>/dev/null | while read -r _ user _; do
            systemctl --user --machine="$user@" stop thermi.service 2>/dev/null || true
        done
    fi
fi
