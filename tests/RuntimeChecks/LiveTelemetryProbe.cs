using System.Diagnostics;
using System.Text.Json;
using SkiaSharp;
using WindowsTouchBar.Gaming;
using WindowsTouchBar.Views;

internal static class LiveTelemetryProbe
{
    public static void Run(int pid, int seconds, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output+".pid", Environment.ProcessId.ToString());
        using var log=new StreamWriter(output+".log"){AutoFlush=true};
        using var data=new StreamWriter(output){AutoFlush=true};
        using var raw=new StreamWriter(output+".csv.log"){AutoFlush=true};
        using var service=new GameTelemetryService(line=>{lock(log)log.WriteLine(line);},line=>raw.WriteLine(Stopwatch.GetTimestamp()+"|"+line));
        service.SetProcess(pid);
        var watch=Stopwatch.StartNew();
        using var view=new GameMonitorView(2008,60);
        var game=new GameDefinition("probe","Telemetry probe","Resources/Games/sekiro.png",Array.Empty<string>());
        while(watch.Elapsed.TotalSeconds<seconds) {
            var snapshot=service.Snapshot;
            data.WriteLine(JsonSerializer.Serialize(new{At=DateTimeOffset.Now,Elapsed=watch.Elapsed.TotalSeconds,Snapshot=snapshot}));
            if(snapshot.Fps is not null && snapshot.CpuTemperature is not null) {
                view.SetSnapshot(game,snapshot);using var bitmap=new SKBitmap(2008,60);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Black);view.Draw(canvas);
                using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(output+".png",png.ToArray());
            }
            Thread.Sleep(250);
        }
        service.SetProcess(0);
        if(service.Snapshot!=TelemetrySnapshot.Empty)throw new Exception("Target exit retained readings");
    }
}
