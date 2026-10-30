#!/bin/sh
# Remove the service from the Pi. The overlay filesystem must be OFF. Boot settings are left alone.
set -eu

sudo systemctl disable --now gopro-backpack 2>/dev/null || true
sudo rm -f /etc/systemd/system/gopro-backpack.service \
           /etc/systemd/system.conf.d/watchdog.conf \
           /etc/systemd/journald.conf.d/volatile.conf
sudo rm -rf /opt/gopro-backpack
sudo systemctl daemon-reload
echo "Removed. Site config kept in /etc/gopro-backpack; delete it by hand if you no longer need it."
