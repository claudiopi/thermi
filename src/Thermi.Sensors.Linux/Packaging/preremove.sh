#!/bin/sh
# thermi-sensors' pre-removal, of the .deb, the .rpm and the AUR's package alike: on a removal, not an update, the socket
# and the service stop and stay off. The .deb says remove, the .rpm and the AUR's 0 left installed.
set -e
if [ "$1" = remove ] || [ "$1" = 0 ]; then
    if [ -d /run/systemd/system ]; then
        systemctl disable --now thermi-sensors.socket thermi-sensors.service || true
    else
        systemctl disable thermi-sensors.socket || true
    fi
fi
