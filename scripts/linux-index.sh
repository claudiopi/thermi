#!/bin/sh
# A channel's apt and dnf indexes, unsigned, by Debian's apt-ftparchive and createrepo_c, over a folder laid out as the
# repositories are that holds only the packages the channel offers; scripts/repository.ps1 runs it and signs them.
# Usage: linux-index.sh <folder> <channel> <seconds the Release is valid for>.
set -eu
root=$1 channel=$2 valid=$3

# An offer of nothing, as a disabled channel's, still has its folders.
mkdir -p "$root/apt/pool/$channel"
cd "$root/apt"
for arch in amd64 arm64; do
    dir="dists/$channel/main/binary-$arch"
    mkdir -p "$dir"
    apt-ftparchive --arch "$arch" packages "pool/$channel" > "$dir/Packages"
    gzip -9 -n -k -f "$dir/Packages"
    # Each by its hash too, as the Release says apt may fetch it: a PC that read the offer before finds its own still.
    mkdir -p "$dir/by-hash/SHA256"
    for file in "$dir/Packages" "$dir/Packages.gz"; do cp "$file" "$dir/by-hash/SHA256/$(sha256sum "$file" | cut -d ' ' -f 1)"; done
done
# Its Date is now and its Valid-Until that much later, which apt compares: a PC refuses a Release past its Valid-Until,
# or older than the one it holds.
apt-ftparchive \
    -o APT::FTPArchive::Release::Origin=thermi -o APT::FTPArchive::Release::Label=thermi \
    -o "APT::FTPArchive::Release::Suite=$channel" -o "APT::FTPArchive::Release::Codename=$channel" \
    -o "APT::FTPArchive::Release::Architectures=amd64 arm64" -o APT::FTPArchive::Release::Components=main \
    -o "APT::FTPArchive::Release::Description=thermi's $channel channel" -o APT::FTPArchive::Release::Acquire-By-Hash=yes \
    -o "APT::FTPArchive::Release::ValidTime=$valid" \
    release "dists/$channel" > Release
mv Release "dists/$channel/Release"

mkdir -p "$root/rpm/$channel/packages"
createrepo_c --quiet "$root/rpm/$channel"
