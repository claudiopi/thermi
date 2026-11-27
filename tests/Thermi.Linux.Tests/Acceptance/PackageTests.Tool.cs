namespace Thermi.Linux.Tests.Acceptance;

/// <summary>Each distribution's package tool, as the package tests run it in their containers.</summary>
public sealed partial class PackageTests
{
    /// <summary>A distribution's own package tool, as a person runs it.</summary>
    private sealed record Tool(string Image, string Format)
    {
        // Arch's packages are built from the AUR's PKGBUILD, as a person builds them, by a user of their own: makepkg
        // never runs as root.
        private const string Aur = "/home/builder/aur";

        // What each tool says of the package that holds a file, of the packages thermi's two depend on or recommend, and of
        // what a package provides besides its name, which only pacman's dependencies on a library's name need.
        private const string DebClaims = """
            owner() { { dpkg -S "$1" || dpkg -S "$(realpath "$1")"; } 2>/dev/null | head -n 1 | cut -d: -f1; }
            provides() { :; }
            declared=$(echo thermi; echo thermi-sensors; dpkg-query -W -f '${Depends}, ${Recommends}, ' thermi-sensors thermi \
                | tr ',|' '\n\n' | sed 's/(.*//; s/[[:space:]]//g')

            """;

        private const string RpmClaims = """
            owner() { rpm -qf --qf '%{NAME}\n' "$1" "$(realpath "$1")" 2>/dev/null | grep -v ' ' | head -n 1; }
            provides() { :; }
            needs=$(rpm -q --requires thermi-sensors thermi; rpm -q --recommends thermi-sensors thermi)
            declared=$(echo thermi; echo thermi-sensors; for need in $needs; do rpm -q --whatprovides --qf '%{NAME}\n' "$need" 2>/dev/null; done)

            """;

        private const string PacmanClaims = """
            owner() { pacman -Qqo "$1" 2>/dev/null; }
            provides() { pacman -Qi "$1" | sed -n 's/^Provides *: //p'; }
            declared=$(echo thermi-bin; echo thermi-sensors-bin; awk '/^%(DEPENDS|OPTDEPENDS)%$/ { on = 1; next } /^%/ { on = 0 } on && NF' \
                /var/lib/pacman/local/thermi-*bin-*/desc | sed 's/:.*//')

            """;

        // A library is claimed when a package the packages declare holds it, by its name or one it provides.
        private const string Claimed = """
            claimed() {
                [ -n "$1" ] || return 1
                for name in "$1" $(provides "$1"); do printf '%s\n' "$declared" | grep -qxF "$name" && return 0; done
                return 1
            }
            awk -f /work/asked.awk /tmp/loads.* | sort -u | while read -r file; do
                owner=$(owner "$file")
                claimed "$owner" || echo "$file, of ${owner:-no package}"
            done
            """;

        /// <summary>
        /// Each library a file of thermi's asked the loader for, as <c>LD_DEBUG=files,libs</c> tells it, and the file the
        /// loader mapped for it, the last it tried; one it found nowhere is no line. Its lines end as awk reads them.
        /// </summary>
        public static readonly string Asked = """
            { pid = $1 }
            /file=.*;  (needed|dynamically loaded) by / {
                name = $0; sub(/.*file=/, "", name); sub(/ \[.*/, "", name)
                asked[pid] = ($0 ~ / by (\/usr\/lib\/thermi\/|thermi-cli )/) ? name : ""
                found[pid] = name
                next
            }
            /trying file=/ { path = $0; sub(/.*trying file=/, "", path); found[pid] = path; next }
            /;  generating link map/ {
                name = $0; sub(/.*file=/, "", name); sub(/ \[.*/, "", name)
                if (asked[pid] != "" && name == asked[pid]) print found[pid]
                asked[pid] = ""
            }
            """.ReplaceLineEndings("\n");

        public static Tool Of(Distribution row) => row switch
        {
            Distribution.Ubuntu2404 => new(Container.Ubuntu2404, "deb"),
            Distribution.Ubuntu2604 => new(Container.Ubuntu2604, "deb"),
            Distribution.Debian13 => new(Container.Debian13, "deb"),
            Distribution.Fedora => new(Container.Fedora, "rpm"),
            Distribution.Arch => new(Container.Arch, "pkg.tar.zst"),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown distribution."),
        };

        /// <summary>
        /// thermi-cli as the person, over a settings file, which it locks by a hash of its path: the runtime loads ICU for
        /// the texts and OpenSSL for the hash by their names as it runs, which no file of the package names, and the loader
        /// says what each file of thermi's asked it for.
        /// </summary>
        public static string Load() =>
            "mkdir -p /home/person/.config/thermi && echo '{}' > /home/person/.config/thermi/config.json && chown -R person /home/person/.config"
            + " && su person -c 'LD_DEBUG=files,libs LD_DEBUG_OUTPUT=/tmp/loads thermi-cli status --schema 1'";

        /// <summary>
        /// Each library <see cref="Load"/> loaded for thermi's files that no package thermi's packages declare holds, a line
        /// each; its lines end as the shell reads them.
        /// </summary>
        public string Unclaimed() => (Format switch
        {
            "deb" => DebClaims,
            "rpm" => RpmClaims,
            _ => PacmanClaims,
        } + Claimed).ReplaceLineEndings("\n");

        /// <summary>
        /// A package's name as the distribution knows it: on Arch, the AUR's <c>thermi-bin</c>, which R1 names, packs both,
        /// <c>thermi-bin</c> and <c>thermi-sensors-bin</c>, as the AUR names a package of a build's own files.
        /// </summary>
        public string Package(string name) => Format == "pkg.tar.zst" ? $"{name}-bin" : name;

        /// <summary>
        /// What a desktop PC has for each user's own systemd, which Debian's and Ubuntu's images lack: logind's PAM module,
        /// which starts it, and the user's bus it answers on.
        /// </summary>
        public string Sessions() => Format == "deb" ? Container.Installs(Image, "libpam-systemd dbus-user-session") : "true";

        /// <summary>The distribution's package of Python, which a test's own client runs on.</summary>
        public string Python => Format == "deb" ? "python3-minimal" : "python3";

        /// <summary>
        /// What a PC holds before thermi's packages: apt's lists; su, which Fedora's image lacks and a PC has; or on Arch
        /// the packages built from the PKGBUILD.
        /// </summary>
        public string Prepare() => Format switch
        {
            "pkg.tar.zst" => $"{Container.Installs(Image, "base-devel")} && useradd -m builder"
                + $" && cp -r {Container.Packages}/aur {Aur} && chown -R builder {Aur}"
                + $" && su builder -c 'cd {Aur} && makepkg --nodeps --noconfirm'",
            "deb" => "apt-get update",
            _ => Container.Installs(Image, "util-linux"),
        };

        // Debian names a package's architecture amd64, Fedora x86_64; a file, with its libraries from the distribution's
        // repositories, or from what the PC already holds once its network is cut.
        public string Install(string package, bool offline = false) => Format switch
        {
            "deb" => $"DEBIAN_FRONTEND=noninteractive apt-get install -y $(ls {Container.Packages}/{package}_[0-9]*_amd64.deb)",
            "rpm" => $"dnf install -y {(offline ? "--disablerepo='*' " : "")}$(ls {Container.Packages}/{package}-[0-9]*.x86_64.rpm)",
            _ => $"pacman -U --noconfirm $(ls {Aur}/{Package(package)}-[0-9]*-x86_64.pkg.tar.zst)",
        };

        /// <summary>A version of a package from the files the levers put in the channel, as the first install from thermi's site.</summary>
        public string InstallFromChannel(string package, string version) => Format == "deb"
            ? $"DEBIAN_FRONTEND=noninteractive apt-get install -y $(find {Container.Work}/channel -name '{package}_{version}*_amd64.deb' | head -n 1)"
            : $"dnf install -y $(find {Container.Work}/channel -name '{package}-{version}*.x86_64.rpm' | head -n 1)";

        // A removal that leaves the libraries the packages brought, as apt's and pacman's do, so they install again offline.
        public string Remove() => Format switch
        {
            "deb" => "apt-get remove -y thermi thermi-sensors",
            "rpm" => "dnf remove -y --setopt=clean_requirements_on_remove=False thermi thermi-sensors",
            _ => $"pacman -R --noconfirm {Package("thermi")} {Package("thermi-sensors")}",
        };

        // A removal of everything the packages wrote outside each person's home, which only apt tells from a removal.
        public string Purge() => Format == "deb" ? "apt-get purge -y thermi thermi-sensors" : Remove();

        // The files a package installed, its folders aside, which the tools list too.
        public string Files(string package) => Format switch
        {
            "deb" => $"dpkg -L {package}",
            "rpm" => $"rpm -ql {package}",
            _ => $"pacman -Qlq {Package(package)}",
        } + " | while read -r file; do if [ -f \"$file\" ]; then echo \"$file\"; fi; done";

        // The repository a package added names a key a package installed, which the system checks each update against;
        // Arch's updates come from the AUR instead, which adds none.
        public string SignedRepository() => Format switch
        {
            "deb" => "key=$(sed -n 's/^Signed-By: *//p' /etc/apt/sources.list.d/thermi.sources) && dpkg -S \"$key\"",
            "rpm" => "grep -qx 'gpgcheck=1' /etc/yum.repos.d/thermi.repo && rpm -qf \"$(sed -n 's|^gpgkey=file://||p' /etc/yum.repos.d/thermi.repo)\"",
            _ => "! ls /etc/pacman.d/thermi* 2>/dev/null",
        };

        // The same build packed for arm64 too, at the same version; Arch's PKGBUILD builds for both.
        public string Arm64() => Format switch
        {
            "deb" => $"for p in thermi thermi-sensors; do [ \"$(dpkg-deb -f {Container.Packages}/${{p}}_*_arm64.deb Architecture Version)\" = "
                + "\"$(printf 'Architecture: arm64\\nVersion: %s' $(dpkg-query -W -f '${Version}' $p))\" ] || exit 1; done",
            "rpm" => $"for p in thermi thermi-sensors; do [ \"$(rpm -qp --qf '%{{ARCH}} %{{VERSION}}' {Container.Packages}/$p-[0-9]*.aarch64.rpm)\" = "
                + "\"aarch64 $(rpm -q --qf '%{VERSION}' $p)\" ] || exit 1; done",
            _ => $"grep -q aarch64 {Container.Packages}/aur/PKGBUILD",
        };

        public string Version(string package) => Format == "deb" ? $"dpkg-query -W -f '${{Version}}' {package}" : $"rpm -q --qf '%{{VERSION}}' {package}";

        public string Update() => Format == "deb" ? "apt-get update && apt-get upgrade -y" : "dnf upgrade -y --refresh";

        /// <summary>What the system's updater says of a repository whose signature it refused.</summary>
        public string Unsigned => Format == "deb" ? "NO_PUBKEY|is not signed" : "(?i)signature";

        /// <summary>The Date of the channel's Release apt holds, read last; dnf keeps no such thing to compare.</summary>
        public string? HeldRelease => Format == "deb" ? "sed -n 's/^Date: //p' /var/lib/apt/lists/*_dists_stable_InRelease" : null;

        // The repository the package added, pointed at the levers' channel the test serves.
        public string Served() => Format == "deb"
            ? $"sed -i 's|^URIs:.*|URIs: file://{Container.Work}/channel/apt|' /etc/apt/sources.list.d/thermi.sources"
            : $"sed -i 's|^baseurl=.*|baseurl=file://{Container.Work}/channel/rpm/stable|' /etc/yum.repos.d/thermi.repo";
    }
}
