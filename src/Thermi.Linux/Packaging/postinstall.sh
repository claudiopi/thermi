#!/bin/sh
# thermi's post-install, of the .deb, the .rpm and the AUR's package alike: on a first install, the overlay starts in each
# graphical session that is signed in, so it shows at once; a greeter's, an SSH or a console session has none, where it
# would not show. Starting at sign-in is each person's own switch, their user unit's link, which thermi's first start
# turns on; an overlay already running keeps running the files it started with.
# The .deb says configure with no version before, the .rpm and the AUR's 1 installed.
set -e
if [ "$1" = 1 ] || { [ "$1" = configure ] && [ -z "$2" ]; }; then
    if command -v loginctl >/dev/null && [ -d /run/systemd/system ]; then
        loginctl list-users --no-legend 2>/dev/null | while read -r _ user _; do
            if systemctl --user --machine="$user@" is-active --quiet graphical-session.target 2>/dev/null; then
                systemctl --user --machine="$user@" start thermi.service 2>/dev/null || true
            fi
        done
    fi
fi