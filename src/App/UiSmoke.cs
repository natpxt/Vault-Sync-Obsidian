using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VaultSync.Core;

namespace VaultSync.App;

internal static class UiSmoke
{
    public static void Run(string? output)
    {
        output ??= Path.Combine(Path.GetTempPath(), "vaultsync-ui-smoke"); Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var setup = new SetupWindow(); Capture(setup, Path.Combine(output, "installer.png")); setup.Close();
        var main = new MainWindow(testMode: true); Capture(main, Path.Combine(output, "main.png")); main.CloseSmoke();
        string fixtures = Path.Combine(output, "fixtures"); Directory.CreateDirectory(Path.Combine(fixtures, "local")); Directory.CreateDirectory(Path.Combine(fixtures, "cloud"));
        string l = Path.Combine(fixtures, "local", "Exemplo.md"), c = Path.Combine(fixtures, "cloud", "Exemplo.md");
        File.WriteAllText(l, "# Projeto\n\nPrazo: sexta-feira\n\n- Revisar orçamento\n"); File.WriteAllText(c, "# Projeto\n\nPrazo: segunda-feira\n\n- Revisar orçamento\n- Consultar equipe\n");
        var conflict = new Conflict("Exemplo.md", Scanner.Read(l), Scanner.Read(c), fixtures, "Edições offline nos dois computadores");
        var dialog = new ConflictWindow(new(Path.GetDirectoryName(l)!, Path.GetDirectoryName(c)!), conflict, "Windows de trabalho");
        Capture(dialog, Path.Combine(output, "conflict.png")); dialog.Close();
        if (ConflictWindow.MarkdownPreview("# Teste\n<script>alert(1)</script>\n![imagem](https://invalid.example/a)").Blocks.Count != 3) throw new InvalidOperationException("Preview inesperado.");
        using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
        File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { status = "passed", screens = 3, networkRenderer = false, workingSetMiB = process.WorkingSet64 / 1048576.0 }));
        app.Shutdown();
    }
    private static void Capture(Window window, string path)
    {
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.Measure(new Size(window.Width, window.Height)); window.Arrange(new Rect(0, 0, window.Width, window.Height)); window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
