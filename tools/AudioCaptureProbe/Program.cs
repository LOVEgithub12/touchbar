using System.Diagnostics;
using WindowsTouchBar.Audio;

if (args.Contains("--analyzer"))
{
    static (float Bass, float Mid, float Treble, float Level) Tone(int frequency, bool reverseRight = false)
    {
        var analyzer = new AudioBandAnalyzer(48000, 2);
        (float Bass, float Mid, float Treble, float Level) result = default;
        for (int chunk = 0; chunk < 100; chunk++)
        {
            for (int i = 0; i < 480; i++)
            {
                float sample = .08f * (float)Math.Sin(2 * Math.PI * frequency * (chunk * 480 + i) / 48000);
                analyzer.Add(sample, 0);
                analyzer.Add(reverseRight ? -sample : sample, 1);
            }
            result = analyzer.ReadAndReset();
        }
        return result;
    }
    var bass = Tone(70);
    var mid = Tone(850);
    var high = Tone(8000);
    var inverted = Tone(850, true);
    Console.WriteLine($"70 Hz: {bass}; 850 Hz: {mid}; 8 kHz: {high}; inverted stereo: {inverted}");
    if (!(bass.Bass > bass.Mid * 2 && mid.Mid > mid.Bass * 2 && mid.Mid > mid.Treble * 2 && high.Treble > high.Mid * 2))
        throw new Exception("Band separation failed");
    if (Math.Abs(inverted.Level - mid.Level) > .0001f) throw new Exception("Stereo phase cancellation");
    var silent = new AudioBandAnalyzer(48000, 2);
    for (int i = 0; i < 480; i++) { silent.Add(0, 0); silent.Add(0, 1); }
    if (silent.ReadAndReset() != (0, 0, 0, 0)) throw new Exception("Silence generated energy");
    Console.WriteLine("PASS: three bands, opposite-phase stereo, digital silence.");
    return;
}

using var service = new AudioEnergyService(Console.WriteLine);
service.Start();
service.SetActive(true);
float minimum = 1, maximum = 0;
for (int i = 0; i < 20; i++)
{
    Thread.Sleep(500);
    var value = service.Snapshot;
    minimum = Math.Min(minimum, value.Level);
    maximum = Math.Max(maximum, value.Level);
    Console.WriteLine($"{i / 2.0,4:F1}s: low={value.Bass:F3} mid={value.Mid:F3} high={value.Treble:F3} total={value.Level:F3} frames={value.SampleFrames} capturing={value.IsCapturing}");
}
var before = service.Snapshot;
var timer = Stopwatch.StartNew();
service.SetActive(false);
timer.Stop();
Console.WriteLine($"SetActive(false) took {timer.Elapsed.TotalMilliseconds:F3} ms; immediate snapshot={service.Snapshot}");
if (service.Snapshot.Level != 0 || service.Snapshot.IsCapturing) throw new Exception("Inactive capture did not clear immediately");
Thread.Sleep(1000);
service.SetActive(true);
Thread.Sleep(1500);
Console.WriteLine($"Reactivated: {service.Snapshot}");
if (before.SampleFrames == 0 || service.Snapshot.SampleFrames == 0) throw new Exception("Loopback received no PCM");
Console.WriteLine($"PASS: real capture produced {before.SampleFrames} frames, level range {minimum:F3}..{maximum:F3}; suspend/resume succeeded.");
