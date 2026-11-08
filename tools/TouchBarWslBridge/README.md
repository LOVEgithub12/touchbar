# Touch Bar WSL bridge diagnostic

This directory preserves TonyTouchBar's GPL-2.0-only Linux/libusb bridge, with a local `--test-frame` diagnostic. It does not install a driver or attach USB devices. A separately prepared WSL distribution and USB forwarding of **05ac:8302 only** are prerequisites.

## Source and license

- Upstream: https://github.com/widmonstertony/TonyTouchBar
- Fixed commit: `3434f67b1c156b4b7b8530db4324cb2c9e64a3c4`
- Original file: https://raw.githubusercontent.com/widmonstertony/TonyTouchBar/3434f67b1c156b4b7b8530db4324cb2c9e64a3c4/bridge/t2-touchbar-bridge.c
- Original SHA256: `3DE2FC88119D81F64DC29F70DF1D9B8CE99301A2B74C5BC34D0CE79D19DA1CEA`
- Original is preserved as `upstream-t2-touchbar-bridge.c`; local modified source is `t2-touchbar-bridge.c`.
- License: `LICENSE`, GPL-2.0-only, preserved from the same commit. Its SHA256 is `8177F97513213526DF2CF6184D8FF986C675AFB514D4E68A404010521B880643`.
- Protocol credit remains in the source: Kerem Karabay and Linux appletbdrm contributors.

Local additions: diagnostic color-bar/checkerboard image; maximum geometry 4096 × 256; GINF pixel-format validation; UDCL response-header validation; opening-error details; an atomic touch-thread shutdown flag. The existing bridge protocol and ordinary touch reporting are retained.

## Build inside the prepared Linux distribution

With a C compiler, libusb development headers/library, and pthreads available:

```sh
cc -std=c11 -O2 -Wall -Wextra -Werror -pthread t2-touchbar-bridge.c -lusb-1.0 -o t2-touchbar-bridge
./t2-touchbar-bridge --help
```

Help does not open a device. Run the hardware test only after forwarding the exact Touch Bar USB device:

```sh
./t2-touchbar-bridge --test-frame
```

The test selects display configuration 2, opens the display interface, validates GINF geometry/format, sends REDY/CLDR, submits an opaque image, and requires a valid UDCL acknowledgement for timestamp 1. It succeeds only when it prints:

```text
TEST_FRAME_ACK timestamp=1 width=<actual width> height=<actual height> pattern=color-bars-and-checkerboard
```

Exit 0 means device acknowledgement was received. Exit 11 means opening/handshake failed; exit 12 means frame allocation, transfer, or acknowledgement failed; exit 64 means invalid arguments. `READY` by itself proves startup only. A person must also confirm the display actually shows eight colored bands above a black/white checker strip; neither software output nor an image preview substitutes for that check.

The test does not send keyboard events. Closing the bridge does not restore the Windows driver binding: detach the USB device from WSL and use the established Apple restoration procedure as needed.

## Ordinary bridge protocol

Run without options as a subprocess. Windows communicates using redirected standard streams, not a network listener.

- stderr startup: `READY <width> <height>` after successful GINF/REDY/CLDR.
- stdin frame: ASCII `FRM1`, four-byte little-endian RGBA byte length, then width × height × 4 bytes of row-major RGBA8888 pixels.
- stdout acknowledgement: ASCII `ACK1`, then eight-byte little-endian timestamp. The first timestamp is 1. It is written only after a matching device UDCL reply.
- stderr touch: `TOUCH <x> <y> DOWN`, `MOVE`, or `UP`; coordinates are in display pixels.
- stdin shutdown: eight-byte header beginning with `QUIT` (for example `QUIT` plus four zero bytes), or EOF.

Drain stderr while reading stdout; otherwise process pipes can block. Once the whole Touch Bar is forwarded to WSL, the existing Windows HID reader cannot own it. Any application adapter must consume bridge touch events. The existing Windows app's RGBA Skia frame buffer can be sent directly; its already rotated three-byte DFR `PixelArea` cannot be sent as RGBA.

This directory was prepared and statically reviewed without accessing hardware. Record actual build and test results separately.
