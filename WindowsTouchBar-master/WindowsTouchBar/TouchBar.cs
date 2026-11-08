using SkiaSharp;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsTouchBar.Device;
using WindowsTouchBar.Helpers;
using WindowsTouchBar.Interop;
using WindowsTouchBar.Lyrics;
using WindowsTouchBar.Models;
using WindowsTouchBar.Views;
using WindowsTouchBar.Audio;

namespace WindowsTouchBar;

internal class TouchBar : IDisposable
{
    private const string SETTINGS_FILE = "settings.json";
    private readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, SETTINGS_FILE);
    private readonly object viewLock = new();
    private readonly ManualResetEventSlim stopRequested = new(false);
    private readonly CancellationTokenSource shutdown = new();
    public Exception? LastError { get; private set; }
    public event Action? Ready;

    private readonly IDisplayTransport transport;
    private readonly LockedFrameBuffer FrameBuffer;
    private readonly ITouchInput TouchReceiver;

    private TouchBarView? currentView;
    private string? lastLoggedView;
    private CustomFunctionsView? customFunctionsView;
    private DefaultFunctionsView? defaultFunctionsView;
    private MediaFunctionsView? mediaFunctionsView;
    private ExplorerDialogView? explorerDialogView;
    private LyricsView? lyricsView;
    private FoobarLyricsService? lyricsService;
    private TouchBarBacklightService? touchBarBacklight;
    private AudioEnergyService? audioEnergy;
    private System.Threading.Timer? lyricsAnimation;
    private LyricsSnapshot lyricSnapshot = LyricsSnapshot.Inactive;
    private readonly LyricsNavigation lyricsNavigation = new();
    private bool lastNavigationLyrics;
    private bool mainRequested;
    private bool visualizerToggleRequested;
    private int animationSampleFrames;
    private long animationSampleStart;
    private long animationSamplePrevious;
    private double animationSampleMaxGap;
    private bool animationSampleLogged;
    private SKSurface? skSurface;
    private SKCanvas? canvas;
    private Settings? settings;
    private FileSystemWatcher? settingsFileListener;
    private Settings.View? currentSettingView;
    private ForegroundProcess? currentProcess;
    private bool fnPressed;
    private FnKeyService? fnKeyService;
    private Events.TouchEventArgs? lastTouchEvent;

    // The panel reports its real geometry during the DFR handshake; 2008x60 is only the
    // fallback for a device that answers with something we cannot use.
    private readonly int Width = 2008;
    private readonly int Height = 60;
    public string DisplayDescription => $"{Width}x{Height} via {transport.GetType().Name}";

    public TouchBar(bool useWsl = false, bool useUsbIp = false)
    {
        if (useUsbIp)
        {
            transport = UsbIpTouchBarTransport.Open();
        }
        else if (useWsl)
        {
            transport = WslTouchBarBridge.Open();
        }
        else
        {
            var instancePath = Locator.FindDfrDevice();
            if (instancePath == null) throw new Exception("DFR Display Device not found");
            transport = new WinUsbDisplayTransport(instancePath);
        }
        Width = transport.Width;
        Height = transport.Height;

        try
        {
            FrameBuffer = new LockedFrameBuffer(transport, Width, Height);
            TouchReceiver = transport is ITouchInput bridgeInput ? bridgeInput : new TouchReceiver(Width, Height);
            CreateCanvas();
            CreateViews();

            TouchReceiver.Event += TouchReceiver_Event;
            TouchReceiver.Failed += TouchReceiver_Failed;
            ForegroundTracker.OnProcessChanged += ForegroundTracker_OnProcessChanged;
        }
        catch
        {
            (TouchReceiver as IDisposable)?.Dispose();
            skSurface?.Dispose();
            try { FrameBuffer?.Dispose(); }
            finally { transport.Dispose(); }
            throw;
        }
    }

    public void Init(bool run = true)
    {
        lock (viewLock)
        {
            InitSettings();
            if (run)
            {
                touchBarBacklight = new TouchBarBacklightService(ControlDiagnostics.Write);
                audioEnergy = new AudioEnergyService(ControlDiagnostics.Write);
                audioEnergy.Start();
                lyricsService = new FoobarLyricsService(Path.Combine(AppContext.BaseDirectory, "foobar-state.json"), ControlDiagnostics.Write);
                lyricsService.Published += LyricsPublished;
                lyricsService.Start();
                lyricsAnimation = new System.Threading.Timer(_ => AnimateLyrics(), null, 16, Timeout.Infinite);
                try
                {
                    fnKeyService = new FnKeyService(ControlDiagnostics.Write);
                    fnKeyService.Changed += FnKeyChanged;
                    fnPressed = fnKeyService.Pressed;
                    RefreshView();
                }
                catch (Exception error)
                {
                    ControlDiagnostics.Write("Fn listener unavailable: " + error.Message);
                }
            }
        }
        Ready?.Invoke();
        if (run)
        {
            ForegroundTracker.Init(shutdown.Token);
            stopRequested.Wait();
        }
    }

    public void RequestStop() => stopRequested.Set();

    private void AnimateLyrics()
    {
        // Rearm only after the completed USB frame. Slow hardware cannot build
        // up overlapping timer callbacks or a backlog of obsolete frames.
        lock (viewLock)
        {
            if (disposedValue || stopRequested.IsSet) return;
            long frameStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateSafely(() =>
            {
                if (lyricsNavigation.ShowLyrics != lastNavigationLyrics) RefreshView();
                if (ReferenceEquals(currentView, lyricsView) && lyricsView!.NeedsAnimation)
                {
                    currentView!.Invalidate();
                    if (!animationSampleLogged)
                    {
                        long tick = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (animationSampleFrames == 0) animationSampleStart = tick;
                        else animationSampleMaxGap = Math.Max(animationSampleMaxGap,
                            (tick - animationSamplePrevious) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                        animationSamplePrevious = tick;
                        if (++animationSampleFrames == 120)
                        {
                            double seconds = (tick - animationSampleStart) / (double)System.Diagnostics.Stopwatch.Frequency;
                            ControlDiagnostics.Write($"Lyrics animation: {119 / seconds:F1} frames/s; maximum interval {animationSampleMaxGap:F1} ms over 120 acknowledged frames.");
                            animationSampleLogged = true;
                        }
                    }
                }
                else if (!animationSampleLogged)
                {
                    animationSampleFrames = 0;
                    animationSampleMaxGap = 0;
                }
            });
            if (!disposedValue && !stopRequested.IsSet)
            {
                // Budget the whole acknowledged frame, rather than adding a
                // fixed sleep on top of rendering plus USB round-trip time.
                // One outstanding timer/frame is retained, including on stalls.
                double workMs = (System.Diagnostics.Stopwatch.GetTimestamp() - frameStarted) * 1000.0 /
                    System.Diagnostics.Stopwatch.Frequency;
                int delay = (int)Math.Clamp(Math.Ceiling(25 - workMs), 1, 25);
                lyricsAnimation?.Change(delay, Timeout.Infinite);
            }
        }
    }

    private void InitSettings()
    {
        settingsFileListener = new FileSystemWatcher(AppContext.BaseDirectory, SETTINGS_FILE);
        settingsFileListener.Changed += SettingsFileListener_Changed;
        settingsFileListener.Created += SettingsFileListener_Changed;
        settingsFileListener.Renamed += SettingsFileListener_Changed;
        settingsFileListener.EnableRaisingEvents = true;

        LoadSettings();
    }

    private void LoadSettings()
    {
        if (File.Exists(settingsPath))
        {
            try
            {
                using var shortcutsFile = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var updated = JsonSerializer.Deserialize<Settings>(shortcutsFile, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters =
                {
                    new JsonStringEnumConverter()
                }
                });
                if (updated is null) throw new JsonException("settings.json contains no settings object.");
                settings = updated;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (settings is null) throw new IOException("Cannot load settings.json. Check its JSON syntax and file permissions.", ex);
                // Editors may issue a change event before finishing their write. Keep the
                // last valid layout; a later event or a restart loads the completed file.
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "WindowsTouchBar.settings.log"),
                    $"{DateTime.Now:O}{Environment.NewLine}{ex.Message}{Environment.NewLine}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings?.BackgroundColor) && SKColor.TryParse(settings.BackgroundColor, out var backgroundColor))
            {
                defaultFunctionsView!.BackgroundColor = backgroundColor;
                customFunctionsView!.BackgroundColor = backgroundColor;
                mediaFunctionsView!.BackgroundColor = backgroundColor;
                explorerDialogView!.BackgroundColor = backgroundColor;
            }

            defaultFunctionsView!.ConfigureView(settings?.Views?.FirstOrDefault(v => v.Type == ViewType.Function));
            mediaFunctionsView!.ConfigureView(settings?.Views?.FirstOrDefault(v => v.Type == ViewType.Media));
        }
        else
        {
            settings = null;
        }

        RefreshCurrentProcess(currentProcess);
    }

    private void SettingsFileListener_Changed(object sender, FileSystemEventArgs e)
    {
        UpdateSafely(() =>
        {
            // The launcher can restore the selected default after a missed Fn
            // transition, without adding nonstandard controls to the panel.
            fnPressed = false;
            fnKeyService?.Reset();
            LoadSettings();
        });
    }

    private void CreateCanvas()
    {
        skSurface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque), FrameBuffer.Address);
        canvas = skSurface.Canvas;
    }

    private void CreateViews()
    {
        defaultFunctionsView = new DefaultFunctionsView(Width, Height);
        defaultFunctionsView.OnInvalidate += CurrentView_OnInvalidate;
        customFunctionsView = new CustomFunctionsView(Width, Height);
        customFunctionsView.OnInvalidate += CurrentView_OnInvalidate;
        mediaFunctionsView = new MediaFunctionsView(Width, Height);
        mediaFunctionsView.OnInvalidate += CurrentView_OnInvalidate;
        explorerDialogView = new ExplorerDialogView(Width, Height);
        explorerDialogView.OnInvalidate += CurrentView_OnInvalidate;
        lyricsView = new LyricsView(Width, Height);
        lyricsView.VisualizerEnabled = VisualizerPreference.Load(ControlDiagnostics.Write);
        lyricsView.OnInvalidate += CurrentView_OnInvalidate;
        lyricsView.MainRequested += () =>
        {
            // Process the navigation after dispatch finishes, and only for a
            // real release. Synthetic releases/slide-out must not navigate.
            if (lastTouchEvent?.TouchEventType == Events.TouchEventType.TouchUp) mainRequested = true;
        };
        lyricsView.VisualizerToggleRequested += () =>
        {
            if (lastTouchEvent?.TouchEventType == Events.TouchEventType.TouchUp) visualizerToggleRequested = true;
        };
    }

    private void LyricsPublished(LyricsSnapshot snapshot)
    {
        UpdateSafely(() =>
        {
            bool visibleChanged = lyricSnapshot.ShowLyrics != snapshot.ShowLyrics || lyricSnapshot.Active != snapshot.Active;
            lyricSnapshot = snapshot;
            lyricsNavigation.SetPlaying(snapshot.ShowLyrics);
            visibleChanged |= lyricsNavigation.ShowLyrics != lastNavigationLyrics;
            mediaFunctionsView!.SetSnapshot(snapshot);
            lyricsView!.SetSnapshot(snapshot);
            if (fnPressed) return;
            if (settings?.LyricsEnabled == false)
            {
                if (ReferenceEquals(currentView, mediaFunctionsView)) currentView.Invalidate();
                return;
            }
            if (visibleChanged) RefreshView();
            else if (ReferenceEquals(currentView, lyricsView) || ReferenceEquals(currentView, mediaFunctionsView)) currentView.Invalidate();
        });
    }

    private void ForegroundTracker_OnProcessChanged(ForegroundProcess process)
    {
        UpdateSafely(() => RefreshCurrentProcess(process));
    }

    private void RefreshCurrentProcess(ForegroundProcess? process)
    {
        var previous = currentProcess;
        currentProcess = process;

        RefreshView();
        if (!ReferenceEquals(previous, process)) previous?.Process?.Dispose();
    }

    private void RefreshView()
    {
        lastNavigationLyrics = lyricsNavigation.ShowLyrics;
        if (fnPressed)
        {
            DisposeViewLastTouch();
            currentSettingView = null;
            currentView = defaultFunctionsView;
            currentView?.Invalidate();
            return;
        }

        string? name = null;

        try
        {
            name = currentProcess?.Process?.MainModule?.ModuleName;
        }
        catch { }

        Settings.View? view = null;

        if (!string.IsNullOrWhiteSpace(name))
        {
            view = settings?.Views?.FirstOrDefault(v => v.Type == ViewType.Custom && string.Equals(v.Application, name, StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals("explorer.exe", name, StringComparison.OrdinalIgnoreCase) &&
            currentProcess is not null &&
            currentProcess.Handle != IntPtr.Zero)
        {
            var buttons = ForegroundTracker.GetWindowButtons(currentProcess.Handle).Where(b => b.Title?.StartsWith('&') is true || b.Title?.Contains('&') is not true).ToArray();

            if (buttons.Any())
            {
                explorerDialogView!.CreateDialog(buttons);

                DisposeViewLastTouch();
                currentSettingView = null;
                currentView = explorerDialogView;

                currentView?.Invalidate();

                return;
            }
        }

        if (settings?.LyricsEnabled != false && lyricSnapshot.Active)
        {
            var playerView = lastNavigationLyrics ? (TouchBarView?)lyricsView : mediaFunctionsView;
            if (!ReferenceEquals(currentView, playerView)) DisposeViewLastTouch();
            currentSettingView = null;
            currentView = playerView;
            currentView?.Invalidate();
        }
        else if (view != null)
        {
            if (view != currentSettingView)
            {
                DisposeViewLastTouch();
                currentSettingView = view;
                customFunctionsView?.ConfigureView(currentSettingView);
                currentView = customFunctionsView;
                currentView?.Invalidate();
            }
        }
        else
        {
            currentSettingView = null;

            DisposeViewLastTouch();
            if (settings?.Views?.Any(v => v.Type == ViewType.Media && v.Default) is true)
            {
                currentView = mediaFunctionsView;
            }
            else
            {
                currentView = defaultFunctionsView;
            }

            currentView?.Invalidate();
        }
    }

    private void FnKeyChanged(bool pressed)
    {
        UpdateSafely(() =>
        {
            // A manual reset can supersede a notification waiting for viewLock.
            bool latest = fnKeyService?.Pressed ?? pressed;
            if (fnPressed == latest) return;
            fnPressed = latest;
            RefreshView();
        });
    }

    public void CurrentView_OnInvalidate(object? sender, EventArgs e)
    {
        lock (viewLock)
        {
            if (disposedValue || currentView is null) return;
            bool lyricsVisible = ReferenceEquals(currentView, lyricsView) && lyricSnapshot.ShowLyrics;
            touchBarBacklight?.SetLyricsVisible(lyricsVisible);
            audioEnergy?.SetActive(lyricsVisible && lyricsView!.VisualizerEnabled);
            string viewName = currentView.GetType().Name;
            if (lastLoggedView != viewName)
            {
                lyricsView?.ResetAnimation();
                lastLoggedView = viewName;
                ControlDiagnostics.Write("View: " + viewName);
            }
            if (audioEnergy is not null && lyricsVisible)
            {
                var energy = audioEnergy.Snapshot;
                lyricsView!.SetAudioEnergy(energy.Bass, energy.Mid, energy.Treble, energy.Level);
            }
            currentView.Clear(canvas);
            currentView.Draw(canvas!);
            FrameBuffer.VSync();
        }
    }

    private void TouchReceiver_Event(Events.TouchEventArgs evt)
    {
        UpdateSafely(() =>
        {
            switch (evt.TouchEventType)
            {
                case Events.TouchEventType.TouchDown: lyricsNavigation.TouchDown(); break;
                case Events.TouchEventType.TouchMove: lyricsNavigation.TouchMove(); break;
                case Events.TouchEventType.TouchUp: lyricsNavigation.TouchUp(); break;
            }
            lastTouchEvent = evt;
            currentView?.ReceiveTouch(evt);
            if (visualizerToggleRequested)
            {
                visualizerToggleRequested = false;
                lyricsView!.VisualizerEnabled = !lyricsView.VisualizerEnabled;
                VisualizerPreference.Save(lyricsView.VisualizerEnabled, ControlDiagnostics.Write);
                ControlDiagnostics.Write("Aurora background: " + (lyricsView.VisualizerEnabled ? "shown" : "hidden"));
                currentView?.Invalidate();
            }
            if (mainRequested)
            {
                mainRequested = false;
                lyricsNavigation.OpenMain();
                ControlDiagnostics.Write("Navigation: player icon opened main controls; idle return after 4 seconds.");
                RefreshView();
            }
        });
    }

    private void TouchReceiver_Failed(Exception error)
    {
        lock (viewLock)
        {
            if (disposedValue || stopRequested.IsSet) return;
            LastError = error;
            RequestStop();
        }
    }

    private void UpdateSafely(Action update)
    {
        lock (viewLock)
        {
            if (disposedValue || stopRequested.IsSet) return;
            try { update(); }
            catch (Exception ex)
            {
                LastError = ex;
                RequestStop();
            }
        }
    }

    private void DisposeViewLastTouch()
    {
        if (lastTouchEvent is not null &&
            currentView is not null &&
            lastTouchEvent.TouchEventType != Events.TouchEventType.TouchUp)
        {
            var evt = new Events.TouchEventArgs(lastTouchEvent.X, lastTouchEvent.Y, Events.TouchEventType.TouchUp);
            lastTouchEvent = null;

            currentView.ReceiveTouch(evt);
        }
    }

    private bool disposedValue = false;
    protected virtual void Dispose(bool disposing)
    {
        RequestStop();
        shutdown.Cancel();
        lock (viewLock)
        {
            if (disposedValue) return;
            disposedValue = true;

            ForegroundTracker.OnProcessChanged -= ForegroundTracker_OnProcessChanged;
            if (fnKeyService is not null) fnKeyService.Changed -= FnKeyChanged;
            if (lyricsService is not null) lyricsService.Published -= LyricsPublished;
            lyricsAnimation?.Dispose();
            TouchReceiver.Event -= TouchReceiver_Event;
            TouchReceiver.Failed -= TouchReceiver_Failed;
            settingsFileListener?.Dispose();
            DisposeViewLastTouch();
            currentProcess?.Process?.Dispose();
            skSurface?.Dispose();
            try { FrameBuffer.Dispose(); }
            finally { transport.Dispose(); }
        }
        // Closing HID can wait for a pending callback. Let it finish without
        // holding the view lock that callback needs.
        TouchReceiver.Dispose();
        fnKeyService?.Dispose();
        lyricsService?.Dispose();
        touchBarBacklight?.Dispose();
        audioEnergy?.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
