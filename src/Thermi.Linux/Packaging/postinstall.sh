#!/bin/sh
# thermi's post-install, of the .deb, the .rpm and the AUR's package alike: on a first install, the overlay starts in each
# session that is signed in, so it shows at once. Starting at sign-in is each person's own switch, their user unit's link,
# which thermi's first start turns on; an overlay that runs hands over to an update's files by itself.
# The .deb says configure with no version before, the .rpm and the AUR's 1 installed.
set -e
if [ "$1" = 1 ] || { [ "$1" = configure ] && [ -z "$2" ]; }; then
    if command -v loginctl >/dev/null && [ -d /run/systemd/system ]; then
        loginctl list-users --no-legend 2>/dev/null | while read -r _ user _; do
            systemctl --user --machine="$user@" start thermi.service 2>/dev/null || true
        done
    fi
fi
