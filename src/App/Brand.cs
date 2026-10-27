using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VaultSync.App;

internal static class Brand
{
    // Geometry is the approved 01-orbita.svg, on its original 64 × 64 grid.
    internal static DrawingGroup Drawing(Brush? ink = null)
    {
        ink ??= Theme.Get("Accent");
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(null, new Pen(ink, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M9 28A24 24 0 0 1 45 12M55 36A24 24 0 0 1 19 52")));
        group.Children.Add(new GeometryDrawing(ink, null, Geometry.Parse("M49 18L47 7L38 14ZM15 46L17 57L26 50Z")));
        Facet("M33 20L26 24 22 31 29 34Z", 1);
        Facet("M33 20L41 28 37 43 29 34Z", .72);
        Facet("M22 31L26 40 37 43 29 34Z", .45);
        return group;
        void Facet(string path, double opacity)
        {
            var facet = new DrawingGroup { Opacity = opacity }; facet.Children.Add(new GeometryDrawing(ink, null, Geometry.Parse(path))); group.Children.Add(facet);
        }
    }
    internal static Image Logo(double size = 32) => new() { Source = new DrawingImage(Drawing()), Width = size, Height = size, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) };
    internal static BitmapSource Bitmap(int size, Brush? ink = null, Brush? badge = null)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
            dc.DrawDrawing(Drawing(ink));
            if (badge != null) { dc.DrawEllipse(Theme.Get("Background"), null, new Point(52, 52), 10, 10); dc.DrawEllipse(badge, null, new Point(52, 52), 7, 7); }
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
    }
    internal static void ExportIcon(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path); WriteIcon(file, [16, 24, 32, 48, 64, 128, 256], new SolidColorBrush(Color.FromRgb(120, 82, 199)));
    }
    private static void WriteIcon(Stream stream, int[] sizes, Brush ink, Brush? badge = null)
    {
        var frames = sizes.Select(size => { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap(size, ink, badge))); using var png = new MemoryStream(); encoder.Save(png); return png.ToArray(); }).ToArray();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length; }
        foreach (var frame in frames) writer.Write(frame);
    }
    internal static System.Drawing.Icon TrayIcon(string state)
    {
        Brush badge = state switch { "active" => Theme.Get("Success"), "paused" => Theme.Get("Warning"), "error" => Theme.Get("Danger"), _ => Theme.Get("Muted") };
        using var stream = new MemoryStream(); WriteIcon(stream, [16, 32], Theme.Get("Accent"), badge); stream.Position = 0;
        using var icon = new System.Drawing.Icon(stream); return (System.Drawing.Icon)icon.Clone();
    }
}
