#!/bin/sh
# Install or update the service on the Pi. Run from the folder holding this script,
# with the publish output in ./publish. The overlay filesystem must be OFF.
set -eu

id gopro >/dev/null 2>&1 || sudo useradd --system --no-create-home --groups dialout gopro

sudo install -d /opt/gopro-backpack /etc/gopro-backpack
sudo cp -r publish/. /opt/gopro-backpack/
sudo chmod +x /opt/gopro-backpack/GoProBackpack
[ -f /etc/gopro-backpack/appsettings.json ] || sudo cp publish/appsettings.json /etc/gopro-backpack/

sudo install -D -m 644 systemd/gopro-backpack.service /etc/systemd/system/gopro-backpack.service
sudo install -D -m 644 systemd/system.conf.d/watchdog.conf /etc/systemd/system.conf.d/watchdog.conf
sudo install -D -m 644 journald.conf.d/volatile.conf /etc/systemd/journald.conf.d/volatile.conf

sudo systemctl daemon-reload
sudo systemctl enable gopro-backpack
sudo systemctl restart gopro-backpack
echo "Installed. Check /boot/firmware/config.txt (boot/config.txt.append) and cmdline.txt (no serial console)."
