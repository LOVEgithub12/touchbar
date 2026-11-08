using HidSharp;
using System.Text.Json;

foreach (var device in DeviceList.Local.GetHidDevices(0x05ac, 0x8102))
{
    try
    {
        var descriptor = device.GetReportDescriptor();
        Console.WriteLine(JsonSerializer.Serialize(new {
            device.DevicePath,
            MaxFeatureLength = device.GetMaxFeatureReportLength(),
            Items = descriptor.DeviceItems.Select(item => new {
                Usages = item.Usages.GetAllValues().Select(value => $"{value:X8}"),
                Features = item.FeatureReports.Select(report => new {
                    report.ReportID, report.Length,
                    Fields = report.DataItems.Select(field => new { field.ElementBits, field.ElementCount, field.LogicalMinimum, field.LogicalMaximum,
                        Usages = field.Usages.GetAllValues().Select(value => $"{value:X8}") })
                })
            })
        }));
        if (!device.DevicePath.Contains("mi_00", StringComparison.OrdinalIgnoreCase)) continue;
        using var stream = device.Open();
        foreach (var report in descriptor.DeviceItems.SelectMany(item => item.FeatureReports))
        {
            var bytes = new byte[device.GetMaxFeatureReportLength()];
            bytes[0] = report.ReportID;
            try { stream.GetFeature(bytes); Console.WriteLine(JsonSerializer.Serialize(new { ReadReport = report.ReportID, Bytes = bytes.Select(value => (int)value) })); }
            catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { ReadReport = report.ReportID, Error = error.Message })); }
        }
    }
    catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { device.DevicePath, Error = error.Message })); }
}
