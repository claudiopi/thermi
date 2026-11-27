#!/bin/sh
# thermi-sensors' pre-removal, of the .deb, the .rpm and the AUR's package alike: on a removal, not an update, the socket
# and the service stop. The .rpm and the AUR's, with 0 left installed, turn the socket off too; the .deb's remove keeps
# its link, as apt keeps the package's record until a purge, so installed again it listens as it did, and the purge
# drops it.
set -e
if [ "$1" = 0 ]; then
    if [ -d /run/systemd/system ]; then
        systemctl disable --now thermi-sensors.socket thermi-sensors.service || true
    else
        systemctl disable thermi-sensors.socket || true
    fi
elif [ "$1" = remove ] && [ -d /run/systemd/system ]; then
    systemctl stop thermi-sensors.socket thermi-sensors.service || true
fi