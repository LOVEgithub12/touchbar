using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using WindowsTouchBar.Interop;

namespace WindowsTouchBar.Device;

/// <summary>
/// User-mode replacement for the DFRDisplayKm kernel driver: it speaks the exact same
/// iBridge display wire protocol, but reaches the bulk pipes through WinUSB so no
/// third-party kernel code has to be signed.
/// </summary>
sealed class DfrTransport : IDisposable
{
    private const uint DfrDeviceRequestHeader = 0x15120002;
    private const uint DfrDeviceResponseHeader = 0x01140000;
    private const uint DfrUpdateFbRequestHeader = 0x00120002;
    private const uint DfrUpdateFbRequestReserved1 = 0x00000009;
    private const uint DfrUpdateFbContentReserved0 = 0x00000001;
    private const uint DfrGenericRequestEnd = 0x00000010;

    private const uint DfrGetInfoKey = 0x47494e46;             // GINF
    private const uint DfrHostReadyKey = 0x52454459;           // REDY
    private const uint DfrClearScreenKey = 0x434c4452;         // CLDR
    private const uint DfrFramebufferUpdatedKey = 0x5544434c;  // UDCL

    public const uint FramebufferFormat = 0x52474241;          // ABGR, alpha is not transmitted
    public const int PixelBytes = 3;

    // Each failed read already waits for a USB timeout. Do not turn a missing display
    // into several minutes of an apparently hung startup.
    private const int GinfRetryMax = 4;
    private const int FrameRetransmitMax = 2;

    // DFR_GENERIC_REQUEST: 16-byte header + 16-byte content
    private const int GenericRequestSize = 32;
    // DFR_UPDATE_FB_REQUEST: 16-byte header + 44-byte content
    private const int FbRequestHeaderSize = 60;
    // DFR_INFORMATION_RESPONSE and DFR_FRAMEBUFFER_UPDATE_RESPONSE, both pack(1)
    private const int InfoResponseSize = 65;
    private const int FbAckResponseSize = 40;

    // DFR_UPDATE_FB_REQUEST_CONTENT field offsets (pack=1)
    private const int ContentReserved0Offset = 16;
    private const int ContentFrameIdOffset = 18;
    private const int ContentBeginXOffset = 48;
    private const int ContentBeginYOffset = 50;
    private const int ContentWidthOffset = 52;
    private const int ContentHeightOffset = 54;
    private const int ContentBufferSizeOffset = 56;

    private const uint PIPE_TRANSFER_TIMEOUT = 0x03;
    private const uint ALLOW_PARTIAL_READS = 0x05;

    private const uint USBD_PIPE_TYPE_BULK = 2;

    // DfrUpdatePadding from DfrDisplay.c - the trailing bytes macOS appends to every frame.
    private static readonly byte[] UpdatePadding =
    {
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x08, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x08, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    // winusb.h WINUSB_PIPE_INFORMATION. Field order plus natural alignment reproduces the
    // native layout (PipeType@0, PipeId@4, padding@5, MaximumPacketSize@6, Interval@8).
    // Pack=1 here would not match, and winusb.sys writes the whole 12 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct WINUSB_PIPE_INFORMATION
    {
        public uint PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(IntPtr deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, out USB_INTERFACE_DESCRIPTOR descriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WINUSB_PIPE_INFORMATION pipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint policyValue);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ResetPipe(IntPtr interfaceHandle, byte pipeId);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_AbortPipe(IntPtr interfaceHandle, byte pipeId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEventW(IntPtr eventAttributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(IntPtr eventHandle);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_GetOverlappedResult(IntPtr interfaceHandle, IntPtr overlapped, out uint lengthTransferred, bool wait);

    // OVERLAPPED is four pointer-sized slots: Internal, InternalHigh, the Offset/Pointer union,
    // then hEvent - 32 bytes with the event at +24 on x64, 16 with the event at +12 on x86.
    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public UIntPtr Internal;
        public UIntPtr InternalHigh;
        public IntPtr Pointer;
        public IntPtr hEvent;
    }

    private const int ERROR_OPERATION_ABORTED = 995;
    private const int ERROR_IO_INCOMPLETE = 996;
    private const int ERROR_IO_PENDING = 997;
    private const int ERROR_SEM_TIMEOUT = 121;
    private const uint WAIT_TIMEOUT = 258;

    private const uint WriteTimeoutMs = 3000;
    private const uint ReadTimeoutMs = 1000;
    private const uint AbortGraceMs = 3000;
    // The pipe policy aborts a stalled transfer, but a request that never gets its completion
    // back would hang the caller, so every wait has its own ceiling above the policy.
    private const uint WriteWaitMs = WriteTimeoutMs + 2 * AbortGraceMs;
    private const uint ReadWaitMs = ReadTimeoutMs + 2 * AbortGraceMs;

    private static readonly int OverlappedSize = Marshal.SizeOf<OVERLAPPED>();
    private static readonly int OverlappedEventOffset = (int) Marshal.OffsetOf(typeof(OVERLAPPED), nameof(OVERLAPPED.hEvent));

    private readonly IntPtr _fileHandle;
    private readonly IntPtr _interfaceHandle;
    private readonly IntPtr _eventHandle;
    private readonly byte _pipeIn;
    private readonly byte _pipeOut;
    private readonly byte[] _requestBuf = new byte[GenericRequestSize];
    private readonly byte[] _infoBuf = new byte[InfoResponseSize];
    private readonly byte[] _ackBuf = new byte[FbAckResponseSize];
    // An overlapped request keeps using the buffer after the call returns, so every payload the
    // transport touches is pinned once for its whole lifetime rather than per transfer.
    private readonly GCHandle _requestPin;
    private readonly GCHandle _infoPin;
    private readonly GCHandle _ackPin;
    private readonly IntPtr _requestPtr;
    private readonly IntPtr _infoPtr;
    private readonly IntPtr _ackPtr;
    private byte[]? _framePacket;
    private GCHandle _framePacketPin;
    private IntPtr _framePacketPtr;
    // Views invalidate from both the foreground tracker and the HID touch thread; the kernel
    // driver serialised those submits in its IO queue, so the packet needs a lock here.
    private readonly object _frameLock = new();
    private byte _currentFrameId;
    private bool _deviceReady;
    private bool _disposed;
    private IntPtr _pendingOverlapped;
    private string _lastTransferStage = "not started";

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The 3-bytes-per-pixel payload area of the standing frame packet; rendering into
    /// it costs no copy because the packet is submitted as-is.</summary>
    public Span<byte> PixelArea
    {
        get
        {
            ThrowIfUnavailable();
            return _framePacket!.AsSpan(FbRequestHeaderSize, Width * Height * PixelBytes);
        }
    }

    private void ThrowIfUnavailable()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DfrTransport));
        }
        if (_pendingOverlapped != IntPtr.Zero)
        {
            throw new IOException("A display transfer did not finish cancellation; restart WindowsTouchBar before retrying");
        }
    }

    private static GCHandle Pin(byte[] buffer, out IntPtr pointer)
    {
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        pointer = handle.AddrOfPinnedObject();
        return handle;
    }

    private DfrTransport(IntPtr fileHandle, IntPtr interfaceHandle, IntPtr eventHandle, byte pipeIn, byte pipeOut)
    {
        _fileHandle = fileHandle;
        _interfaceHandle = interfaceHandle;
        _eventHandle = eventHandle;
        _pipeIn = pipeIn;
        _pipeOut = pipeOut;
        _requestPin = Pin(_requestBuf, out _requestPtr);
        _infoPin = Pin(_infoBuf, out _infoPtr);
        _ackPin = Pin(_ackBuf, out _ackPtr);
    }

    public static DfrTransport Open(string deviceInterfacePath)
    {
        // winusb.sys refuses WinUsb_Initialize on a handle opened without FILE_FLAG_OVERLAPPED
        // (it fails with ERROR_INVALID_HANDLE), so the whole transport is issued asynchronously.
        IntPtr fileHandle = NativeMethods.CreateFile(
            deviceInterfacePath, NativeMethods.GenericRead | NativeMethods.GenericWrite, FileShare.ReadWrite,
            IntPtr.Zero, FileMode.Open, FileOptions.Asynchronous, IntPtr.Zero);
        if (fileHandle == new IntPtr(-1))
        {
            throw new IOException($"CreateFile on {deviceInterfacePath} failed (win32 {Marshal.GetLastWin32Error()})");
        }

        IntPtr interfaceHandle = IntPtr.Zero;
        IntPtr eventHandle = IntPtr.Zero;
        bool interfaceOwned = false;
        try
        {
            if (!WinUsb_Initialize(fileHandle, out interfaceHandle))
            {
                throw new IOException($"WinUsb_Initialize failed (win32 {Marshal.GetLastWin32Error()})");
            }
            interfaceOwned = true;

            eventHandle = CreateEventW(IntPtr.Zero, true, false, null);
            if (eventHandle == IntPtr.Zero)
            {
                throw new IOException($"CreateEvent failed (win32 {Marshal.GetLastWin32Error()})");
            }

            if (Marshal.SizeOf<WINUSB_PIPE_INFORMATION>() != 12)
            {
                throw new InvalidOperationException("WINUSB_PIPE_INFORMATION does not match the winusb.h layout");
            }

            if (OverlappedSize != 4 * IntPtr.Size || OverlappedEventOffset != 3 * IntPtr.Size)
            {
                throw new InvalidOperationException($"OVERLAPPED is laid out as {OverlappedSize}/{OverlappedEventOffset}, expected {4 * IntPtr.Size}/{3 * IntPtr.Size}");
            }

            if (!WinUsb_QueryInterfaceSettings(interfaceHandle, 0, out USB_INTERFACE_DESCRIPTOR interfaceDesc))
            {
                throw new IOException($"WinUsb_QueryInterfaceSettings failed (win32 {Marshal.GetLastWin32Error()})");
            }

            byte pipeIn = 0;
            byte pipeOut = 0;
            for (byte i = 0; i < interfaceDesc.bNumEndpoints; i++)
            {
                if (!WinUsb_QueryPipe(interfaceHandle, interfaceDesc.bAlternateSetting, i, out WINUSB_PIPE_INFORMATION pipe))
                {
                    throw new IOException($"WinUsb_QueryPipe({i}) failed (win32 {Marshal.GetLastWin32Error()})");
                }
                if (pipe.PipeType != USBD_PIPE_TYPE_BULK)
                {
                    continue;
                }
                if ((pipe.PipeId & 0x80) != 0)
                {
                    pipeIn = pipe.PipeId;
                }
                else
                {
                    pipeOut = pipe.PipeId;
                }
            }

            if (pipeIn == 0 || pipeOut == 0)
            {
                throw new InvalidOperationException($"Interface {interfaceDesc.bInterfaceNumber} exposes no bulk-IN/bulk-OUT pair");
            }

            uint writeTimeout = WriteTimeoutMs;
            uint readTimeout = ReadTimeoutMs;
            uint partialReads = 1;
            SetPipePolicy(interfaceHandle, pipeOut, PIPE_TRANSFER_TIMEOUT, writeTimeout);
            SetPipePolicy(interfaceHandle, pipeIn, PIPE_TRANSFER_TIMEOUT, readTimeout);
            SetPipePolicy(interfaceHandle, pipeIn, ALLOW_PARTIAL_READS, partialReads);

            // The driver resets both pipes in its D0Entry: it clears the data toggle and any
            // stall left behind by whichever driver held the interface before us.
            if (!WinUsb_ResetPipe(interfaceHandle, pipeOut) || !WinUsb_ResetPipe(interfaceHandle, pipeIn))
            {
                throw new IOException($"WinUsb_ResetPipe failed (win32 {Marshal.GetLastWin32Error()})");
            }

            DfrTransport transport = new DfrTransport(fileHandle, interfaceHandle, eventHandle, pipeIn, pipeOut);
            // The handshake needs the pinned buffers, so the instance exists before the panel has
            // been proven usable; it owns the handles from here on and Dispose is the single path
            // that releases them.
            interfaceOwned = false;
            eventHandle = IntPtr.Zero;
            fileHandle = IntPtr.Zero;
            try
            {
                transport.Initialize();
            }
            catch
            {
                transport.Dispose();
                throw;
            }
            return transport;
        }
        catch
        {
            if (interfaceOwned)
            {
                WinUsb_Free(interfaceHandle);
            }
            if (eventHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(eventHandle);
            }
            NativeMethods.CloseHandle(fileHandle);
            throw;
        }
    }

    private void Initialize()
    {
        SendGenericRequest(DfrGetInfoKey);

        int retry = 0;
        while (true)
        {
            bool ok = Transfer(false, _pipeIn, _infoPtr, InfoResponseSize, ReadWaitMs, out uint transferred, out int win32);
            if (ok && transferred >= InfoResponseSize)
            {
                break;
            }
            // A timed-out read is the "the panel is not quite ready yet" case the driver retried
            // for; anything else (device gone, bad handle) will not fix itself by waiting.
            if (!ok && !IsTransferTimeout(win32))
            {
                throw new IOException($"Reading the GINF response failed (win32 {win32})");
            }
            if (retry++ >= GinfRetryMax)
            {
                throw new IOException($"The display never sent a complete GINF response ({transferred} of {InfoResponseSize} bytes, win32 {win32})");
            }
        }

        uint responseHeader = BinaryPrimitives.ReadUInt32LittleEndian(_infoBuf.AsSpan(0, 4));
        uint messageKey = BinaryPrimitives.ReadUInt32LittleEndian(_infoBuf.AsSpan(16, 4));
        uint reportedWidth = BinaryPrimitives.ReadUInt32LittleEndian(_infoBuf.AsSpan(32, 4));
        uint reportedHeight = BinaryPrimitives.ReadUInt32LittleEndian(_infoBuf.AsSpan(36, 4));
        uint pixelFormat = BinaryPrimitives.ReadUInt32LittleEndian(_infoBuf.AsSpan(53, 4));

        if (responseHeader != DfrDeviceResponseHeader || messageKey != DfrGetInfoKey)
        {
            throw new IOException($"The display returned an invalid GINF message (header 0x{responseHeader:X8}, key 0x{messageKey:X8})");
        }
        if (pixelFormat != FramebufferFormat)
        {
            throw new IOException($"The display reported an unsupported framebuffer format 0x{pixelFormat:X8}");
        }
        if (reportedWidth == 0 || reportedHeight == 0 || reportedWidth > 0xFFFF || reportedHeight > 0xFFFF)
        {
            throw new IOException($"The display reported an impossible size {reportedWidth}x{reportedHeight}");
        }

        Width = (int) reportedWidth;
        Height = (int) reportedHeight;
        _framePacket = new byte[checked(FbRequestHeaderSize + Width * Height * PixelBytes + UpdatePadding.Length)];
        UpdatePadding.CopyTo(_framePacket, FbRequestHeaderSize + Width * Height * PixelBytes);
        _framePacketPin = Pin(_framePacket, out _framePacketPtr);

        SendGenericRequest(DfrHostReadyKey);
        // CLDR is meant to clear the panel; the driver noted it does not really work and
        // sends it only because macOS does.
        SendGenericRequest(DfrClearScreenKey);

        _deviceReady = true;
    }

    private static bool IsTransferTimeout(int win32) =>
        win32 is ERROR_OPERATION_ABORTED or ERROR_SEM_TIMEOUT or (int) WAIT_TIMEOUT;

    private static void SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint value)
    {
        if (!WinUsb_SetPipePolicy(interfaceHandle, pipeId, policyType, sizeof(uint), ref value))
        {
            throw new IOException($"WinUsb_SetPipePolicy(0x{policyType:X2}) on pipe 0x{pipeId:X2} failed (win32 {Marshal.GetLastWin32Error()})");
        }
    }

    /// <summary>Issue one overlapped transfer and wait with a bounded cancellation path.</summary>
    private bool Transfer(bool toDevice, byte pipeId, IntPtr buffer, uint length, uint waitCeilingMs, out uint transferred, out int win32)
    {
        ThrowIfUnavailable();
        transferred = 0;
        win32 = 0;
        bool safeToFree = true;
        uint waitResult = uint.MaxValue;
        IntPtr overlapped = Marshal.AllocHGlobal(OverlappedSize);
        try
        {
            for (int i = 0; i < OverlappedSize; i++)
            {
                Marshal.WriteByte(overlapped, i, 0);
            }
            Marshal.WriteIntPtr(overlapped, OverlappedEventOffset, _eventHandle);
            if (!ResetEvent(_eventHandle))
            {
                throw new IOException($"ResetEvent failed (win32 {Marshal.GetLastWin32Error()})");
            }

            _lastTransferStage = "submit";
            bool ok = toDevice
                ? WinUsb_WritePipe(_interfaceHandle, pipeId, buffer, length, out transferred, overlapped)
                : WinUsb_ReadPipe(_interfaceHandle, pipeId, buffer, length, out transferred, overlapped);
            win32 = ok ? 0 : Marshal.GetLastWin32Error();

            if (!ok && win32 == ERROR_IO_PENDING)
            {
                safeToFree = false;
                _pendingOverlapped = overlapped;
                waitResult = WaitForSingleObject(_eventHandle, waitCeilingMs);
                _lastTransferStage = "completion";
                ok = WinUsb_GetOverlappedResult(_interfaceHandle, overlapped, out transferred, false);
                win32 = ok ? 0 : Marshal.GetLastWin32Error();
                if (!ok && win32 == ERROR_IO_INCOMPLETE)
                {
                    // Cancellation must complete before reusing a payload or freeing OVERLAPPED.
                    // A timeout is not itself proof that the kernel stopped using either one.
                    WinUsb_AbortPipe(_interfaceHandle, pipeId);
                    WaitForSingleObject(_eventHandle, AbortGraceMs);
                    _lastTransferStage = "cancellation completion";
                    ok = WinUsb_GetOverlappedResult(_interfaceHandle, overlapped, out transferred, false);
                    win32 = ok ? 0 : Marshal.GetLastWin32Error();
                    if (!ok && win32 == ERROR_IO_INCOMPLETE)
                    {
                        _pendingOverlapped = overlapped;
                        _deviceReady = false;
                        throw new IOException("The display did not complete USB cancellation; restart WindowsTouchBar before retrying");
                    }
                    if (!ok && waitResult == WAIT_TIMEOUT)
                    {
                        win32 = ERROR_SEM_TIMEOUT;
                    }
                }
            }
            _pendingOverlapped = IntPtr.Zero;
            safeToFree = true;
            if (!ok)
            {
                transferred = 0;
            }
            return ok;
        }
        finally
        {
            // Dispose retries completion for an abandoned transfer. Until then, both this
            // allocation and every payload pin must survive; no subsequent transfer is allowed.
            if (safeToFree)
            {
                Marshal.FreeHGlobal(overlapped);
            }
        }
    }

    private void SendGenericRequest(uint requestKey)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_requestBuf.AsSpan(0, 4), DfrDeviceRequestHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(_requestBuf.AsSpan(12, 4), GenericRequestSize - 16);
        BinaryPrimitives.WriteUInt32LittleEndian(_requestBuf.AsSpan(16, 4), requestKey);
        BinaryPrimitives.WriteUInt32LittleEndian(_requestBuf.AsSpan(28, 4), DfrGenericRequestEnd);

        if (!Transfer(true, _pipeOut, _requestPtr, GenericRequestSize, WriteWaitMs, out uint written, out int win32)
            || written != GenericRequestSize)
        {
            throw new IOException($"Sending request 0x{requestKey:X8} failed at {_lastTransferStage} ({written} of {GenericRequestSize} bytes, win32 {win32})");
        }
    }

    /// <summary>Submit the standing packet, then wait for the UDCL acknowledgement, retransmitting
    /// the same frame id up to twice exactly like DFRDisplayTransferFrameBuffer does.</summary>
    public void UpdateFrame()
    {
        lock (_frameLock)
        {
            ThrowIfUnavailable();
            if (!_deviceReady)
            {
                throw new InvalidOperationException("The DFR device is not ready");
            }
            int pixelLength = PixelArea.Length;
            byte[] packet = _framePacket!;
            byte frameId = NextFrameId();

            WriteUInt32(packet, 0, DfrUpdateFbRequestHeader);
            WriteUInt32(packet, 4, DfrUpdateFbRequestReserved1);
            WriteUInt32(packet, 12, (uint)(packet.Length - 16));
            WriteUInt16(packet, ContentReserved0Offset, (ushort)DfrUpdateFbContentReserved0);
            packet[ContentFrameIdOffset] = frameId;
            WriteUInt16(packet, ContentBeginXOffset, 0);
            WriteUInt16(packet, ContentBeginYOffset, 0);
            WriteUInt16(packet, ContentWidthOffset, (ushort)Width);
            WriteUInt16(packet, ContentHeightOffset, (ushort)Height);
            WriteUInt32(packet, ContentBufferSizeOffset, (uint)pixelLength);

            uint transferred = 0;
            int readErr = 0;
            for (int attempt = 0; ; attempt++)
            {
                if (!Transfer(true, _pipeOut, _framePacketPtr, (uint)packet.Length, WriteWaitMs, out uint written, out int writeErr)
                    || written != packet.Length)
                {
                    throw new IOException($"Writing the framebuffer packet failed after {written} bytes (win32 {writeErr})");
                }

                bool ok = Transfer(false, _pipeIn, _ackPtr, FbAckResponseSize, ReadWaitMs, out transferred, out readErr);
                if (!ok && !IsTransferTimeout(readErr))
                {
                    throw new IOException($"Reading the UDCL acknowledgement failed (win32 {readErr})");
                }

                bool acked = ok
                             && transferred >= FbAckResponseSize
                             && BinaryPrimitives.ReadUInt32LittleEndian(_ackBuf.AsSpan(0, 4)) == DfrDeviceResponseHeader
                             && BinaryPrimitives.ReadUInt32LittleEndian(_ackBuf.AsSpan(16, 4)) == DfrFramebufferUpdatedKey
                             && _ackBuf[32] == frameId;
                if (acked)
                {
                    return;
                }

                if (attempt >= FrameRetransmitMax)
                {
                    throw new IOException($"The display never acknowledged frame {frameId} ({transferred} bytes read, win32 {readErr})");
                }
            }
        }
    }

    public void Clear()
    {
        PixelArea.Clear();
        UpdateFrame();
    }

    private byte NextFrameId()
    {
        if (_currentFrameId >= 0xff)
        {
            _currentFrameId = 0x00;
        }
        _currentFrameId++;
        return _currentFrameId;
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);

    public void Dispose()
    {
        // A submit in flight still reads the pinned packet, so drain the lock before letting go
        // of anything it points at.
        lock (_frameLock)
        {
            if (_disposed && _pendingOverlapped == IntPtr.Zero)
            {
                return;
            }
            _disposed = true;
            _deviceReady = false;
            if (_pendingOverlapped != IntPtr.Zero)
            {
                // Even an aborted request can still own its buffer. If the driver never
                // completes it, retain the native handles and pins until process exit.
                bool completed = WinUsb_GetOverlappedResult(_interfaceHandle, _pendingOverlapped, out _, false);
                if (!completed && Marshal.GetLastWin32Error() == ERROR_IO_INCOMPLETE)
                {
                    return;
                }
                Marshal.FreeHGlobal(_pendingOverlapped);
                _pendingOverlapped = IntPtr.Zero;
            }
            WinUsb_Free(_interfaceHandle);
            NativeMethods.CloseHandle(_fileHandle);
            NativeMethods.CloseHandle(_eventHandle);
            if (_framePacketPin.IsAllocated)
            {
                _framePacketPin.Free();
            }
            _requestPin.Free();
            _infoPin.Free();
            _ackPin.Free();
        }
    }
}
