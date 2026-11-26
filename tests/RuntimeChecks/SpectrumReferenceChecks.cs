using SkiaSharp;
using WindowsTouchBar.Audio;
using WindowsTouchBar.Lyrics;

internal static class SpectrumReferenceChecks
{
    public static void MiddleHarmonicGroup()
    {
        const int rate = 48000;
        var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
        float[] tones = { 490, 980, 1470, 1960 };
        for (int i = 0; i < rate * 4; i++)
        {
            float v = 0;
            for (int j = 0; j < tones.Length; j++)
                v += .06f / (j + 1) * MathF.Sin(2 * MathF.PI * tones[j] * i / rate);
            analyzer.Add(v, 0); analyzer.Add(-v, 1);
        }
        var axis = new SpectrumResponse(); var bins = analyzer.Latest.ToArray();
        foreach (float hz in tones)
            Require(bins.Where((v,i) => Math.Abs(axis.FrequencyAt(i,16500)-hz)<hz*.025f).Max() > .65f,
                $"Middle harmonic {hz} Hz is too short on the native display.");
        Require(bins.Where((v,i) => axis.FrequencyAt(i,16500)<180 || axis.FrequencyAt(i,16500)>5000).Max() < .015f,
            "A middle-only signal lifted the silent outer regions.");
        foreach (float hz in new[] { 730f, 1210f, 1710f })
            Require(bins.Where((v,i) => Math.Abs(axis.FrequencyAt(i,16500)-hz)<30).Max() < .03f,
                "Middle sensitivity filled the gaps between separate harmonic peaks.");
    }

    public static void VisibleParticleFlight()
    {
        var renderer = new SpectrumBackgroundRenderer();
        using var bitmap = new SKBitmap(972,56); using var canvas = new SKCanvas(bitmap);
        var input = Enumerable.Range(0,1024).Select(i => i >= 350 && i <= 650 ? .28f : 0).ToArray();
        void Step(float[] values, int frames)
        { for (int i=0;i<frames;i++) { canvas.Clear(SKColors.Black); renderer.Draw(canvas,972,56,values,1,1.0/60); } }
        int Bright(int yLimit)
        {
            int count=0;
            for(int x=340;x<610;x++) for(int y=8;y<yLimit;y++)
            { var c=bitmap.GetPixel(x,y); if(c.Red>=180 && c.Green>=160 && c.Blue>=180) count++; }
            return count;
        }
        Step(input,14); int lifted=Bright(35);
        Require(lifted >= 30, $"Rising audio did not launch clearly visible native pixels at least 4 px above the body: {lifted}.");
        Step(new float[1024],18); int held=Bright(39);
        Require(held >= 20, $"Particles disappeared before a visible 300 ms hold/fall: {held}.");
        Step(new float[1024],150);
        Require(!renderer.HasEnergy && bitmap.Pixels.All(c=>c==SKColors.Black), "Particle flight persisted in silence.");
        Step(input,240); int settled=Bright(35);
        Require(settled == 0, $"A steady tone kept launching unrelated particles: {settled}.");
        Require(Enumerable.Range(8,42).All(y => bitmap.GetPixel(100,y)==SKColors.Black),
            "Particles appeared in a frequency band without audio.");
        Console.WriteLine($"  Native airborne bright pixels: rise={lifted}, 300 ms residue={held}, steady={settled}.");
    }

    public static void IrregularEdgeStability()
    {
        var renderer = new SpectrumBackgroundRenderer();
        using var bitmap = new SKBitmap(972,56); using var canvas = new SKCanvas(bitmap);
        var input = Enumerable.Range(0,1024).Select(i => i >= 350 && i <= 470 ? .55f : 0).ToArray();
        void Step(int frames)
        { for(int i=0;i<frames;i++){canvas.Clear(SKColors.Black);renderer.Draw(canvas,972,56,input,1,1.0/60);} }
        Step(240);
        var edges=new List<double>();
        for(int y=29;y<49;y++)
        {
            double sum=0,weighted=0;
            for(int x=328;x<=335;x++) { double a=bitmap.GetPixel(x,y).Green/255.0;sum+=a;weighted+=a*x; }
            Require(sum>0,"A steep flank disappeared."); edges.Add(weighted/sum);
        }
        double variation=edges.Max()-edges.Min();
        Require(variation>.12 && variation<2, $"Flank grain was smooth or displaced the peak excessively: {variation} px.");
        var first=bitmap.Pixels;Step(60);
        Require(first.Zip(bitmap.Pixels,(a,b)=>Math.Abs(a.Green-b.Green)).Average()<.02,
            "A steady waveform's edge moved on its own and could flicker.");
        Require(bitmap.Pixels.Take(972*8).All(c=>c==SKColors.Black) && bitmap.Pixels.Skip(972*55).All(c=>c==SKColors.Black),
            "Irregular contour escaped the display bounds.");
        Console.WriteLine($"  Steep-flank contour variation={variation:F3} native px; stationary input is stable.");
    }

    public static void BassDominatedMix()
    {
        const int rate = 48000;
        var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
        long samples = 0;
        void Feed(double seconds, float bass, bool upper = true)
        {
            for (int i = 0; i < rate * seconds; i++, samples++)
            {
                double t = samples / (double)rate;
                float v = bass * (float)Math.Sin(2 * Math.PI * 94 * t);
                if (upper) v += .015f * (float)Math.Sin(2 * Math.PI * 750 * t) + .003f * (float)Math.Sin(2 * Math.PI * 6000 * t);
                analyzer.Add(v, 0); analyzer.Add(-v, 1);
            }
        }
        var axis = new SpectrumResponse();
        float Peak(float hz) => analyzer.Latest.ToArray().Where((v,i) => Math.Abs(axis.FrequencyAt(i,16500)-hz)<hz*.06f).Max();
        Feed(12, .5f);
        float before = Peak(6000);
        Require(Peak(94) > .45f && Peak(750) > .45f && before > .45f,
            $"Present mid/treble notes vanished under a bass-heavy mix: {Peak(94)}, {Peak(750)}, {before}.");
        Feed(.3, 3f);
        Require(Math.Abs(Peak(6000) - before) < .04f,
            "A bass accent suppressed an unchanged treble note through global gain.");
        var bins = analyzer.Latest.ToArray();
        Require(bins.Where((v,i) => axis.FrequencyAt(i,16500)>2200 && axis.FrequencyAt(i,16500)<4500).Max() < .04f,
            "Regional gain filled a deep valley with unrelated frequency energy.");
        Feed(.5, .5f, false);
        Require(Peak(6000) == 0 && Peak(750) == 0,
            "Removing mid/treble audio left invented peaks in those regions.");
        Console.WriteLine($"  Bass-heavy mix: treble peak={before:F3}; independent bass accent and empty valley verified.");
    }

    public static void QuietNoiseDoesNotGrow()
    {
        const int rate = 48000;
        var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
        var random = new Random(104);
        float maximum = 0;
        for (int i = 0; i < rate * 12; i++)
        {
            float value = ((float)random.NextDouble() * 2 - 1) * .0001f;
            analyzer.Add(value, 0); analyzer.Add(-value, 1);
            if (i % 800 == 799) maximum = Math.Max(maximum, analyzer.Latest.Span.ToArray().Max());
        }
        Require(maximum < .005f, $"A silent background grew into visible peaks: {maximum}.");
    }

    public static void FloatingPointHeadroom()
    {
        const int rate = 48000;
        var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
        for (int i = 0; i < rate * 3; i++)
        {
            float sample = 3.5f * (float)Math.Sin(2 * Math.PI * 750 * i / rate);
            analyzer.Add(sample, 0); analyzer.Add(-sample, 1);
        }
        float[] bins = analyzer.Latest.ToArray();
        var axis = new SpectrumResponse();
        float fundamental = 0, harmonics = 0;
        for (int i = 0; i < bins.Length; i++)
        {
            float hz = axis.FrequencyAt(i, 16500);
            if (Math.Abs(hz - 750) < 70) fundamental = Math.Max(fundamental, bins[i]);
            if (Math.Abs(hz - 2250) < 100 || Math.Abs(hz - 3750) < 100) harmonics = Math.Max(harmonics, bins[i]);
        }
        Require(fundamental > .25f && harmonics < .025f,
            $"Full-scale floating-point input was clipped or lost: fundamental={fundamental}, harmonics={harmonics}.");
        for (int i = 0; i < rate / 2; i++)
        {
            analyzer.Add(i % 3 == 0 ? float.NaN : float.PositiveInfinity, 0);
            analyzer.Add(i % 3 == 0 ? float.MaxValue : float.NegativeInfinity, 1);
        }
        Require(analyzer.Latest.ToArray().All(v => float.IsFinite(v) && v == 0),
            "Invalid samples poisoned the spectrum or prevented silence.");
    }

    public static void InputLevelAdaptation()
    {
        const int rate = 48000;
        float[] Render(float gain)
        {
            var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
            for (int i = 0; i < rate * 4; i++)
            {
                float sample = gain * (float)(Math.Sin(2 * Math.PI * 750 * i / rate) + .3 * Math.Sin(2 * Math.PI * 6000 * i / rate));
                analyzer.Add(sample, 0); analyzer.Add(-sample, 1);
            }
            return analyzer.Latest.ToArray();
        }
        var moderate = Render(.35f); var hot = Render(3.5f);
        float difference = moderate.Zip(hot, (a,b) => Math.Abs(a-b)).Max();
        Require(difference < .025f && hot.Max() > .25f,
            $"Changing the sustained input gain changed its settled frequency contour: {difference}.");
        var quiet = Render(.0035f);
        Require(quiet.Average() < moderate.Average() * .7f,
            "Input adaptation amplified a quiet passage to full music height.");
    }

    public static void AdaptationPreservesBeatAndSilence()
    {
        const int rate = 48000, hop = 800;
        var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
        float[] Step(int hops, float amplitude)
        {
            for (int frame = 0; frame < hops; frame++)
                for (int i = 0; i < hop; i++)
                {
                    float sample = amplitude * (float)Math.Sin(2 * Math.PI * 750 * i / rate);
                    analyzer.Add(sample, 0); analyzer.Add(sample, 1);
                }
            return analyzer.Latest.ToArray();
        }
        var steady = Step(240, .5f);
        var beat = Step(6, 1f);
        Require(beat.Max() > steady.Max() + .04f, "Level adaptation flattened a short musical accent.");
        Step(180, .5f);
        var before = analyzer.Latest.ToArray();
        var silence = Step(240, 0);
        Require(silence.All(v => v == 0), "Silence retained a spectrum or raised its sensitivity.");
        var resumed = Step(12, .5f);
        Require(before.Zip(resumed,(a,b)=>Math.Abs(a-b)).Max() < .025f,
            $"A silent gap increased gain and inflated the resumed music: max difference={before.Zip(resumed,(a,b)=>Math.Abs(a-b)).Max()}.");
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    public static void AudioDrivenPeaks()
    {
        using var bitmap = new SKBitmap(972, 56);
        using var canvas = new SKCanvas(bitmap);
        var renderer = new SpectrumBackgroundRenderer();
        float[] bins = new float[512];
        foreach (int center in new[] { 95, 338, 179 })
        {
            for (int i = 0; i < bins.Length; i++) bins[i] = .02f + .68f * MathF.Exp(-MathF.Pow((i - center) / 11f, 2));
            for (int j = 0; j < 50; j++) { canvas.Clear(SKColors.Black); renderer.Draw(canvas, 972, 56, bins, 1, 1.0 / 60); }
            int[] body = new int[972];
            for (int x = 0; x < 972; x++)
                for (int y = 7; y < 54; y++)
                {
                    var color = bitmap.GetPixel(x, y);
                    if (color.Green > 235 && color.Red < 220) body[x]++;
                }
            int peak = Array.IndexOf(body, body.Max());
            Require(Math.Abs(peak - center * 971f / 511) < 14 && body.Max() > 10,
                "A peak did not follow the supplied frequency; a stored silhouette may have replaced it.");
            Require(body.Where((v, x) => Math.Abs(x - center * 971f / 511) > 100).All(v => v == 0),
                "Unrelated frequency peaks were invented outside the input tone.");
        }
    }

    public static void FrameCadence()
    {
        float[] bins = Enumerable.Range(0, 512).Select(i => .05f + .6f * MathF.Pow(MathF.Sin(i * .043f), 2)).ToArray();
        SKColor[] Render(int frames, double dt)
        {
            var renderer = new SpectrumBackgroundRenderer();
            using var bitmap = new SKBitmap(972, 56);
            using var canvas = new SKCanvas(bitmap);
            for (int i = 0; i < frames; i++) { canvas.Clear(SKColors.Black); renderer.Draw(canvas, 972, 56, bins, 1, dt); }
            return bitmap.Pixels;
        }
        // The longer-lived launched glints are transient. Observe the settled
        // tone after they land, using identical three-second histories.
        var a = Render(180, 1.0 / 60); var b = Render(90, 1.0 / 30);
        Require(a.Zip(b, (x, y) => Math.Abs(x.Red - y.Red) + Math.Abs(x.Green - y.Green) + Math.Abs(x.Blue - y.Blue)).Average() < .05,
            "Changing render cadence changed the steady visual result.");
    }

    public static void CrestMemory()
    {
        using var bitmap = new SKBitmap(972, 56);
        using var canvas = new SKCanvas(bitmap);
        var renderer = new SpectrumBackgroundRenderer();
        float[] bins = Enumerable.Range(0, 512).Select(i => .15f + .55f * MathF.Pow(MathF.Sin(i * .018f), 2)).ToArray();
        void Draw(float[] input, double dt)
        {
            canvas.Clear(SKColors.Black);
            renderer.Draw(canvas, 972, 56, input, 1, dt);
        }
        for (int i = 0; i < 60; i++) Draw(bins, 1.0 / 60);
        var silent = new float[512];
        double Centroid()
        {
            double ysum = 0, sum = 0;
            for (int x = 55; x < 100; x++)
            {
                // Exclude the filled body, whose pale lower part is also white.
                // A slower release keeps it visible during the first observation.
                int bodyTop = 46;
                for (int y = 7; y < 54; y++)
                {
                    var c = bitmap.GetPixel(x, y);
                    if (c.Green > 235 && c.Green > c.Red + 12) { bodyTop = y; break; }
                }
                for (int y = 7; y < Math.Min(46, bodyTop - 1); y++)
                {
                    var color = bitmap.GetPixel(x, y);
                    if (color.Red < 70 || color.Red < color.Green - 8) continue;
                    sum += color.Red; ysum += color.Red * y;
                }
            }
            Require(sum > 0, "No local crest residue remained after a falling wave.");
            return ysum / sum;
        }
        for (int i = 0; i < 5; i++) Draw(silent, 1.0 / 60);
        double held = Centroid();
        // Allow the deliberately extended 200–320 ms hold, then observe fall.
        for (int i = 0; i < 36; i++) Draw(silent, 1.0 / 60);
        double fallen = Centroid();
        Require(fallen > held + 2, $"Crest memory did not fall toward its local waveform: held={held:F2}, fallen={fallen:F2}.");
        for (int i = 0; i < 120; i++) Draw(silent, 1.0 / 60);
        Require(!renderer.HasEnergy && bitmap.Pixels.All(c => c == SKColors.Black), "Crest memory survived silence indefinitely.");
        renderer.Reset(); Draw(bins, 0);
        Require(bitmap.Pixels.All(c => c == SKColors.Black), "Reset left stale pixels or a new crest appeared without elapsed time.");
    }

    public static void CalibratedAudio()
    {
        foreach (int rate in new[] { 44100, 48000 })
        {
            float[] Tone(double hz, bool opposite)
            {
                var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
                for (int i = 0; i < rate / 2; i++)
                {
                    float value = (float)(.14 * Math.Sin(2 * Math.PI * hz * i / rate));
                    analyzer.Add(value, 0); analyzer.Add(opposite ? -value : value, 1);
                }
                float[] result = analyzer.Latest.ToArray();
                Require(result.Length == 1024 && result.All(float.IsFinite), "Invalid calibrated FFT output.");
                for (int i = 0; i < rate; i++) { analyzer.Add(0, 0); analyzer.Add(0, 1); }
                Require(analyzer.Latest.ToArray().All(v => v < .0001f), "The calibrated analyzer retained a silent tone.");
                return result;
            }
            float[] bass = Tone(117.1875, false), opposite = Tone(117.1875, true), treble = Tone(6000, true);
            Require(bass.Zip(opposite, (a, b) => Math.Abs(a - b)).Max() < .0001f,
                "Opposite stereo phases cancelled the calibrated spectrum.");
            int low = Array.IndexOf(bass, bass.Max()), high = Array.IndexOf(treble, treble.Max());
            Require(low > 80 && low < 240 && high > 760 && high < 940 && bass.Max() > .25 && treble.Max() > .20,
                $"Frequency calibration put tones in the wrong regions at {rate} Hz: {low}, {high}.");
            Require(bass[400..680].Max() < .04 && treble[240..520].Max() < .04,
                "Calibration created a waveform in frequencies absent from the audio.");
        }
        // Endpoint formats can have a Nyquist limit below the calibrated axis.
        // Direct FFT sampling must stay inside that format's available bins.
        foreach (int rate in new[] { 16000, 22050, 32000 })
        {
            var analyzer = new AudioSpectrumAnalyzer(rate, 1, referenceSpectrum: true);
            for (int i = 0; i < rate / 3; i++)
                analyzer.Add(.14f * MathF.Sin(2 * MathF.PI * 1000 * i / rate), 0);
            float[] output = analyzer.Latest.ToArray();
            Require(output.Length == 1024 && output.All(float.IsFinite) && output.Max() > .15f,
                $"Direct FFT sampling exceeded the endpoint's frequency range at {rate} Hz.");
        }
    }

    public static void IndependentPulses()
    {
        const int rate = 48000, hop = 800;
        var analyzer = new AudioSpectrumAnalyzer(rate, 2, referenceSpectrum: true);
        var lows = new List<float>(); var highs = new List<float>();
        int lowStart = 12, lowEnd = 24, highStart = 42, highEnd = 51;
        for (int frame = 0; frame < 90; frame++)
        {
            for (int j = 0; j < hop; j++)
            {
                double t = (frame * hop + j) / (double)rate;
                float sample = (frame >= lowStart && frame < lowEnd ? .14f * (float)Math.Sin(2 * Math.PI * 117.1875 * t) : 0)
                    + (frame >= highStart && frame < highEnd ? .14f * (float)Math.Sin(2 * Math.PI * 6000 * t) : 0);
                analyzer.Add(sample, 0); analyzer.Add(-sample, 1);
            }
            float[] values = analyzer.Latest.ToArray();
            lows.Add(values[170..250].Max()); highs.Add(values[820..900].Max());
        }
        Require(lows.Take(lowStart).All(v => v == 0) && highs.Take(lowStart).All(v => v == 0), "Silence generated a pre-beat wave.");
        float bassPeak = lows.Max(), treblePeak = highs.Max();
        int bassAttack = lows.FindIndex(lowStart, v => v > bassPeak * .8f) - lowStart;
        int trebleAttack = highs.FindIndex(highStart, v => v > treblePeak * .8f) - highStart;
        Require(bassPeak > .3f && treblePeak > .3f && bassAttack >= 0 && bassAttack <= 3 && trebleAttack >= 0 && trebleAttack <= 1,
            $"Music attack was delayed: bass={bassAttack} hops; treble={trebleAttack} hops.");
        Require(highs.Skip(lowStart).Take(lowEnd - lowStart).Max() < .04f && lows.Skip(highStart).Take(highEnd - highStart).Max() < .04f,
            "A beat in one band lifted unrelated frequencies.");
        // The preserved bass window spans 128 ms at 48 kHz; wait eight hops
        // before inspecting the gap, and do not include the next tone onset.
        float bassTail = lows.Skip(lowEnd + 8).Take(highStart - lowEnd - 8).Max();
        float trebleTail = highs.Skip(highEnd + 3).Max();
        Require(bassTail < .0001f && trebleTail < .0001f,
            $"Audio analysis retained a pulse after its FFT window had emptied: {bassTail}, {trebleTail}.");
        Console.WriteLine($"  Pulse onset to 80%: bass {(bassAttack + 1) * 1000.0 / 60:F1} ms, treble {(trebleAttack + 1) * 1000.0 / 60:F1} ms (analysis-hop observations).");
    }

    public static void TransientCadence()
    {
        // Observe rendered body pixels, excluding the independently held white
        // crest markers. Compare equal elapsed times at different frame rates.
        int[] Observe(int fps)
        {
            var renderer = new SpectrumBackgroundRenderer();
            using var bitmap = new SKBitmap(972, 56);
            using var canvas = new SKCanvas(bitmap);
            var input = Enumerable.Range(0, 1024).Select(i => .68f * MathF.Exp(-MathF.Pow((i - 512) / 20f, 2))).ToArray();
            void Draw(float[] data)
            {
                canvas.Clear(SKColors.Black);
                renderer.Draw(canvas, 972, 56, data, 1, 1.0 / fps);
            }
            int Height()
            {
                for (int y = 7; y < 54; y++)
                {
                    var c = bitmap.GetPixel(486, y);
                    if (c.Green > 235 && c.Green > c.Red + 12) return 54 - y;
                }
                return 0;
            }
            for (int i = 0; i < fps / 10; i++) Draw(input);
            int attack = Height();
            Array.Clear(input);
            for (int i = 0; i < fps / 10; i++) Draw(input);
            int release = Height();
            for (int i = 0; i < fps * 4 / 5; i++) Draw(input);
            int settled = Height();
            return new[] { attack, release, settled };
        }
        var at30 = Observe(30); var at60 = Observe(60); var at120 = Observe(120);
        Require(at60[0] > 25 && at60[1] > at60[0] / 4 && at60[1] < at60[0] * .75 && at60[2] == 0,
            $"A musical accent was delayed, disappeared in a flash, or retained a silent waveform: {string.Join(',', at60)}.");
        Require(at30.Zip(at60, (a,b) => Math.Abs(a-b)).Max() <= 1 && at120.Zip(at60, (a,b) => Math.Abs(a-b)).Max() <= 1,
            "Frame rate changed the time of the body attack or release.");
    }

    public static void FlickerSuppression()
    {
        // Measure visible body motion, not envelope constants. A 120 BPM pulse
        // must remain distinct while rapid inter-frame fluctuations are damped.
        (int range, double peakLag) Observe(double hz)
        {
            const int fps = 60;
            var renderer = new SpectrumBackgroundRenderer();
            using var bitmap = new SKBitmap(972, 56);
            using var canvas = new SKCanvas(bitmap);
            float[] input = new float[1024];
            var samples = new List<(double time, int height)>();
            for (int n = 0; n < fps * 4; n++)
            {
                double t = (n + 1.0) / fps;
                float level = .40f + .23f * (float)Math.Sin(2 * Math.PI * hz * t);
                for (int i = 0; i < input.Length; i++)
                    input[i] = level * MathF.Exp(-MathF.Pow((i - 512) / 20f, 2));
                canvas.Clear(SKColors.Black);
                renderer.Draw(canvas, 972, 56, input, 1, 1.0 / fps);
                int h = 0;
                for (int y = 7; y < 54; y++)
                {
                    var c = bitmap.GetPixel(486, y);
                    if (c.Green > 235 && c.Green > c.Red + 12) { h = 54 - y; break; }
                }
                if (t > 2) samples.Add((t, h));
            }
            double peakTime = 2 + .25 / hz;
            var cycle = samples.Where(s => s.time >= 2 && s.time < 2 + 1 / hz).ToArray();
            int highest = cycle.Max(s => s.height);
            double lag = cycle.First(s => s.height == highest).time - peakTime;
            return (samples.Max(s => s.height) - samples.Min(s => s.height), lag);
        }
        var beat = Observe(2); var rapid = Observe(12);
        Require(beat.range >= 9 && beat.peakLag >= -.025 && beat.peakLag <= .10,
            $"Smoothing lost or delayed a musical pulse: range={beat.range}px, lag={beat.peakLag:F3}s.");
        Require(rapid.range < beat.range * .6,
            $"Rapid fluctuations still flash like beats: rapid={rapid.range}px, beat={beat.range}px.");
        Console.WriteLine($"  Visible 2 Hz beat range={beat.range}px; 12 Hz fluctuation={rapid.range}px; peak lag={beat.peakLag * 1000:F1} ms.");
    }
}
