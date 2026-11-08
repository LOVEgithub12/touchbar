using WindowsTouchBar.Lyrics;

var checks = new (string Name, Action Run)[]
{
    ("Short lines stay centered inside the expanded content viewport", Centered),
    ("Long sung lines move only forward and keep the singing front visible", Singing),
    ("Tiny backwards clock corrections do not reverse the camera", Jitter),
    ("Seeks and changed geometry place the new singing front immediately", Reset),
    ("Camera easing is stable across frame rates", FrameRates),
    ("Invalid measurements recover cleanly", Invalid)
};
int failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS: " + check.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL: " + check.Name + "\n" + error); }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed. No player, USB access or clock waits.");
return failed == 0 ? 0 : 1;

static void Near(float actual, float expected, string reason, float tolerance = 0.01f)
{
    if (!float.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
        throw new InvalidOperationException($"{reason}: {actual} != {expected}");
}
static void Require(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}
static void Centered()
{
    var camera = new LyricsViewport();
    // Physical lyric label width 928, retaining 15px outer padding on each side.
    Near(15 + camera.GetX(400, 928 - 30, 100, 1 / 60d), 264, "Short line did not center in the label");
    Near(15 + camera.GetX(400, 928 - 30, 350, 1 / 60d), 264, "Short line moved as it was sung");
}
static void Singing()
{
    var camera = new LyricsViewport();
    const float width = 3000, viewport = 898;
    float last = camera.GetX(width, viewport, 0, 0);
    Near(last, LyricsViewport.EdgeInset, "Long line's beginning was not visible");
    for (int frame = 1; frame <= 720; frame++)
    {
        float sung = width * frame / 720;
        float x = camera.GetX(width, viewport, sung, 1 / 60d);
        Require(x <= last + 0.001f, "Camera moved backwards during normal singing");
        Require(x + sung >= LyricsViewport.EdgeInset - 0.001f, "Singing front disappeared at the left");
        Require(x + sung <= viewport - LyricsViewport.EdgeInset + 0.001f, "Singing front disappeared at the right");
        Require(x >= viewport - LyricsViewport.EdgeInset - width - 0.001f, "Camera scrolled past the line's end");
        last = x;
    }
    Near(last + width, viewport - LyricsViewport.EdgeInset, "Line ending was not fully visible");
    // A fast burst must also keep the current syllable within the viewing area.
    camera.Reset();
    camera.GetX(width, viewport, 0, 0);
    float burst = camera.GetX(width, viewport, 2200, 1 / 60d);
    Require(burst + 2200 <= viewport - LyricsViewport.EdgeInset, "Fast singing outran the camera");
}
static void Jitter()
{
    var camera = new LyricsViewport();
    float before = camera.GetX(3000, 898, 1800, 0);
    Near(camera.GetX(3000, 898, 1795, 1 / 60d), before, "Small clock correction reversed camera motion");
}
static void Reset()
{
    var camera = new LyricsViewport();
    camera.GetX(3000, 898, 2400, 0);
    Near(camera.GetX(3000, 898, 200, 1 / 60d, reset: true), LyricsViewport.EdgeInset, "Backward seek retained an old offset");
    camera.GetX(3000, 898, 2300, 0, reset: true);
    Near(camera.GetX(3000, 1098, 1500, 1 / 60d), 1098 * LyricsViewport.FocusFraction - 1500,
        "Resizing panned through a stale camera position");
    camera.Reset();
    Near(camera.GetX(3000, 898, 1100, 0), 898 * LyricsViewport.FocusFraction - 1100, "Explicit reset was ignored");
}
static void FrameRates()
{
    static float Move(int frameRate)
    {
        var camera = new LyricsViewport();
        camera.GetX(3000, 898, 1000, 0);
        float x = 0;
        for (int frame = 0; frame < frameRate; frame++) x = camera.GetX(3000, 898, 1200, 1d / frameRate);
        return x;
    }
    Near(Move(30), Move(120), "Easing depended on display frame rate");
}
static void Invalid()
{
    var camera = new LyricsViewport();
    Near(camera.GetX(float.NaN, 898, 0, 0), 0, "Invalid width escaped validation");
    Near(camera.GetX(3000, 0, 0, 0), 0, "Empty viewport escaped validation");
    Near(camera.GetX(3000, 898, 5000, 0), 898 - LyricsViewport.EdgeInset - 3000, "Over-completed progress was not clamped");
    camera.Reset();
    float x = camera.GetX(3000, 898, 1000, 0);
    Near(camera.GetX(3000, 898, 1001, double.NaN), x, "Invalid elapsed time moved the camera");
}
