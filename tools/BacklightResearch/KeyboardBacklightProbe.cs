using HidSharp;
using System.Text.Json;
if(args.Contains("--api-read"))
{
 Console.WriteLine($"CurrentSavedLevelPercent={WindowsTouchBar.Interop.KeyboardBacklightAPI.GetCurrentBrightness()}");
 return;
}
foreach(var device in DeviceList.Local.GetHidDevices(0x05ac,0x8102))
{
 var descriptor=device.GetReportDescriptor();
 if(!descriptor.DeviceItems.Any(i=>i.Usages.ContainsValue(0xff00000f)))continue;
 Console.WriteLine(JsonSerializer.Serialize(new {
   device.DevicePath,
   MaxFeatureLength=device.GetMaxFeatureReportLength(),
   Items=descriptor.DeviceItems.Select(i=>new {
    Usages=i.Usages.GetAllValues().Select(u=>$"{u:X8}"),
    Features=i.FeatureReports.Select(r=>new {r.ReportID,r.Length,
     Fields=r.DataItems.Select(f=>new {f.ElementBits,f.ElementCount,f.LogicalMinimum,f.LogicalMaximum,Usages=f.Usages.GetAllValues().Select(u=>$"{u:X8}")})})
   })
 }));
 using var stream=device.Open();
 if(args.Contains("--api-roundtrip"))
 {
  var original=new byte[9];original[0]=1;stream.GetFeature(original);
  int before=BitConverter.ToInt32(original,1);
  try
  {
   var timer=System.Diagnostics.Stopwatch.StartNew();
   int percent=WindowsTouchBar.Interop.KeyboardBacklightAPI.Adjust(5);
   Console.WriteLine(JsonSerializer.Serialize(new{Operation="api-increase",BeforeRaw=before,ConfirmedPercent=percent,ElapsedMs=timer.ElapsedMilliseconds}));
  }
  finally
  {
   original[0]=1;BitConverter.GetBytes(0x005e).CopyTo(original,5);stream.SetFeature(original);
   var restored=new byte[9];
   for(int retry=0;retry<40;retry++)
   {
    restored[0]=1;stream.GetFeature(restored);
    if(Math.Abs(BitConverter.ToInt32(restored,1)-before)<=2)break;
    Thread.Sleep(25);
   }
   Console.WriteLine(JsonSerializer.Serialize(new{Operation="api-restore",ReadbackRaw=BitConverter.ToInt32(restored,1)}));
  }
  continue;
 }
 foreach(var report in descriptor.DeviceItems.SelectMany(i=>i.FeatureReports))
 {
  var bytes=new byte[device.GetMaxFeatureReportLength()]; bytes[0]=report.ReportID;
  try{stream.GetFeature(bytes);Console.WriteLine(JsonSerializer.Serialize(new{ReadReport=report.ReportID,Bytes=bytes.Select(b=>(int)b)}));}
  catch(Exception ex){Console.WriteLine(JsonSerializer.Serialize(new{ReadReport=report.ReportID,Error=ex.Message}));}
 }
 if(args.Contains("--roundtrip"))
 {
  var original=new byte[9];original[0]=1;stream.GetFeature(original);
  int before=BitConverter.ToInt32(original,1);
  int target=Math.Clamp(before+300,100,14660);
  var change=new byte[9];change[0]=1;BitConverter.GetBytes(target).CopyTo(change,1);BitConverter.GetBytes(0x15e).CopyTo(change,5);
  try
  {
   stream.SetFeature(change);
   Thread.Sleep(700);
   var read=new byte[9];read[0]=1;stream.GetFeature(read);
   Console.WriteLine(JsonSerializer.Serialize(new{Operation="small-increase",Before=before,Target=target,Readback=BitConverter.ToInt32(read,1),Bytes=read.Select(b=>(int)b)}));
  }
  finally
  {
   original[0]=1;BitConverter.GetBytes(0x15e).CopyTo(original,5);stream.SetFeature(original);
   Thread.Sleep(700);
   var restored=new byte[9];restored[0]=1;stream.GetFeature(restored);
   Console.WriteLine(JsonSerializer.Serialize(new{Operation="restore",Readback=BitConverter.ToInt32(restored,1),Bytes=restored.Select(b=>(int)b)}));
  }
 }
}
