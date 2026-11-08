namespace WindowsTouchBar.Lyrics;

// A render-only camera for an already measured line. The caller supplies the
// same sung width used to paint karaoke, so scrolling and coloring cannot drift.
internal sealed class LyricsViewport
{
    // The caller already reserves a 15px fade on both sides of this viewport.
    public const float EdgeInset = 0;
    public const float FocusFraction = 0.48f;
    public const double SmoothingSeconds = 0.12;

    private bool initialized;
    private float currentX;
    private float previousTextWidth;
    private float previousViewportWidth;

    public void Reset() => initialized = false;

    // Use reset for a new track/line or a seek, including a seek within the same
    // line. elapsedSeconds is elapsed render time, not the playback position.
    // The returned X is relative to the supplied content viewport. For a label
    // with 15px outer padding, pass label.Width - 30 and draw at 15 + GetX(...).
    // Untimed song titles should use a separate browsing/scrolling policy.
    public float GetX(float textWidth, float viewportWidth, float sungWidth,
        double elapsedSeconds, bool reset = false)
    {
        if (!float.IsFinite(textWidth) || !float.IsFinite(viewportWidth) ||
            !float.IsFinite(sungWidth) || textWidth < 0 || viewportWidth <= 0)
        {
            Reset();
            return 0;
        }

        float inset = Math.Min(EdgeInset, viewportWidth / 4);
        if (textWidth <= viewportWidth - inset * 2)
        {
            Reset();
            return (viewportWidth - textWidth) / 2;
        }

        sungWidth = Math.Clamp(sungWidth, 0, textWidth);
        float minimumX = viewportWidth - inset - textWidth;
        float maximumX = inset;
        float targetX = Math.Clamp(viewportWidth * FocusFraction - sungWidth,
            minimumX, maximumX);
        bool geometryChanged = textWidth != previousTextWidth || viewportWidth != previousViewportWidth;

        if (!initialized || reset || geometryChanged)
        {
            // Changes in meaning or geometry must not pan through the old line.
            currentX = targetX;
            initialized = true;
        }
        else
        {
            double elapsed = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 0.25) : 0;
            double fraction = 1 - Math.Exp(-elapsed / SmoothingSeconds);
            float smoothed = currentX + (targetX - currentX) * (float)fraction;

            // Small player-clock corrections cannot make a sung line oscillate.
            currentX = Math.Min(currentX, smoothed);

            // Fast singing can outrun the easing curve. Keep the current front
            // readable instead of enforcing a speed cap that hides it offscreen.
            currentX = Math.Min(currentX, viewportWidth - inset - sungWidth);
            currentX = Math.Clamp(currentX, minimumX, maximumX);
        }

        previousTextWidth = textWidth;
        previousViewportWidth = viewportWidth;
        return currentX;
    }
}
