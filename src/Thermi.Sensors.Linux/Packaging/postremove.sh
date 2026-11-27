#!/bin/sh
# thermi-sensors' post-removal, of the .deb, the .rpm and the AUR's package alike: on a removal, the repository the
# post-install added goes, with the key it names, which left with the package; on the .deb's purge, the socket's link
# its removal kept. Each person's settings, in their home, stay. The .deb says remove or purge, the .rpm and the AUR's 0
# left installed.
set -e
if [ "$1" = remove ] || [ "$1" = purge ] || [ "$1" = 0 ]; then
    rm -f /etc/apt/sources.list.d/thermi.sources /etc/yum.repos.d/thermi.repo
fi
if [ "$1" = purge ]; then
    rm -f /etc/systemd/system/sockets.target.wants/thermi-sensors.socket
fi
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi