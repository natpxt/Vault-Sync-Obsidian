using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VaultSync.Core;

namespace VaultSync.App;

internal sealed record ConfigEdit(string Path, string Before, string After);
internal sealed class IntegrationWindow : Window
{
    private readonly List<ConfigEdit> edits = [];
    private readonly SyncConfig config;
    private readonly TextBox text = new() { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap };
    private readonly string? junction;
    private readonly string? oldJunctionTarget;
    private string reportSource = "";
    public IntegrationWindow(SyncConfig config, bool testMode = false)
    {
        this.config = config;
        Title = "Revisar integração · " + Program.Product; Width = 900; Height = 650; Ui.Style(this);
        var root = new DockPanel { Margin = new Thickness(10) }; Ui.Chrome(this, root, "Revisar integração · Vault Sync");
        void LocalizeReport() => text.Text = Strings.T(reportSource);
        Strings.Changed += LocalizeReport; Closed += (_, _) => Strings.Changed -= LocalizeReport;
        var header = Ui.Text("Referências externas · revisão antes de aplicar", 23, Ui.Accent); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var controls = Ui.Row(Ui.Button("Aplicar alterações revisadas", (_, _) => Apply()), Ui.Button("Fechar", (_, _) => Close())); DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls); root.Children.Add(text);
        if (testMode) { ((Button)controls.Children[0]).IsEnabled = false; SetReport("Prévia isolada de referências externas.\n\nOrigem: C:\\Users\\demo\\iCloudDrive\\Meu vault\nNova referência: C:\\Users\\demo\\Documents\\Meu vault\n\nNenhum arquivo externo foi lido ou alterado nesta demonstração."); return; }
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var path in new[] { Path.Combine(user, ".codex", "hooks.json"), Path.Combine(user, ".codex", "config.toml"), Path.Combine(user, ".codex", "AGENTS.md"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obsidian", "obsidian.json") })
        {
            if (!File.Exists(path)) continue;
            var before = File.ReadAllText(path); var after = ReplacePaths(before, config.CloudRoot, config.LocalRoot);
            if (after != before) edits.Add(new(path, before, after));
        }
        string candidate = Path.Combine(user, ".agents", "skills", "memory-sync");
        if (Directory.Exists(candidate))
        {
            var info = new DirectoryInfo(candidate);
            if (info.LinkTarget is { } target && Paths.Normalize(target).Equals(Paths.Normalize(Path.Combine(config.CloudRoot, ".agents", "skills", "memory-sync")), StringComparison.OrdinalIgnoreCase))
            { junction = candidate; oldJunctionTarget = target; }
        }
        var lines = new List<string> { "Aprovação necessária: estes arquivos pertencem a outros aplicativos.", "Feche o Obsidian e reinicie o Codex após a atualização. Hooks alterados podem exigir revisão humana no Codex; este aplicativo não altera confiança.", "", "Origem: " + config.CloudRoot, "Nova referência: " + config.LocalRoot, "" };
        foreach (var edit in edits)
        {
            lines.Add("ARQUIVO · " + edit.Path);
            var beforeLines = edit.Before.Split('\n'); var afterLines = edit.After.Split('\n');
            for (int i = 0; i < Math.Min(beforeLines.Length, afterLines.Length); i++)
                if (beforeLines[i] != afterLines[i]) { lines.Add("− " + beforeLines[i]); lines.Add("+ " + afterLines[i]); }
            lines.Add("");
        }
        if (junction != null) lines.Add("JUNÇÃO · " + junction + "\n→ " + Path.Combine(config.LocalRoot, ".agents", "skills", "memory-sync"));
        lines.Add("\nOs vínculos relativos dentro do vault serão preservados. Referências em documentos históricos não serão reescritas. Aplicativos com caminhos abertos devem ser reabertos na pasta local.");
        if (edits.Count == 0 && junction == null) lines.Add("Nenhuma referência externa correspondente foi encontrada.");
        SetReport(string.Join("\n", lines));
    }
    private void SetReport(string source) { reportSource = source; text.Text = Strings.T(source); }
    internal static string ReplacePaths(string text, string oldPath, string newPath)
    {
        var oldNorm = Paths.Normalize(oldPath); var newNorm = Paths.Normalize(newPath);
        foreach (var pair in new[] { (oldNorm.Replace("\\", "\\\\"), newNorm.Replace("\\", "\\\\")), (oldNorm.Replace('\\', '/'), newNorm.Replace('\\', '/')), (oldNorm, newNorm) })
            text = text.Replace(pair.Item1, pair.Item2, StringComparison.OrdinalIgnoreCase);
        return text;
    }
    private void Apply()
    {
        if (edits.Count == 0 && junction == null) return;
        if (Process.GetProcessesByName("Obsidian").Length > 0) { ThemedDialog.Info(this, "Feche o Obsidian antes de atualizar suas referências."); return; }
        if (!File.Exists(Path.Combine(config.LocalRoot, "AGENTS.md"))) { ThemedDialog.Info(this, "A cópia local ainda não contém AGENTS.md. Conclua a cópia verificada primeiro."); return; }
        if (!ThemedDialog.Confirm(this, "Aplicar as alterações exibidas? Os arquivos anteriores serão guardados em um backup local. Reinicie o Codex e revise seus hooks depois.")) return;
        string backup = Path.Combine(AppFiles.DataRoot, "integration-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            foreach (var edit in edits) if (File.ReadAllText(edit.Path) != edit.Before) throw new IOException("Arquivo alterado após a prévia: " + edit.Path);
            Directory.CreateDirectory(backup);
            for (int i = 0; i < edits.Count; i++) File.WriteAllText(Path.Combine(backup, i + ".before"), edits[i].Before);
            File.WriteAllText(Path.Combine(backup, "manifest.json"), JsonSerializer.Serialize(new { files = edits.Select((e, i) => new { e.Path, backup = i + ".before" }), junction, oldJunctionTarget }));
            foreach (var edit in edits)
            {
                Paths.CheckAncestors(edit.Path); string temp = edit.Path + ".vaultsync-new";
                File.WriteAllText(temp, edit.After, new UTF8Encoding(false));
                if (File.ReadAllText(edit.Path) != edit.Before) throw new IOException("Edição concorrente detectada: " + edit.Path);
                File.Move(temp, edit.Path, true);
                if (File.ReadAllText(edit.Path) != edit.After) throw new IOException("Falha na verificação: " + edit.Path);
            }
            if (junction != null)
            {
                string target = Path.Combine(config.LocalRoot, ".agents", "skills", "memory-sync");
                if (!Directory.Exists(target)) throw new IOException("Skill local ainda ausente; junção anterior preservada.");
                var info = new DirectoryInfo(junction);
                if (info.LinkTarget != oldJunctionTarget) throw new IOException("A junção mudou desde a prévia.");
                Directory.Delete(junction, false);
                try { CreateJunction(junction, target); }
                catch { if (!Directory.Exists(junction)) CreateJunction(junction, oldJunctionTarget!); throw; }
                if (new DirectoryInfo(junction).LinkTarget is not { } actual || !Paths.Normalize(actual).Equals(Paths.Normalize(target), StringComparison.OrdinalIgnoreCase)) throw new IOException("Junção não verificada.");
            }
            SetReport(reportSource + "\n\nAtualizações verificadas. Backup: " + backup + "\nReinicie os aplicativos e revise hooks no Codex.");
            edits.Clear();
        }
        catch (Exception ex) { ThemedDialog.Info(this, "Integração interrompida. Confira o relatório e os backups antes de repetir.\n" + ex.Message + "\nBackup: " + backup); }
    }
    private static void CreateJunction(string path, string target)
    {
        string script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null";
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-NonInteractive"); psi.ArgumentList.Add("-EncodedCommand"); psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(psi)!; string error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Não foi possível criar a junção: " + error);
    }
}
