#!/bin/sh
# thermi-sensors' post-install, of the .deb, the .rpm and the AUR's package alike, on an install and on an update: the
# service's user and rule, its socket listening from the first install on, the service restarted on the new files if it
# ran, and thermi's repository, so updates come with the system's. The .deb says configure, with no version before on a
# first install; the .rpm and the AUR's how many are installed, 1 on a first install.
# scripts/linux-packages.ps1 writes the repository's address and channel in.
set -e
systemd-sysusers thermi-sensors.conf
udevadm control --reload-rules 2>/dev/null || true
udevadm trigger --subsystem-match=powercap --action=add 2>/dev/null || true
# Without systemd running, as in an image being built, the socket listens from its next boot.
if [ "$1" = 1 ] || { [ "$1" = configure ] && [ -z "$2" ]; }; then
    systemctl enable thermi-sensors.socket
fi
if [ -d /run/systemd/system ]; then
    systemctl daemon-reload
    if systemctl is-enabled --quiet thermi-sensors.socket; then
        systemctl start thermi-sensors.socket
    fi
    systemctl try-restart thermi-sensors.service
fi

# Written only when missing, so a person's choice of the beta channel stays across updates. The key each names is the
# package's own file, which the system checks every update of the repository against; the AUR's package has none, as
# its updates come from the AUR.
if [ -e /usr/share/keyrings/thermi.asc ] && [ -d /etc/apt/sources.list.d ] && [ ! -e /etc/apt/sources.list.d/thermi.sources ]; then
    cat > /etc/apt/sources.list.d/thermi.sources <<'EOF'
Types: deb
URIs: @REPOSITORY@apt
Suites: @CHANNEL@
Components: main
Signed-By: /usr/share/keyrings/thermi.asc
EOF
fi
if [ -e /etc/pki/rpm-gpg/RPM-GPG-KEY-thermi ] && [ -d /etc/yum.repos.d ] && [ ! -e /etc/yum.repos.d/thermi.repo ]; then
    cat > /etc/yum.repos.d/thermi.repo <<'EOF'
[thermi]
name=thermi
baseurl=@REPOSITORY@rpm/@CHANNEL@
enabled=1
gpgcheck=1
repo_gpgcheck=1
gpgkey=file:///etc/pki/rpm-gpg/RPM-GPG-KEY-thermi
EOF
fi
