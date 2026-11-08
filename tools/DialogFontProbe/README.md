# Explorer dialog Chinese labels

The app uses `SkiaSharp.Elements.Text`, whose default font on this system is **Verdana**. This font returns glyph ID 0 for all six characters in `确定取消应用`; the library draws missing-glyph boxes without automatic CJK fallback. The text is correctly read as Unicode; this is a font selection issue rather than a USB/display encoding issue.

The independently rendered `default-vs-yahei.png` has default Verdana on the first row and Microsoft YaHei UI on the second. All three two-character labels are boxes in the first row and readable Chinese in the second.

Probe output:

```text
Elements default family: Verdana
Requested=Verdana; actual=Verdana; glyphs 确定取消应用=0,0,0,0,0,0
Requested=Microsoft YaHei UI; actual=Microsoft YaHei UI; glyphs 确定取消应用=2375,1556,1352,2179,1665,2304
```

Recommended fix: set the default text family when `Button.GetText()` creates its text element to `Microsoft YaHei UI`. Media and custom icon buttons continue to explicitly select `Segoe MDL2 Assets`. Ordinary two-character property-button labels fit the existing 350×56 bounds.

The probe reads font data and writes an image only. It does not open USB devices, change drivers, send input, or invoke property buttons.

```powershell
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
& $dotnet run --project tools\DialogFontProbe\DialogFontProbe.csproj -c Release
```
