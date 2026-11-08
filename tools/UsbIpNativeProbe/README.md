# Native local USB/IP display probe

This .NET 6 diagnostic connects to the existing local usbipd server at **127.0.0.1:3240**. It does not need WSL or a virtual machine. It installs no driver and changes no system settings itself. IMPORT temporarily transfers ownership of the specified USB device to the server's already-installed VBoxUSB driver; closing the connection causes usbipd to release ownership.

`UsbIpClient.cs` is a reusable, sequential USB/IP 1.1.1 client. It first reads DEVLIST to validate the exact command-line bus ID as Apple **05ac:8302**, then validates the IMPORT response again. A bus ID must come from the current `usbipd list` output. It does not discover or attach another device automatically.

```powershell
$probe = Join-Path (Get-Location) 'tools\UsbIpNativeProbe\bin\Release\net6.0\UsbIpNativeProbe.exe'
& $probe --busid 1-6 --test-frame
```

The example bus ID must be rechecked if the machine's device enumeration changes. With no arguments or `--help`, the program does not connect to anything.

## Scope and acceptance

The probe obtains the current USB configuration, selects configuration 0 then 2 when changing mode, and always sends SET_CONFIGURATION 2 so VBoxUSB establishes endpoint handles. It selects interface 1, alternate 0, exchanges GINF, checks geometry/format, sends REDY/CLDR, and sends one color-bar/checkerboard image. It succeeds only on a matching UDCL header, message key and 64-bit timestamp of 1.

Success is JSON with `Stage: "TEST_FRAME_ACK"`. This verifies device acknowledgement; a person must still confirm that the physical Touch Bar shows the image. Any exception prints the exact failing stage, exits 1, and disposes the network connection. No keys or other application actions are sent.

Every socket operation has a 10-second timeout; import connect has a 10-second cancellation deadline. Transfers are bounded at 4 MiB. Reply version/opcode, command/sequence, counts, lengths and non-isochronous shape are checked. Server status errors and short OUT writes fail explicitly. It sends only one request at a time. Do not use the same client concurrently from display and touch threads without adding a proper response dispatcher.

The server's RET_SUBMIT intentionally contains zero `devid`, `direction` and `ep` fields; these are not incorrectly required to echo the request. Endpoint direction is tracked from the outstanding request. Linux USB/IP numbers endpoints without the direction bit in `ep`.

Closing a session is not guaranteed to restore the original Apple binding when the device was force-bound beforehand. The controlling task must inspect `usbipd state`, unbind if appropriate, and execute the verified Apple restoration procedure after a failed test.

## Validated sequence on this machine

The initial path that explicitly selected interface 1 alt 0 stalled on the first bulk write. The following bounded alternative succeeded:

```powershell
& $probe --busid 1-6 --test-frame --skip-set-interface --clear-pipes --hold-seconds 60
```

This reads and validates the actual descriptors, sets configuration 0 then 2, omits SET_INTERFACE, and clears endpoint halts for OUT 0x01 and IN 0x82. Omitting SET_INTERFACE follows the upstream libusb claim sequence; clearing pipes follows the original kernel driver's startup reset. These changes were applied together, so the test does not establish which one alone resolves the earlier stall.

The first display response is a 32-byte REDY event, followed by the 65-byte GINF response. After sending the frame, a 16-byte event arrives before the 40-byte UDCL acknowledgement. The probe therefore accepts a matching response within at most four reads and logs the other replies.

On 2026-11-08 it detected **2008 × 60**, sent a **361584-byte** frame and received the matching timestamp-1 ACK. The user initially saw black after the test had already disconnected. Keeping the same connection open with `--hold-seconds 60` allowed the user to confirm seeing the color bars and checkerboard. A running display application must retain USB ownership; an exited probe is not a persistent display host. Evidence is in `../../verification-usbip-20261108`.

## Source references and licensing

USB/IP framing is implemented from the public protocol specification:

- https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html
- Official server v5.3.0 `Usbipd/Interop/UsbIp.cs`: version 0x0111, operation codes, 48-byte URB header, network byte order.
- Official server v5.3.0 `Usbipd/ExportedDevice.cs`: 312-byte device descriptor and optional interface list.
- Official server v5.3.0 `Usbipd/AttachedEndpoint.cs`: SET_CONFIGURATION/SET_INTERFACE interception through VBoxUSB and RET_SUBMIT semantics.
- Official server v5.3.0 `Usbipd/Tools.cs`: control/bulk/interrupt endpoint type selection.

Read-only copies of those official GPL-3.0-only reference files are under `reference/` with their original headers; they are excluded from compilation. The client is newly written protocol code and does not compile or link those server sources.

Display packet composition is adapted from the separately preserved GPL-2.0-only TonyTouchBar bridge at commit `3434f67b1c156b4b7b8530db4324cb2c9e64a3c4`, credited to Kerem Karabay and Linux appletbdrm contributors. This probe is provided under GPL-2.0-only; see `LICENSE`. The original bridge and source hash are in `../TouchBarWslBridge/`.

Build was completed with .NET SDK 6.0.428, zero warnings/errors. Only `--help` was executed by the preparing agent; hardware results must be recorded by the controlling task.
