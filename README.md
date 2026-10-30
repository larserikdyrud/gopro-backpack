# GoPro backpack

[![CI](https://github.com/larserikdyrud/gopro-backpack/actions/workflows/ci.yml/badge.svg)](https://github.com/larserikdyrud/gopro-backpack/actions/workflows/ci.yml)

A headless Raspberry Pi 5 "backpack" that lets a Radiomaster Boxer start and stop a GoPro HERO13/12 Black over ExpressLRS, and shows camera status on the radio as telemetry.

```
Boxer ⇄ ELRS receiver ⇄ UART (CRSF, 115200) ⇄ Pi 5 ⇄ USB-C (Open GoPro HTTP) ⇄ GoPro
```

The guiding rule: **rather film too much than too little.** No failure in the Pi, USB, radio or software may stop a recording. Only a deliberate switch movement from the operator does.

## Controls

| Channel | Boxer switch | Function |
| --- | --- | --- |
| CH6 | SA (2-pos) | REC: up = record, down (held 1 s, stable link) = stop |
| CH7 | SF (momentary, stretched to ≥ 0.5 s in EdgeTX) | MARK: HiLight in the current recording |

Start is level-triggered and retried every 2 s until the camera confirms, also during link loss. Stop needs an ON→OFF edge held for 1 s with a link that has been stable for 2 s. A switch moved while the link was down never stops the camera.

## Telemetry

The service sends two standard CRSF frames that EdgeTX understands without Lua:

- **Flight mode (0x21):** `REC 12:34`, `RDY 87m`, `START..`, `NO CAM`, plus warnings (`SD FULL`, `SD ERR`, `HOT`, `COLD`, `BUSY`, `STOP ERR`).
- **Battery (0x08):** camera battery % (`GPBt`), remaining SD minutes (`SDmn`), state code 0–3 (`Stat`: NOCAM, READY, REC, FAULT), Pi voltage (`PiV`).

## Repository layout

```
src/GoProBackpack/        .NET 9 worker service
  Crsf/                   CRSF framing, CRC8, RC channel unpacking, UART port
  Camera/                 Open GoPro client, camera locator, status polling
  Control/                RecordController (start/stop rules) and its loop
  Telemetry/              Frame encoding and publishing
  HealthWatchdog.cs       systemd watchdog pings
tests/GoProBackpack.Tests xUnit tests for the pure parts
deploy/                   deploy.ps1, install.sh/uninstall.sh, systemd unit, watchdog/journald drop-ins, config.txt lines
docs/getting-started.md   Step-by-step build, publish and install guide
```

## Status

Working and tested: CRSF framing and parsing, telemetry encoding, camera address discovery, camera polling, and `RecordController` with all six start/stop/mark rules. Its tests include randomized runs proving a stop is never sent without a valid stop gesture. Not yet run on hardware. Still to do:

- [ ] FM warning texts alternating with the main text
- [ ] Auto Power Down = Never (option id from camera capabilities)
- [ ] Open points: status 33 OK values, status 35 unit, ELRS failsafe behaviour on CRSF

## Quick start

**[docs/getting-started.md](docs/getting-started.md)** walks through everything step by step: tools, building, publishing, preparing the Pi, wiring, installing, setting up the receiver, camera and Boxer, checking each link, and locking the filesystem.

The short version, once the Pi is prepared (guide step 5):

```powershell
git clone https://github.com/larserikdyrud/gopro-backpack.git
cd gopro-backpack
dotnet test                                        # 57 tests, no hardware needed
./deploy/deploy.ps1 -Target pi@gopro-bp.local      # publish, copy, install on the Pi
```

Builds for the Pi are also attached to every CI run (artifact `gopro-backpack-linux-arm64`).

## Hardware

- Raspberry Pi 5 (1 GB), 5 V / 5 A supply
- ELRS 2.4 GHz receiver on GPIO14/15 (pins 8/10), CRSF at 115200 baud, Model Match on
- GoPro HERO13 Black (HERO12 Black accepted) with Labs firmware (`WAKE=2`, optional record-on-boot), battery always fitted
- Short USB-C data cable

## License

MIT, see [LICENSE](LICENSE).
