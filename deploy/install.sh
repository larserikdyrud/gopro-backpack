#!/bin/sh
# Install or update the service on the Pi. Run from the folder holding this script,
# with the publish output in ./publish. The overlay filesystem must be OFF.
set -eu
cd "$(dirname "$0")"

if [ "$(findmnt -no FSTYPE /)" = "overlay" ]; then
    echo "The overlay filesystem is on, so nothing installed now would survive a reboot." >&2
    echo "Run: sudo raspi-config nonint disable_overlayfs && sudo reboot   then run this again." >&2
    exit 1
fi

if [ ! -f publish/GoProBackpack ]; then
    echo "publish/GoProBackpack not found. Publish for linux-arm64 into deploy/publish first." >&2
    exit 1
fi

id gopro >/dev/null 2>&1 || sudo useradd --system --no-create-home --groups dialout gopro

sudo systemctl stop gopro-backpack 2>/dev/null || true
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

# Boot settings are not changed automatically; warn if they are missing.
warn=0
for line in dtparam=uart0=on usb_max_current_enable=1 dtparam=watchdog=on; do
    if ! grep -q "^$line" /boot/firmware/config.txt; then
        echo "WARNING: $line is missing from /boot/firmware/config.txt (see boot/config.txt.append)" >&2
        warn=1
    fi
done
if grep -qE "console=(serial0|ttyAMA0)" /boot/firmware/cmdline.txt; then
    echo "WARNING: /boot/firmware/cmdline.txt still has a serial console on the UART; remove console=serial0,115200" >&2
    warn=1
fi

echo "Installed $(stat -c %y /opt/gopro-backpack/GoProBackpack | cut -d. -f1)."
[ $warn -eq 0 ] || echo "Fix the warnings above, then reboot."
echo "Check it with: systemctl status gopro-backpack   and   journalctl -u gopro-backpack -f"
