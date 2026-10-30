# Getting started

From a fresh clone to a backpack that boots, finds the camera and answers the Boxer. Do the steps in order: the Pi is prepared before the service is installed, and the filesystem is locked last.

1. [What you need](#1-what-you-need)
2. [Get the code](#2-get-the-code)
3. [Build and test on your PC](#3-build-and-test-on-your-pc)
4. [Publish for the Pi](#4-publish-for-the-pi)
5. [Prepare the Pi](#5-prepare-the-pi)
6. [Wire the receiver](#6-wire-the-receiver)
7. [Install the service](#7-install-the-service)
8. [Set up the receiver, camera and Boxer](#8-set-up-the-receiver-camera-and-boxer)
9. [Check that it works](#9-check-that-it-works)
10. [Lock the filesystem](#10-lock-the-filesystem)
11. [Update, configure and uninstall](#11-update-configure-and-uninstall)
12. [Troubleshooting](#12-troubleshooting)

## 1. What you need

**On your PC**

| Tool | Why | Get it |
| --- | --- | --- |
| .NET 9 SDK | Build, test, publish | `winget install Microsoft.DotNet.SDK.9` or [dot.net](https://dotnet.microsoft.com/download/dotnet/9.0) |
| Git | Clone and update | `winget install Git.Git` |
| OpenSSH client (`ssh`, `scp`) and `tar` | Copy to the Pi | Built into Windows 10/11, macOS and Linux |
| Raspberry Pi Imager | Write the SD card | [raspberrypi.com/software](https://www.raspberrypi.com/software/) |
| Visual Studio 2022 17.12+ (optional) | IDE and debugging | Any edition with the .NET workload |
| GitHub CLI `gh` (optional) | Download CI builds | `winget install GitHub.cli` |

Check the SDK with `dotnet --list-sdks`; you need a `9.0.x` line.

**Hardware**

- Raspberry Pi 5 (1 GB is enough) with a 5 V / 5 A USB-C supply
- microSD card, high endurance or industrial
- ELRS 2.4 GHz receiver with UART pads, on the same major firmware version as the Boxer's module
- Four wires and a soldering iron (or a connector) for receiver → Pi header
- GoPro HERO13 Black (HERO12 Black works too) with a battery fitted
- Short USB-C data cable (not charge-only)
- Radiomaster Boxer with internal ELRS

## 2. Get the code

```sh
git clone https://github.com/larserikdyrud/gopro-backpack.git
cd gopro-backpack
```

Without Git: on the GitHub page choose **Code → Download ZIP** and unpack it. You then update by downloading again instead of `git pull`.

If you only want to install a ready build and not compile anything, you still need the repo for the `deploy/` scripts; get the binaries from CI in [step 4](#option-b-download-a-build-from-ci).

## 3. Build and test on your PC

From the repo root:

```sh
dotnet build
dotnet test
```

`dotnet test` should end with `Passed! - Failed: 0`. The tests need no hardware: they cover the CRSF framing, telemetry encoding, camera address discovery and the start/stop rules.

**In Visual Studio:** open `GoProBackpack.sln`, build with Ctrl+Shift+B, and run the tests from **Test → Test Explorer**. The start/stop rules are in `src/GoProBackpack/Control/RecordController.cs` and their tests in `tests/GoProBackpack.Tests/RecordControllerTests.cs`.

Running the service itself on Windows is not useful: it needs `/dev/ttyAMA0` and the camera's USB network.

## 4. Publish for the Pi

The Pi needs a self-contained linux-arm64 build: one executable plus `libSystem.IO.Ports.Native.so`, with no .NET runtime installed on the Pi.

### Option A: publish yourself

```sh
dotnet publish src/GoProBackpack -c Release -r linux-arm64 -o deploy/publish
```

`deploy/publish/` should now hold `GoProBackpack` (about 80 MB), `libSystem.IO.Ports.Native.so` and `appsettings.json`. The folder is git-ignored. Always copy the whole folder, never just the executable.

### Option B: download a build from CI

Every push to `main` builds the Pi version. On GitHub open **Actions → CI → the latest green run → Artifacts → gopro-backpack-linux-arm64**, download the zip and unpack it into `deploy/publish/`. Or with the GitHub CLI:

```sh
gh run download --repo larserikdyrud/gopro-backpack --name gopro-backpack-linux-arm64 --dir deploy/publish
```

Zip files lose the executable bit; `install.sh` sets it again.

## 5. Prepare the Pi

### 5.1 Write the SD card

1. If you have no SSH key yet, make one on your PC: `ssh-keygen -t ed25519` (press Enter for the defaults). The public key is in `~/.ssh/id_ed25519.pub` (`%USERPROFILE%\.ssh\id_ed25519.pub` on Windows).
2. In Raspberry Pi Imager choose **Raspberry Pi 5**, **Raspberry Pi OS Lite (64-bit)** and your SD card.
3. Under **Edit settings** (OS customisation):
   - Hostname: `gopro-bp`
   - Username and password: your choice (the examples below use `pi`)
   - Wi-Fi: your phone's hotspot or workshop network. It is only for maintenance; the backpack never needs a network in use.
   - Services: enable SSH, **public-key authentication only**, and paste your public key.
4. Write the card, put it in the Pi, power on and wait a minute or two.

```sh
ssh pi@gopro-bp.local
```

If `gopro-bp.local` does not resolve, use the Pi's IP address from your router or hotspot.

### 5.2 Update the OS

```sh
sudo apt update && sudo apt full-upgrade -y && sudo reboot
```

### 5.3 Enable the UART, USB current and watchdog

Append the lines from `deploy/boot/config.txt.append` to the boot config:

```sh
sudo nano /boot/firmware/config.txt
```

Add at the end:

```ini
[all]
dtparam=uart0=on            # UART0 (ttyAMA0) on GPIO14/15
usb_max_current_enable=1    # removes the 600 mA per-port USB limit
dtparam=watchdog=on
```

Only set `usb_max_current_enable=1` with a supply that really delivers 5 A; otherwise the Pi may brown out when the camera charges.

### 5.4 Remove the serial console

The UART belongs to CRSF, so Linux must not put a login console on it.

```sh
sudo nano /boot/firmware/cmdline.txt
```

It is one single line. Delete `console=serial0,115200` (and any `console=ttyAMA0,...`) and keep everything else, including `console=tty1`, on that one line. Alternatively use `sudo raspi-config` → **Interface Options → Serial Port**: answer **No** to a login shell over serial and **Yes** to enabling the serial hardware.

### 5.5 Power supply limit (only if needed)

If your supply is not the official 27 W one and does not negotiate 5 A by itself, tell the bootloader:

```sh
sudo rpi-eeprom-config --edit
```

Add `PSU_MAX_CURRENT=5000`, save and exit.

### 5.6 Reboot and check

```sh
sudo reboot
# after reconnecting:
ls -l /dev/ttyAMA0                 # should exist, group dialout
grep -c "console=serial0" /proc/cmdline   # should print 0
ls /dev/watchdog                   # should exist
```

## 6. Wire the receiver

| Receiver pad | Pi 5 header | Function |
| --- | --- | --- |
| TX | Pin 10 (GPIO15, RXD) | Channels into the Pi |
| RX | Pin 8 (GPIO14, TXD) | Telemetry out of the Pi |
| 5V | Pin 2 or 4 | Power (check the receiver accepts 5 V) |
| GND | Pin 6 | Common ground |

TX goes to RX and RX to TX. CRSF needs both wires: full duplex, non-inverted. Power the Pi off while wiring.

## 7. Install the service

The overlay filesystem must be **off** while installing; on a fresh image it is. `install.sh` checks this and refuses to run otherwise.

### Windows (PowerShell), one command

```powershell
./deploy/deploy.ps1 -Target pi@gopro-bp.local
```

This publishes (step 4, option A), packs `deploy/` into one archive, copies it to the Pi and runs `install.sh` there. Add `-SkipPublish` if you already put a build in `deploy/publish` (for example from CI).

### macOS, Linux or by hand

```sh
dotnet publish src/GoProBackpack -c Release -r linux-arm64 -o deploy/publish
tar -czf /tmp/gpb.tgz -C deploy .
scp /tmp/gpb.tgz pi@gopro-bp.local:/tmp/
ssh pi@gopro-bp.local 'rm -rf ~/gopro-backpack && mkdir ~/gopro-backpack && tar -xzf /tmp/gpb.tgz -C ~/gopro-backpack && sh ~/gopro-backpack/install.sh'
```

### What `install.sh` does

1. Creates a system user `gopro` in the `dialout` group (access to the UART).
2. Copies the build to `/opt/gopro-backpack/`.
3. Copies `appsettings.json` to `/etc/gopro-backpack/` the first time only, so your settings survive updates.
4. Installs the systemd unit (`Restart=always`, `WatchdogSec=10`, no start limit), the hardware watchdog drop-in (`RuntimeWatchdogSec=10s`) and the journald drop-in (logs in RAM).
5. Enables and starts `gopro-backpack`, then warns if the boot settings from steps 5.3 and 5.4 are missing.

If it printed warnings, fix them and `sudo reboot`. The watchdog drop-in only takes effect after a reboot.

## 8. Set up the receiver, camera and Boxer

The design doc has the reasoning; this is the checklist.

### ELRS receiver (WebUI or ELRS Lua)

| Setting | Value |
| --- | --- |
| Serial Protocol | CRSF |
| UART baud (Options tab) | 115200 |
| Bind phrase | Same as the Boxer |
| Model Match | On, with its own model ID |

### GoPro

1. Update to the latest regular firmware, then install the **Labs** firmware for your model.
2. Create the Labs QR code in the Labs Blockly tool and show it to the camera once. Starting point: `*WAKE=2*TUSB=1*FAST=1*BOOT="!Lbt"!SAVEbt=>u0!S` (wake on any power, trust USB power, start recording on boot). Do **not** use the old USB Power Trigger, which stops recording when USB power drops.
3. In the camera menus: USB connection = **GoPro Connect** (not MTP), Auto Power Down = **Never**, choose a fixed video preset.
4. SD card V30 or better, formatted in the camera. Keep the battery in at all times.
5. Plug the camera into a USB port on the Pi.

### Boxer (EdgeTX model "GoPro")

1. **Internal RF:** CRSF/ELRS, Receiver ID = the Model Match ID. Packet rate 50 Hz, telemetry ratio 1:4, switch mode Hybrid. Leave CH5 unused.
2. **Mixer:** CH6 = SA (REC). For MARK, make logical switch **L6**: function `OR`, V1 = the spring-loaded button pressed (SF on the doc's layout, check yours), Duration = 0.5 s. Then CH7 = L6. The duration makes sure a quick tap survives ELRS's round-robin channel updates.
3. **Telemetry:** with the backpack running (step 9), choose **Discover new sensors**, then rename them: `Bat%` → `GPBt`, `Capa` → `SDmn`, `Curr` → `Stat`, `RxBt` → `PiV`.
4. **Alarms** (logical switches plus special functions):

| Logical switch | Condition | Special function |
| --- | --- | --- |
| L1 | `a~x` Stat ≈ 2.0 (or `Stat > 1.5` AND `Stat < 2.5`) | Sound + haptic on change: "recording" / "stopped" |
| L2 | `Stat > 2.5` | Alarm, repeat every 10 s |
| L3 | `Stat < 0.5` | "No camera", repeat every 30 s |
| L4 | `GPBt < 20` | Low camera battery |
| L5 | `SDmn < 10` | Low SD space |

Put FM, Stat, GPBt, SDmn and RQly on a telemetry screen.

## 9. Check that it works

Work from the bottom of the chain up; each check depends on the one before.

**1. Receiver frames reach the Pi.** Turn on the Boxer, then on the Pi:

```sh
sudo systemctl stop gopro-backpack
stty -F /dev/ttyAMA0 115200 raw -echo
timeout 2 cat /dev/ttyAMA0 | od -An -tx1 | head
sudo systemctl start gopro-backpack
```

You should see many `c8` bytes (the CRSF sync byte). Nothing at all: check wiring (TX↔RX swapped?), baud rate and that the receiver is bound. Then turn the Boxer **off** and repeat: the output should stop. If frames keep coming with the radio off, the receiver repeats its last values on link loss and the link-loss logic will not work; note this as an open point.

**2. The camera's USB network is up.**

```sh
ip -4 addr | grep 172.2
```

You should see an address like `172.24.156.50`. The camera is the same address ending in `.51`:

```sh
curl -s http://172.24.156.51:8080/gopro/camera/state | head -c 300; echo
```

A JSON reply with `"status"` means the camera answers. This is also how you answer the open points: look at status `33` (SD), `35` (remaining time) and `10` (recording).

**3. The service is healthy.**

```sh
systemctl status gopro-backpack
journalctl -u gopro-backpack -f
```

Look for `Opened /dev/ttyAMA0 at 115200 baud` and `Camera connected at 172.2x.1yz.51`. Repeated `Withholding watchdog ping` lines mean a loop is stuck.

**4. The Boxer sees telemetry.** FM shows `RDY 87m` (or `NO CAM` without a camera). Flip SA up: within 2 s FM shows `START..` then `REC 00:01`, and L1 sounds. Flip SA down and hold 1 s: recording stops.

**5. Run the acceptance tests.** Tests T1–T20 in the design doc (section 10) cover cold start, link loss, USB unplug, crashes, watchdog and range. Do them on the bench first, then in the field.

## 10. Lock the filesystem

When everything works, make the root filesystem read-only so power cuts cannot corrupt the SD card:

```sh
sudo raspi-config nonint enable_overlayfs
sudo raspi-config nonint enable_bootro
sudo reboot
```

Or use `sudo raspi-config` → **Performance Options → Overlay File System** (yes to both questions). Check with `findmnt -no FSTYPE /`: it prints `overlay` when locked. From now on every change made on the Pi is lost at reboot, which is the point.

## 11. Update, configure and uninstall

### Update the software

```sh
git pull                                    # on your PC
ssh pi@gopro-bp.local 'sudo raspi-config nonint disable_overlayfs && sudo raspi-config nonint disable_bootro && sudo reboot'
./deploy/deploy.ps1 -Target pi@gopro-bp.local
ssh pi@gopro-bp.local 'sudo raspi-config nonint enable_overlayfs && sudo raspi-config nonint enable_bootro && sudo reboot'
```

### Change settings

Settings live in `/etc/gopro-backpack/appsettings.json`, under `"Backpack"`. With the overlay off:

```sh
sudo nano /etc/gopro-backpack/appsettings.json
sudo systemctl restart gopro-backpack
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `SerialPort`, `BaudRate` | `/dev/ttyAMA0`, `115200` | UART to the receiver |
| `RecChannel`, `MarkChannel` | `6`, `7` | EdgeTX channel numbers |
| `OnThreshold`, `OffThreshold` | `1400`, `600` | Switch hysteresis (CRSF 172–1811) |
| `LinkTimeout`, `LinkStableTime` | `0.5 s`, `2 s` | Link lost / stable enough to stop |
| `StopHoldTime` | `1 s` | How long REC must stay off to stop |
| `StartRetryInterval`, `StopRetryInterval`, `MaxStopAttempts` | `2 s`, `1 s`, `3` | Retries |
| `CameraSerial` | `null` | Fallback if the USB interface cannot be found: the camera's serial number |

Times use the format `hh:mm:ss.fff`, for example `"00:00:00.500"`.

### Uninstall

With the overlay off: `sh ~/gopro-backpack/uninstall.sh`. It removes the service and its systemd files but leaves `/etc/gopro-backpack` and the boot settings alone.

## 12. Troubleshooting

| Symptom | Likely cause | Check / fix |
| --- | --- | --- |
| `install.sh` says the overlay is on | Filesystem locked | `sudo raspi-config nonint disable_overlayfs && sudo reboot` |
| Service restarts every 2 s, log says it cannot open `/dev/ttyAMA0` | UART not enabled, or console still on it | Steps 5.3 and 5.4, then reboot |
| No `c8` bytes from the UART | Wiring or receiver config | TX↔RX, common ground, CRSF at 115200, receiver bound |
| FM never appears on the Boxer | Telemetry path | Pi TXD → receiver RX wire, telemetry ratio not Off, sensors discovered |
| `NO CAM` forever | USB network or camera mode | Data cable (not charge-only), USB mode GoPro Connect, `ip -4 addr` shows 172.2x, `curl` the camera |
| Camera stops answering after boot | Webcam mode | The service sends `webcam/exit` by itself; if it keeps happening, check the log and the design doc's open points |
| Camera falls asleep | Auto Power Down not Never | Set it in the camera menu (the service does not set it yet) |
| Pi reboots or `vcgencmd get_throttled` is not `0x0` | Power | 5 A supply, `PSU_MAX_CURRENT=5000`, shorter cable |
| Changes vanish after reboot | Overlay on | Expected; turn it off to change anything (step 11) |
