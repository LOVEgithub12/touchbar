using System.Globalization;
using SkiaSharp;
using WindowsInput.Native;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Gaming;
using WindowsTouchBar.Models;

namespace WindowsTouchBar.Views;

internal sealed class GameMonitorView : TouchBarView, IDisposable
{
    private readonly Button gameButton;
    private readonly SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI");
    private readonly Dictionary<string, SKBitmap> icons = new();
    private SKBitmap? icon;
    private TelemetrySnapshot snapshot = TelemetrySnapshot.Empty;
    public override ViewType Type => ViewType.Media;
    public event Action? MainRequested;
    public GameMonitorView(int width, int height) : base(width, height)
    {
        ComponentsGap = 20;
        gameButton = new Button(SKRect.Create(112, height - 4)) { FillColor = SKColors.Black, DownFillColor = new SKColor(38,38,40), CornerRadius = new SKPoint(9,9) };
        gameButton.OnTouchUp += (_, _) => MainRequested?.Invoke();
        Components.Add(gameButton); Components.Add(new Spacer(true));
        AddVolume("\uE74F",112,VirtualKeyCode.VOLUME_MUTE,false);
        AddVolume("\uE993",128,VirtualKeyCode.VOLUME_DOWN,true);
        AddVolume("\uE995",128,VirtualKeyCode.VOLUME_UP,true);
        Components.Add(new Spacer(12)); Update();
    }
    private void AddVolume(string text,int width,VirtualKeyCode key,bool repeat)
    {
        Components.Add(new MediaIconButton(SKRect.Create(width,Height-4)) { Text=text,FontFamily="Segoe MDL2 Assets",FontSize=36,
            FillColor=new SKColor(38,38,40),DownFillColor=new SKColor(85,85,90),CornerRadius=new SKPoint(9,9),TouchDownInterval=repeat,Command=new KeyPressCommand(key) });
    }
    public void SetSnapshot(GameDefinition? game, TelemetrySnapshot value)
    {
        snapshot=value; icon=null;
        if(game is null) return;
        if(!icons.TryGetValue(game.Id,out icon))
        {
            string path=Path.Combine(AppContext.BaseDirectory,game.Icon);
            if(File.Exists(path)) { icon=SKBitmap.Decode(path); if(icon is not null) icons[game.Id]=icon; }
        }
    }
    public override void Draw(SKCanvas canvas)
    {
        base.Draw(canvas);
        using var paint=new SKPaint { IsAntialias=true,Typeface=typeface,Color=SKColors.White,FilterQuality=SKFilterQuality.High };
        if(icon is not null) canvas.DrawBitmap(icon,SKRect.Create(gameButton.X+32,Height/2f-24,48,48),paint);
        float scale=(Width-420)/1588f;
        void Text(string text,float x,float size,SKColor? color=null,float maxWidth=0) {
            paint.TextSize=size;paint.Color=color??SKColors.White;
            if(maxWidth>0 && paint.MeasureText(text)>maxWidth*scale) paint.TextSize*=maxWidth*scale/paint.MeasureText(text);
            float baseline=Height/2f-(paint.FontMetrics.Ascent+paint.FontMetrics.Descent)/2;
            canvas.DrawText(text,x*scale,baseline,paint);
        }
        void Line(float x){paint.Color=new SKColor(48,48,52);canvas.DrawRect(x*scale,11,1,Height-22,paint);}
        var muted=new SKColor(181,187,197);
        Line(116);Line(690);Line(1030);
        Text("CPU",140,28,muted);Text(Format(snapshot.CpuTemperature,"F1","°C"),222,30,maxWidth:121);
        Text(Format(snapshot.CpuLoad,"F0","%"),353,28,maxWidth:75);Text(Format(snapshot.CpuClockMHz/1000,"F2"," GHz"),438,28,maxWidth:225);
        Text(Format(snapshot.Fps,"F0",""),712,37,new SKColor(139,230,168),91);Text("FPS",813,26);Text(Format(snapshot.FrameTimeMs,"F1"," ms"),887,26,muted,128);
        Text("GPU",1050,28,muted);Text(Format(snapshot.GpuTemperature,"F1","°C"),1136,30,maxWidth:121);
        Text(Format(snapshot.GpuLoad,"F0","%"),1267,28,maxWidth:78);Text(Format(snapshot.GpuClockMHz,"F0"," MHz"),1355,28,maxWidth:210);
    }
    internal static string Format(double? value,string format,string suffix) => value.HasValue && double.IsFinite(value.Value) && value>=0 ? value.Value.ToString(format,CultureInfo.InvariantCulture)+suffix : "—";
    public void Dispose() { foreach(var item in Components.OfType<IDisposable>())item.Dispose();foreach(var b in icons.Values)b.Dispose();typeface.Dispose(); }
}
