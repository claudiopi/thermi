#!/bin/sh
# A channel's apt and dnf indexes, unsigned, by Debian's apt-ftparchive and createrepo_c, over a folder laid out as the
# repositories are that holds only the packages the channel offers; scripts/repository.ps1 runs it and signs them.
# Usage: linux-index.sh <folder> <channel> <date> <valid until>, both dates as RFC 2822.
set -eu
root=$1 channel=$2 date=$3 until=$4

cd "$root/apt"
mkdir -p "pool/$channel"
for arch in amd64 arm64; do
    dir="dists/$channel/main/binary-$arch"
    mkdir -p "$dir"
    apt-ftparchive --arch "$arch" packages "pool/$channel" > "$dir/Packages"
    gzip -9 -n -k -f "$dir/Packages"
done
# Date and Valid-Until are apt's to compare: a PC refuses a Release past its Valid-Until, or older than the one it holds.
apt-ftparchive \
    -o APT::FTPArchive::Release::Origin=thermi -o APT::FTPArchive::Release::Label=thermi \
    -o "APT::FTPArchive::Release::Suite=$channel" -o "APT::FTPArchive::Release::Codename=$channel" \
    -o "APT::FTPArchive::Release::Architectures=amd64 arm64" -o APT::FTPArchive::Release::Components=main \
    -o "APT::FTPArchive::Release::Description=thermi's $channel channel" \
    -o "APT::FTPArchive::Release::Date=$date" -o "APT::FTPArchive::Release::Valid-Until=$until" \
    release "dists/$channel" > Release
mv Release "dists/$channel/Release"

mkdir -p "$root/rpm/$channel/packages"
createrepo_c --quiet "$root/rpm/$channel"
