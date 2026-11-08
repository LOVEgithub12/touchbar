using System.Diagnostics;
using System.Text.Json;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

// No USB control writes, HID output/feature reports, keyboard simulation, or driver
// changes. Device descriptors are read only after --list or --listen is requested.
const uint FingerIdentifier = 0x000D0038;
const uint FingerTap = 0x000D0033;
const uint FingerTap2 = 0x000D0032;
const uint X = 0x00010030;
const uint Y = 0x00010031;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("HidTouchProbe --list | --listen [seconds 1..60]");
    Console.WriteLine("Targets only Apple Touch Bar VID 05AC PID 8302/8600. No keyboard events or writes are sent.");
    Console.WriteLine("--list reads HID descriptors. --listen opens one unambiguous matching digitizer and counts input reports.");
    Console.WriteLine("Stop WindowsTouchBar before listening. A zero report count is inconclusive without physical touches.");
    return 0;
}

bool listen = args[0] == "--listen";
if (!listen && args[0] != "--list") { Console.Error.WriteLine("Unknown option. Use --help."); return 64; }
int seconds = 15;
if (args.Length > 1 && (!int.TryParse(args[1], out seconds) || seconds < 1 || seconds > 60))
{
    Console.Error.WriteLine("Duration must be 1 to 60 seconds."); return 64;
}

try
{
    var found = new List<(HidDevice Device, ReportDescriptor Descriptor, DeviceItem Item)>();
    var descriptors = new List<object>();
    var descriptorIssues = new HashSet<string>();
    foreach (var device in DeviceList.Local.GetHidDevices(0x05AC, 0x8302)
        .Concat(DeviceList.Local.GetHidDevices(0x05AC, 0x8600)))
    {
        try
        {
            var descriptor = device.GetReportDescriptor();
            var matching = descriptor.DeviceItems.Where(d => d.InputReports.SelectMany(r => r.DataItems)
                .Any(v => v.Usages.ContainsValue(FingerIdentifier))).ToArray();
            foreach (var item in matching)
            {
                found.Add((device, descriptor, item));
                foreach (var issue in CheckLayout(item.CreateDeviceItemInputParser()))
                    descriptorIssues.Add(issue);
            }
            descriptors.Add(new
            {
                device.DevicePath,
                device.VendorID,
                device.ProductID,
                MaximumInputLength = device.GetMaxInputReportLength(),
                DigitizerCollections = matching.Length,
                ParserCollections = matching.Select(d =>
                {
                    var parser = d.CreateDeviceItemInputParser();
                    return new
                    {
                        parser.ValueCount,
                        LayoutIssues = CheckLayout(parser),
                        Values = Enumerable.Range(0, parser.ValueCount).Select(i =>
                        {
                            var value = parser.GetValue(i);
                            return new
                            {
                                Index = i,
                                Usages = value.Usages.Select(u => $"0x{u:X8}").ToArray(),
                                value.Report.ReportID,
                                value.DataItem.PhysicalMinimum,
                                value.DataItem.PhysicalMaximum
                            };
                        }).ToArray()
                    };
                }).ToArray(),
                Collections = descriptor.DeviceItems.Select(d => new
                {
                    Usages = d.Usages.GetAllValues().Select(u => $"0x{u:X8}").ToArray(),
                    Reports = d.InputReports.Select(r => new
                    {
                        r.ReportID,
                        r.Length,
                        Fields = r.DataItems.Select(v => new
                        {
                            Usages = v.Usages.GetAllValues().Select(u => $"0x{u:X8}").ToArray(),
                            v.ElementCount,
                            v.ElementBits,
                            v.LogicalMinimum,
                            v.LogicalMaximum,
                            v.PhysicalMinimum,
                            v.PhysicalMaximum
                        }).ToArray()
                    }).ToArray()
                }).ToArray()
            });
        }
        catch (Exception error)
        {
            descriptors.Add(new { device.DevicePath, Error = error.Message });
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new
        {
            ReadOnly = true,
            MatchingCollections = found.Count,
            DescriptorLayoutMatchesApp = found.Count == 1 && descriptorIssues.Count == 0,
            LayoutIssues = descriptorIssues.ToArray(),
            Devices = descriptors
        },
        new JsonSerializerOptions { WriteIndented = true }));
    if (!listen) return found.Count != 1 ? 2 : descriptorIssues.Count > 0 ? 4 : 0;
    if (found.Count != 1)
    {
        Console.Error.WriteLine($"Expected one digitizer collection, found {found.Count}; no input stream opened.");
        return 2;
    }

    using var appMutex = new Mutex(false, @"Local\WindowsTouchBar.Application", out bool noApp);
    if (!noApp)
    {
        Console.Error.WriteLine("WindowsTouchBar is running; stop it before probing input.");
        return 3;
    }
    var selected = found[0];
    using var stream = selected.Device.Open();
    var receiver = selected.Descriptor.CreateHidDeviceInputReceiver();
    var parser = selected.Item.CreateDeviceItemInputParser();
    receiver.Start(stream);
    var buffer = new byte[selected.Device.GetMaxInputReportLength()];
    int rawReports = 0, parsedReports = 0, changedReports = 0, activeReports = 0, releases = 0;
    bool wasActive = false;
    var reportIds = new HashSet<byte>();
    var layoutIssues = new HashSet<string>(descriptorIssues);
    double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
    Console.Error.WriteLine($"Listening for {seconds} seconds. Touch and release the Touch Bar several times. No keys will be sent.");
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed.TotalSeconds < seconds)
    {
        receiver.WaitHandle.WaitOne(100);
        while (timer.Elapsed.TotalSeconds < seconds && receiver.TryRead(buffer, 0, out Report report))
        {
            rawReports++;
            reportIds.Add(report.ReportID);
            if (!parser.TryParseReport(buffer, 0, report)) continue;
            parsedReports++;
            if (!parser.HasChanged) continue;
            changedReports++;
            bool anyActive = false;
            for (int i = 0; i < parser.ValueCount; i++)
            {
                var value = parser.GetValue(i);
                if (!value.Usages.Contains(FingerIdentifier)) continue;
                if (i + 4 >= parser.ValueCount)
                {
                    layoutIssues.Add("FingerIdentifier lacks four following fields.");
                    continue;
                }
                var tap = parser.GetValue(i + 1);
                var tap2 = parser.GetValue(i + 2);
                var x = parser.GetValue(i + 3);
                var y = parser.GetValue(i + 4);
                if (!tap.Usages.Contains(FingerTap) || !tap2.Usages.Contains(FingerTap2) ||
                    !x.Usages.Contains(X) || !y.Usages.Contains(Y))
                    layoutIssues.Add("Field order differs from app assumption FingerIdentifier, Touch (0x33), InRange, X, Y.");
                if (x.DataItem.PhysicalMaximum <= 0)
                    layoutIssues.Add("X physical maximum is zero; app coordinate normalization would fail.");
                if (tap.GetPhysicalValue() == 0) continue;
                anyActive = true;
                double physicalX = x.GetPhysicalValue();
                minX = Math.Min(minX, physicalX);
                maxX = Math.Max(maxX, physicalX);
            }
            if (anyActive) activeReports++;
            if (wasActive && !anyActive) releases++;
            wasActive = anyActive;
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ReadOnly = true,
        ElapsedSeconds = timer.Elapsed.TotalSeconds,
        RawReports = rawReports,
        ParsedReports = parsedReports,
        ChangedReports = changedReports,
        ActiveTouchReports = activeReports,
        ReleaseTransitions = releases,
        ReportIds = reportIds.OrderBy(i => i).ToArray(),
        ActiveXMinimum = double.IsFinite(minX) ? (double?)minX : null,
        ActiveXMaximum = double.IsFinite(maxX) ? (double?)maxX : null,
        LayoutIssues = layoutIssues.ToArray(),
        PhysicalInputObserved = activeReports > 0 && releases > 0,
        Interpretation = activeReports > 0 && releases > 0
            ? "Touch and release reports observed. No keyboard or display behavior was tested."
            : "No complete touch/release sequence observed; this does not prove failure without physical touches."
    }, new JsonSerializerOptions { WriteIndented = true }));
    return layoutIssues.Count > 0 ? 4 : 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static string[] CheckLayout(DeviceItemInputParser parser)
{
    const uint finger = 0x000D0038, touch = 0x000D0033, inRange = 0x000D0032;
    const uint xUsage = 0x00010030, yUsage = 0x00010031;
    var issues = new HashSet<string>();
    int slots = 0;
    for (int i = 0; i < parser.ValueCount; i++)
    {
        var id = parser.GetValue(i);
        if (!id.Usages.Contains(finger)) continue;
        slots++;
        if (id.Usages.FirstOrDefault() != finger)
            issues.Add("FingerIdentifier is an alias, not the first usage; current app would skip it.");
        if (i + 4 >= parser.ValueCount)
        {
            issues.Add("FingerIdentifier lacks four following fields.");
            continue;
        }
        var tap = parser.GetValue(i + 1);
        var tap2 = parser.GetValue(i + 2);
        var x = parser.GetValue(i + 3);
        var y = parser.GetValue(i + 4);
        if (!tap.Usages.Contains(touch) || !tap2.Usages.Contains(inRange) ||
            !x.Usages.Contains(xUsage) || !y.Usages.Contains(yUsage))
            issues.Add("Field order differs from app assumption FingerIdentifier, Touch (0x33), InRange, X, Y.");
        if (new[] { tap.Report, tap2.Report, x.Report, y.Report }.Any(r => !ReferenceEquals(r, id.Report)))
            issues.Add("One assumed touch slot spans multiple input reports.");
        if (x.DataItem.PhysicalMaximum <= 0)
            issues.Add("X physical maximum is zero; app coordinate normalization would fail.");
        if (x.DataItem.PhysicalMinimum != 0 || y.DataItem.PhysicalMinimum != 0)
            issues.Add("Nonzero coordinate physical minimum; app assumes zero-based coordinates.");
    }
    if (slots == 0) issues.Add("No FingerIdentifier values exist in the parser.");
    if (slots > 11) issues.Add("More than 11 touch slots; current app ignores later slots.");
    return issues.ToArray();
}
