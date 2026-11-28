#!/bin/sh
# thermi's post-install, of the .deb, the .rpm and the AUR's package alike: on a first install, the overlay starts in each
# person's graphical session that is signed in, so it shows at once. A login screen's session, as GDM's, is a greeter's,
# and an SSH or a console session has no graphical one, so none of them gets it. Starting at sign-in is each person's
# own switch, their user unit's link, which thermi's first start turns on.
# The .deb says configure with no version before, the .rpm and the AUR's 1 installed. On each install and update, the
# .deb's AppArmor profile loads, so the settings window opens without a restart where AppArmor would keep WebKit's sandbox
# from its user namespace; with no AppArmor, or no profile, there is nothing to load, and a failure fails no install.
set -e
if [ -f /etc/apparmor.d/thermi ] && [ -d /sys/kernel/security/apparmor ] && command -v apparmor_parser >/dev/null; then
    apparmor_parser -r -T -W /etc/apparmor.d/thermi >/dev/null 2>&1 || true
fi
if [ "$1" = 1 ] || { [ "$1" = configure ] && [ -z "$2" ]; }; then
    if command -v loginctl >/dev/null && [ -d /run/systemd/system ]; then
        loginctl list-sessions --no-legend 2>/dev/null | while read -r session _; do
            [ "$(loginctl show-session "$session" -p Class --value 2>/dev/null)" = user ] || continue
            user=$(loginctl show-session "$session" -p Name --value 2>/dev/null)
            if systemctl --user --machine="$user@" is-active --quiet graphical-session.target 2>/dev/null; then
                systemctl --user --machine="$user@" start thermi.service 2>/dev/null || true
            fi
        done
    fi
fi