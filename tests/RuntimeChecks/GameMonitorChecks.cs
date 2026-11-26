using SkiaSharp;
using WindowsTouchBar.Components;
using WindowsTouchBar.Gaming;
using WindowsTouchBar.Views;

internal static class GameMonitorChecks
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static GameDefinition Game(string id="lol") => new(id,id,"Resources/Games/sekiro.png",new[]{"C:/Games/Game/League of Legends.exe"});
    public static void Detection()
    {
        var catalog=new GameCatalog(new[]{Game(),new GameDefinition("zelda","Zelda","",new[]{"C:/Cemu/Cemu.exe"},new[]{"Breath of the Wild"})});
        Require(catalog.Match("C:/Games/Game/League of Legends.exe","")?.Id=="lol","Game path did not match");
        Require(catalog.Match("C:/Other/League of Legends.exe","")==null,"Same filename in different location matched");
        foreach(string name in new[]{"LeagueClient.exe","LeagueClientUx.exe","RiotClientServices.exe"})Require(catalog.Match("C:/Games/"+name,"")==null,"Launcher matched");
        Require(catalog.Match("C:/Cemu/Cemu.exe","Cemu 2.0")==null,"Idle emulator matched");
        Require(catalog.Match("C:/Cemu/Cemu.exe","Cemu - Breath of the Wild")?.Id=="zelda","Playing Zelda not matched");
    }
    public static void Navigation()
    {
        double now=0;var nav=new GameNavigation(()=>now);var game=Game();
        nav.SetForeground(game,10,(IntPtr)100);now=1.9;nav.Tick();Require(nav.ActiveGame==null,"Entered before debounce");
        now=2;Require(nav.Tick()&&nav.ActivePid==10,"Did not enter game");
        Require(nav.OpenMain(),"Main did not open");nav.TouchDown();now=20;nav.Tick();Require(nav.MainActive,"Returned while held");
        nav.TouchUp();now=23.9;nav.Tick();Require(nav.MainActive,"Returned before four seconds");now=24;nav.Tick();Require(!nav.MainActive,"Failed to return");
        nav.OpenMain();nav.SetForeground(null,20,(IntPtr)200);Require(!nav.MainActive,"Switch away kept temporary main");now=26.9;nav.Tick();Require(nav.ActiveGame!=null,"Leave debounce too short");now=27;nav.Tick();Require(nav.ActiveGame==null,"Failed to leave");
        Require(ForegroundViewPriority.Select(true,false,false,true,true,true,gameActive:true)==ForegroundViewSelection.FunctionPage,"Game stole explicit F page");
        Require(ForegroundViewPriority.Select(false,false,false,true,true,true,gameActive:true)==ForegroundViewSelection.GameMonitor,"Music stole game page");
        Require(ForegroundViewPriority.Select(false,false,false,true,true,true,gameActive:true,gameMainActive:true)==ForegroundViewSelection.DefaultMedia,"Game icon failed to open main over music");
    }
    public static void Frames()
    {
        var frames=new PresentMonFrames();
        frames.Add("Application,ProcessID,SwapChainAddress,MsBetweenPresents",10,0);
        for(int i=0;i<120;i++)frames.Add("\"game, quoted.exe\",10,0x1,8.333333",10,i/120.0);
        for(int i=0;i<60;i++)frames.Add("game.exe,10,0x2,16.666667",10,i/60.0);
        for(int i=0;i<300;i++)frames.Add("launcher.exe,20,0x3,1",10,.5);
        var value=frames.Read(1);Require(Math.Abs(value.Fps!.Value-120)<.01,"Summed swapchains or another PID");
        Require(frames.Read(4).Fps==null,"Retained stale FPS after no frames");
        Require(PresentMonFrames.ParseCsv("\"a\"\"b,c\",x")[0]=="a\"b,c","CSV quote handling");
        frames = new();
        frames.Add("Application,ProcessID,SwapChainAddress,msBetweenPresents,QPCTime",10,100);
        string ticks = (100.0 * System.Diagnostics.Stopwatch.Frequency).ToString(System.Globalization.CultureInfo.InvariantCulture);
        frames.Add("game,10,1,10,"+ticks,10,100.1);frames.Add("game,10,1,10,"+ticks,10,100.1);
        Require(frames.Read(100.2).Fps == 100,"Official legacy CSV casing/QPC rejected");
        frames.Add("game,10,1,1,"+ticks,10,104);
        Require(frames.Read(104).Fps == null,"Buffered old CSV revived stale FPS");
        frames = new();frames.Add("Application,ProcessID,SwapChainAddress,MsBetweenPresents,TimeInQPC",10,100);
        frames.Add("game,10,1,10,"+ticks,10,102);frames.Add("game,10,1,10,"+ticks,10,102);
        Require(frames.Read(102.8).Fps==100,"Normal ETW batching caused a blank FPS");
        Require(frames.Read(103.1).Fps==null,"Stopped frames did not expire");
    }
    public static void Layout()
    {
        foreach(int width in new[]{2008,2170})
        {
            using var view=new GameMonitorView(width,60);
            var controls=view.Components.OfType<MediaIconButton>().ToArray();
            Require(controls.Select(c=>c.Text).SequenceEqual(new[]{"\uE74F","\uE993","\uE995"}),"Volume icons changed");
            Require(controls[0].X==width-420&&controls[^1].X+controls[^1].Width<=width-12,"Volume geometry wrong");
            int count=0;view.MainRequested+=()=>count++;
            var button=view.Components.OfType<Button>().First();button.TouchMove(default);button.TouchUp(default);Require(count==0,"Hover triggered page");
            button.TouchDown(default);button.TouchUp(default);Require(count==1,"Icon tap did not trigger once");
            view.SetSnapshot(Game("sekiro"),new TelemetrySnapshot(70.1,17,3800,73,16,1600,120,8.3));
            using var bitmap=new SKBitmap(width,60);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Black);view.Draw(canvas);
            using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory,$"game-monitor-{width}.png"),data.ToArray());
            view.SetSnapshot(Game(),TelemetrySnapshot.Empty);canvas.Clear(SKColors.Black);view.Draw(canvas);
            view.SetSnapshot(Game(),new TelemetrySnapshot(105.5,100,5500,101.2,100,2750,1234,1000));canvas.Clear(SKColors.Black);view.Draw(canvas);
            using var extreme=SKImage.FromBitmap(bitmap);using var extremeData=extreme.Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory,$"game-monitor-extreme-{width}.png"),extremeData.ToArray());
            Require(GameMonitorView.Format(null,"F1","°C")=="—","Missing sensor forged zero");
        }
    }
}
