using System.Buffers.Binary;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace WindowsTouchBar.Audio;

/// <summary>Captures the foobar output endpoint's Windows shared audio mix, never a microphone.</summary>
internal sealed class AudioEnergyService : IDisposable
{
    private readonly Action<string>? log;
    private readonly AutoResetEvent wake = new(false);
    private readonly object lifecycle = new();
    private AudioEnergyFrame frame = AudioEnergyFrame.Empty;
    private Thread? worker;
    private volatile bool active, disposed;
    private volatile bool spectrumRequested;
    private int generation, recordingFailed;
    private WasapiCapture? capture;
    private MMDevice? device;
    private string deviceId = "", selectionMode = "";

    public AudioEnergyService(Action<string>? log = null) => this.log = log;

    public AudioEnergyFrame Snapshot
    {
        get
        {
            if (!active) return AudioEnergyFrame.Empty;
            AudioEnergyFrame current = Volatile.Read(ref frame);
            double age = Now - current.CapturedAtSeconds;
            return current.FadeForAge(age);
        }
    }

    public void Start()
    {
        lock (lifecycle)
        {
            if (disposed || worker != null) return;
            worker = new Thread(Run) { IsBackground = true, Name = "TouchBar audio loopback" };
            worker.Start();
        }
    }

    // Device enumeration and WASAPI StopRecording never run on the render/input thread.
    public void SetActive(bool value, bool includeSpectrum = false)
    {
        lock (lifecycle)
        {
            if (disposed) return;
            spectrumRequested = value && includeSpectrum;
            if (active == value) return;
            active = value;
            wake.Set();
        }
    }

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private void Run()
    {
        double nextSelection = 0;
        try
        {
            while (!disposed)
            {
                if (!active)
                {
                    StopCapture();
                    nextSelection = 0;
                    wake.WaitOne(500);
                    continue;
                }
                if (Interlocked.Exchange(ref recordingFailed, 0) != 0)
                {
                    StopCapture();
                    nextSelection = Now + 1;
                }
                if (Now >= nextSelection)
                {
                    nextSelection = Now + 2;
                    try
                    {
                        var selected = SelectDevice();
                        if (selected.Device.ID != deviceId || capture == null)
                        {
                            StopCapture();
                            if (active && !disposed) StartCapture(selected.Device, selected.Mode);
                            else selected.Device.Dispose();
                        }
                        else
                        {
                            selected.Device.Dispose();
                            if (selectionMode != selected.Mode)
                            {
                                selectionMode = selected.Mode;
                                Log("audio endpoint selection: " + selected.Mode);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StopCapture();
                        Log("audio loopback retry: " + ex.Message);
                    }
                }
                wake.WaitOne(200);
            }
        }
        finally
        {
            StopCapture();
            wake.Dispose();
        }
    }

    private static (MMDevice Device, string Mode) SelectDevice()
    {
        HashSet<uint> foobarIds = new();
        foreach (Process process in Process.GetProcessesByName("foobar2000"))
        {
            using (process)
            {
                try { foobarIds.Add((uint)process.Id); }
                catch (InvalidOperationException) { }
            }
        }
        using var enumerator = new MMDeviceEnumerator();
        if (foobarIds.Count > 0)
        {
            foreach (MMDevice candidate in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                bool matched = false;
                try
                {
                    var sessions = candidate.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        if (foobarIds.Contains(session.GetProcessID) &&
                            session.State == AudioSessionState.AudioSessionStateActive)
                        {
                            matched = true;
                            break;
                        }
                    }
                }
                catch { /* An endpoint may disappear during enumeration. */ }
                if (matched) return (candidate, "foobar2000 active output session");
                candidate.Dispose();
            }
        }
        return (enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia),
            "default multimedia output fallback (no active foobar2000 shared session)");
    }

    private void StartCapture(MMDevice selected, string mode)
    {
        device = selected;
        deviceId = selected.ID;
        selectionMode = mode;
        var source = new ShortBufferLoopback(selected);
        capture = source;
        WaveFormat format = source.WaveFormat.AsStandardWaveFormat();
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat;
        int bytes = format.BitsPerSample / 8;
        if ((!floating && format.Encoding != WaveFormatEncoding.Pcm) ||
            (floating && bytes != 4) || (!floating && bytes is not (2 or 3 or 4)))
            throw new NotSupportedException("Unsupported loopback format " + format);
        var analyzer = new AudioBandAnalyzer(format.SampleRate, format.Channels);
        AudioSpectrumAnalyzer? spectrum = null;
        string name = selected.FriendlyName;
        long samples = 0;
        int token = Interlocked.Increment(ref generation);
        var previousFrame = Volatile.Read(ref frame);
        Volatile.Write(ref frame, previousFrame with { IsCapturing = true, DeviceName = name, Spectrum = default });
        source.DataAvailable += (_, e) =>
        {
            if (!active || token != Volatile.Read(ref generation)) return;
            try
            {
                int block = format.BlockAlign;
                int count = e.BytesRecorded - e.BytesRecorded % block;
                if (count == 0) return;
                spectrum = spectrumRequested ? spectrum ?? new AudioSpectrumAnalyzer(format.SampleRate, format.Channels) : null;
                for (int offset = 0; offset < count; offset += block)
                {
                    for (int channel = 0; channel < format.Channels; channel++)
                    {
                        int at = offset + channel * bytes;
                        float sample;
                        if (floating) sample = BitConverter.ToSingle(e.Buffer, at);
                        else if (bytes == 2) sample = BinaryPrimitives.ReadInt16LittleEndian(e.Buffer.AsSpan(at, 2)) / 32768f;
                        else if (bytes == 3)
                        {
                            int value = e.Buffer[at] | e.Buffer[at + 1] << 8 | e.Buffer[at + 2] << 16;
                            sample = (value << 8 >> 8) / 8388608f;
                        }
                        else sample = BinaryPrimitives.ReadInt32LittleEndian(e.Buffer.AsSpan(at, 4)) / 2147483648f;
                        analyzer.Add(sample, channel);
                        spectrum?.Add(sample, channel);
                    }
                }
                samples += count / block;
                var energy = analyzer.ReadAndReset();
                if (active && token == Volatile.Read(ref generation))
                    Volatile.Write(ref frame, new AudioEnergyFrame(energy.Bass, energy.Mid, energy.Treble,
                        energy.Level, true, name, samples, Now) { Spectrum = spectrum?.Latest ?? default });
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref recordingFailed, 1) == 0)
                    Log("audio analysis restart: " + ex.Message);
                wake.Set();
            }
        };
        source.RecordingStopped += (_, e) =>
        {
            if (token != Volatile.Read(ref generation)) return;
            Interlocked.Exchange(ref recordingFailed, 1);
            if (e.Exception != null) Log("audio capture stopped: " + e.Exception.Message);
            wake.Set();
        };
        source.StartRecording();
        Log($"audio loopback started: {name}; {mode}; {format.SampleRate} Hz/{format.Channels} channels/{format.Encoding}/{format.BitsPerSample} bit");
    }

    private void StopCapture()
    {
        Interlocked.Increment(ref generation);
        var previous = capture;
        capture = null;
        try { previous?.StopRecording(); }
        catch (Exception ex) { Log("audio stop: " + ex.Message); }
        try { previous?.Dispose(); }
        catch (Exception ex) { Log("audio dispose: " + ex.Message); }
        device?.Dispose();
        device = null;
        deviceId = "";
        var previousFrame = Volatile.Read(ref frame);
        Volatile.Write(ref frame, previousFrame with { IsCapturing = false, Spectrum = default });
        if (previous != null) Log("audio loopback suspended");
    }

    private void Log(string value)
    {
        try { log?.Invoke(value); }
        catch { /* Diagnostics must not terminate the audio worker. */ }
    }

    // The default NAudio loopback buffer is 100 ms. A shorter shared-mode
    // buffer gives the spectrum frequent fresh data without changing output.
    private sealed class ShortBufferLoopback : WasapiCapture
    {
        public ShortBufferLoopback(MMDevice endpoint) : base(endpoint, false, 28) { }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
            base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
    }

    public void Dispose()
    {
        Thread? pending;
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            active = false;
            Volatile.Write(ref frame, AudioEnergyFrame.Empty);
            pending = worker;
            if (pending == null) wake.Dispose();
            else wake.Set();
        }
        if (pending != null && pending != Thread.CurrentThread) pending.Join(1500);
    }
}
