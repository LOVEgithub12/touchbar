using WindowsTouchBar.Device.Hid;
using WindowsTouchBar.Events;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace WindowsTouchBar;

internal class TouchReceiver : ITouchInput
{
    private readonly HidDevice _digitizer;
    private readonly ReportDescriptor _reportDescr;
    private readonly HidStream _hidStream;
    private readonly HidDeviceInputReceiver _hidDeviceInputReceiver;
    private readonly DeviceItemInputParser _hiddeviceInputParser;
    private readonly int _width;
    private readonly int _height;
    //private TouchReport[] _prevReports;
    private int _prevTappedSlotIndex = -1;
    private volatile bool _disposed;

    public event Action<TouchEventArgs>? Event;
    public event Action<Exception>? Failed;

    public TouchReceiver(int width, int height)
    {
        _width = width;
        _height = height;
        // The composite device exposes several HID collections. Select the digitizer
        // descriptor instead of relying on the enumeration order of keyboard collections.
        var candidates = DeviceList.Local.GetHidDevices(0x05ac, 0x8302)
            .Concat(DeviceList.Local.GetHidDevices(0x05ac, 0x8600));
        foreach (var candidate in candidates)
        {
            ReportDescriptor descriptor;
            try { descriptor = candidate.GetReportDescriptor(); }
            catch (IOException) { continue; }
            var item = descriptor.DeviceItems.FirstOrDefault(d => d.InputReports
                .SelectMany(r => r.DataItems)
                .Any(v => v.Usages.ContainsValue((uint)VendorUsage.FingerIdentifier)));
            if (item is null || !candidate.TryOpen(out var stream)) continue;

            _digitizer = candidate;
            _reportDescr = descriptor;
            _hidStream = stream;
            _hidDeviceInputReceiver = descriptor.CreateHidDeviceInputReceiver();
            _hiddeviceInputParser = item.CreateDeviceItemInputParser();
            _hidDeviceInputReceiver.Received += OnDigitizerInputReceived;
            _hidDeviceInputReceiver.Start(_hidStream);
            return;
        }
        throw new IOException("iBridge HID touch digitizer not found or could not be opened");
    }

    private void OnDigitizerInputReceived(object? sender, EventArgs e)
    {
        if (_disposed) return;
        try
        {
        var inputReportBuffer = new byte[_digitizer.GetMaxInputReportLength()];
        while (!_disposed && _hidDeviceInputReceiver.TryRead(inputReportBuffer, 0, out Report report))
        {
            // Parse the report if possible.
            // This will return false if (for example) the report applies to a different DeviceItem.
            if (_hiddeviceInputParser.TryParseReport(inputReportBuffer, 0, report))
            {
                ProcessEvent();
            }
        }
        }
        catch (Exception ex)
        {
            if (!_disposed) Failed?.Invoke(ex);
        }
    }

    private void ProcessEvent()
    {
        if (_hiddeviceInputParser.HasChanged)
        {
            int j = -1;
            var currentReports = new TouchReport?[11];

            for (int i = 0; i + 4 < _hiddeviceInputParser.ValueCount; i++)
            {
                var data = _hiddeviceInputParser.GetValue(i);
                if (data.Usages.FirstOrDefault() == VendorUsage.FingerIdentifier)
                {
                    j++;
                }
                else
                {
                    continue;
                }

                // Only 11 slots are statically allocated
                if (j >= 11) break;

                // This is defined by the descriptor, we just take the assumption
                var fingerTapData1 = _hiddeviceInputParser.GetValue(i + 1);
                var fingerTapData2 = _hiddeviceInputParser.GetValue(i + 2);
                var xData = _hiddeviceInputParser.GetValue(i + 3);
                var yData = _hiddeviceInputParser.GetValue(i + 4);

                // Register this
                currentReports[j] = new TouchReport(
                    xData.GetPhysicalValue(),
                    xData.DataItem.PhysicalMaximum,
                    yData.GetPhysicalValue(),
                    yData.DataItem.PhysicalMaximum,
                    fingerTapData1.GetPhysicalValue() != 0);
            }

            // Check if need to raise touch leave event for prev slot
            if (_prevTappedSlotIndex >= 0)
            {
                var previous = currentReports[_prevTappedSlotIndex];
                var scaledX = Scale(previous?.GetXInPercentage() ?? 0, _width);
                var scaledY = Scale(previous?.GetYInPercentage() ?? 0.5, _height);

                if (previous?.FingerStatus != true)
                {
                    Event?.Invoke(new TouchEventArgs(
                        scaledX, 
                        scaledY, 
                        TouchEventType.TouchUp));

                    _prevTappedSlotIndex = -1;
                }
                else
                {
                    Event?.Invoke(new TouchEventArgs(
                        scaledX,
                        scaledY,
                        TouchEventType.TouchMove));
                }
            }

            // Can raise new tap event
            if (_prevTappedSlotIndex == -1)
            {
                for (int i = 0; i < 11; i++)
                {
                    var current = currentReports[i];
                    if (current?.FingerStatus == true)
                    {
                        var scaledX = Scale(current.GetXInPercentage(), _width);
                        var scaledY = Scale(current.GetYInPercentage(), _height);

                        Event?.Invoke(new TouchEventArgs(
                        scaledX,
                        scaledY,
                        TouchEventType.TouchDown));
                        
                        _prevTappedSlotIndex = i;
                        break;
                    }
                }
            }

            // Update cache
            //_prevReports = currentReports;
        }
    }

    private static int Scale(double position, int size) =>
        Math.Clamp((int)(position * size), 0, size - 1);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hidDeviceInputReceiver.Received -= OnDigitizerInputReceived;
        _hidStream.Dispose();
    }
}
