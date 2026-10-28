using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VaultSync.Core;
using VaultSync.Updates;

namespace VaultSync.App;

internal sealed partial class MainWindow
{
    private readonly TextBlock updateStatus = Ui.Text("Ainda não verificado.", 12, Ui.Muted);
    private readonly TextBlock updateLastCheck = Ui.Text("", 11, Ui.Muted);
    private readonly CheckBox automaticUpdates = new() { Content = "Verificar atualizações automaticamente", IsChecked = true };
    private readonly CancellationTokenSource updateStop = new();
    private CancellationTokenSource? updateDownload;
    private Button updateCheckButton = null!, updateOfferButton = null!, updateCancelButton = null!;
    private UpdateStore updateStore = null!;
    private GitHubUpdates updater = null!;
    private UpdateRelease? availableUpdate;
    private bool updateBusy, updateInstalling, updateChecksStarted;
    private bool canInstallUpdate;
    private string updateStatusSource = "Ainda não verificado.";
    private string? dismissedUpdate;
    private string? notifiedUpdate;
    private ResumeState? resumeState;
    private Border updateBanner = null!;
    private TextBlock updateBannerText = null!;
    private Button updateBannerButton = null!;
    private UIElement BuildUpdateBanner()
    {
        updateBannerText = Ui.Text("", 12, Ui.Accent); updateBannerText.Tag = "UserContent"; updateBannerText.Margin = new Thickness(0, 3, 14, 5); updateBannerText.VerticalAlignment = VerticalAlignment.Center;
        updateBannerButton = Ui.Button("Ver atualização disponível", (_, _) => ShowUpdateOffer());
        updateBanner = new Border { Child = Ui.Row(updateBannerText, updateBannerButton), Background = Ui.Surface, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        return updateBanner;
    }

    private UIElement BuildUpdatePanel(bool evaluation)
    {
        updateStore = new(Path.Combine(dataRoot, "updates")); updater = new(updateStore);
        canInstallUpdate = !testMode && !evaluation && Installer.IsInstalledExecutable;
        var panel = new StackPanel(); panel.Children.Add(Ui.Text("Atualizações", 18));
        panel.Children.Add(Ui.Text("Versão instalada: " + Program.Version, 12, Ui.Muted));
        automaticUpdates.IsChecked = updateStore.Preferences.Automatic;
        automaticUpdates.Checked += (_, _) => SaveUpdatePreference(true);
        automaticUpdates.Unchecked += (_, _) => SaveUpdatePreference(false);
        panel.Children.Add(automaticUpdates);
        panel.Children.Add(Ui.Text("Consulta ao abrir e a cada seis horas. Downloads e instalação somente após sua confirmação.", 12, Ui.Muted));
        updateCheckButton = Ui.Button("Verificar agora", async (_, _) => await CheckForUpdates(true));
        updateOfferButton = Ui.Primary(Ui.Button("Ver atualização disponível", (_, _) => ShowUpdateOffer())); updateOfferButton.Visibility = Visibility.Collapsed;
        updateCancelButton = Ui.Button("Cancelar download", (_, _) => updateDownload?.Cancel()); updateCancelButton.Visibility = Visibility.Collapsed;
        panel.Children.Add(Ui.Row(updateCheckButton, updateOfferButton, updateCancelButton));
        updateStatus.Tag = updateLastCheck.Tag = "UserContent"; panel.Children.Add(updateStatus); panel.Children.Add(updateLastCheck);
        if (!canInstallUpdate) panel.Children.Add(Ui.Text("Avaliações e versões portáteis usam instalação manual pela página de releases.", 11, Ui.Muted));
        Strings.Changed += UpdateLanguage;
        UpdateLanguage(); UpdateUpdateButtons(); return panel;
    }
    private void SaveUpdatePreference(bool value)
    {
        try { updateStore.Modify(p => p with { Automatic = value }); if (value && updateChecksStarted) _ = CheckForUpdates(false); }
        catch (Exception ex) { SetUpdateStatus("Não foi possível salvar a preferência de atualização."); log.Write("ATUALIZAÇÃO", ex.Message); }
    }
    internal void StartUpdateChecks()
    {
        if (updateChecksStarted || testMode || !canInstallUpdate) return;
        updateChecksStarted = true; _ = AutoUpdateLoop();
    }
    private async Task AutoUpdateLoop()
    {
        try
        {
            if (updateStore.Preferences.Automatic) await CheckForUpdates(false);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            while (await timer.WaitForNextTickAsync(updateStop.Token)) if (updateStore.Preferences.Automatic) await CheckForUpdates(false);
        }
        catch (OperationCanceledException) { }
    }
    private async Task CheckForUpdates(bool manual)
    {
        if (closed || updateBusy) return;
        updateBusy = true; UpdateUpdateButtons(); SetUpdateStatus("Verificando atualizações…");
        try
        {
            var check = await updater.CheckAsync(Program.Version, updateStop.Token);
            availableUpdate = check.Release;
            if (check.Phase == UpdatePhase.Available && availableUpdate != null)
            {
                SetUpdateStatus("Nova versão disponível: " + availableUpdate.Version);
                if (!manual && dismissedUpdate != availableUpdate.Version && notifiedUpdate != availableUpdate.Version && !IsVisible)
                {
                    notifiedUpdate = availableUpdate.Version;
                    tray.ShowBalloonTip(5000, "Vault Sync", Strings.T("Nova versão disponível: " + availableUpdate.Version), System.Windows.Forms.ToolTipIcon.Info);
                }
                if (manual) ShowUpdateOffer();
            }
            else SetUpdateStatus("Você está usando a versão mais recente.");
        }
        catch (OperationCanceledException) { if (!closed) SetUpdateStatus("Verificação cancelada ou sem resposta da rede."); }
        catch (Exception ex)
        {
            if (!closed) { SetUpdateStatus("Não foi possível verificar atualizações. A sincronização continua funcionando."); log.Write("ATUALIZAÇÃO", ex.Message); }
        }
        finally { if (updateDownload == null) updateBusy = false; UpdateUpdateButtons(); UpdateLanguage(); }
    }
    private void ShowUpdateOffer()
    {
        if (availableUpdate == null || updateInstalling || closed) return;
        var dialog = new UpdateWindow(availableUpdate, canInstallUpdate) { Owner = IsVisible ? this : null };
        if (dialog.ShowDialog() == true)
        {
            if (canInstallUpdate) _ = DownloadAndInstall(availableUpdate);
            else OpenUpdatePage();
        }
        else { dismissedUpdate = availableUpdate.Version; UpdateUpdateButtons(); }
    }
    private void OpenUpdatePage()
    { Process.Start(new ProcessStartInfo(GitHubUpdates.ReleasePage) { UseShellExecute = true }); }
    private async Task DownloadAndInstall(UpdateRelease release)
    {
        // CheckForUpdates may still be unwinding its modal offer; this method owns the entire download.
        if (updateDownload != null || !canInstallUpdate) return;
        updateBusy = true; updateDownload = CancellationTokenSource.CreateLinkedTokenSource(updateStop.Token); UpdateUpdateButtons();
        ResumeState previous = ResumeState.Stopped; SyncConfig? config = activeConfig; bool quiesced = false, helperLaunched = false; string? pendingDirectory = null;
        try
        {
            var progress = new Progress<DownloadProgress>(p => { if (!closed && !updateInstalling) SetUpdateStatus(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Baixando atualização: {0} de {1} MB", p.Bytes / 1048576, p.Total / 1048576)); });
            var verified = await updater.DownloadAsync(release, progress, updateDownload.Token);
            pendingDirectory = Path.GetDirectoryName(verified.Path)!;
            updateInstalling = true; UpdateButtons(); UpdateUpdateButtons(); SetUpdateStatus("Preparando instalação; aguardando operações em andamento…");
            while (busy) await Task.Delay(100, updateDownload.Token);
            config = activeConfig;
            previous = worker == null ? ResumeState.Stopped : worker.Paused ? ResumeState.Paused : ResumeState.Running;
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Installer.StartupPath);
            bool startWithWindows = run?.GetValue("VaultSync") != null;
            bool visible = IsVisible;
            string file = await Task.Run(() => UpdateRuntime.Prepare(verified, previous, visible, startWithWindows));
            updateDownload.Token.ThrowIfCancellationRequested();
            var current = worker; worker = null;
            if (current != null) await Task.Run(current.Dispose);
            prepared?.Dispose(); prepared = null; plan = null; quiesced = true;
            if (closed || updateStop.IsCancellationRequested) return;
            helperLaunched = true; await UpdateRuntime.LaunchAsync(file, updateDownload.Token);
            await Exit();
        }
        catch (OperationCanceledException) { if (!closed) SetUpdateStatus("Atualização cancelada. A versão instalada foi preservada."); }
        catch (Exception ex) { if (!closed) { SetUpdateStatus("A atualização não foi instalada. A versão atual foi preservada."); log.Write("ATUALIZAÇÃO", ex.Message); ThemedDialog.Info(this, Strings.T("A atualização não foi instalada. A versão atual foi preservada.")); } }
        finally
        {
            updateInstalling = false; updateBusy = false; updateDownload.Dispose(); updateDownload = null;
            if (!helperLaunched && pendingDirectory != null) { try { UpdateRuntime.AbandonUnstarted(pendingDirectory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Write("ATUALIZAÇÃO", ex.Message); } }
            if (quiesced && !closed && previous != ResumeState.Stopped && config != null)
            { resumeState = previous; BeginWorker(new SyncEngine(config, dataRoot), config); resumeState = null; }
            if (!closed) { UpdateButtons(); UpdateUpdateButtons(); }
        }
    }
    private void SetUpdateStatus(string source) { updateStatusSource = source; UpdateLanguage(); }
    private void UpdateLanguage()
    {
        updateStatus.Text = Strings.T(updateStatusSource);
        updateLastCheck.Text = updateStore.Preferences.LastCheckedUtc is { } time ? Strings.T("Última verificação: " + time.LocalDateTime.ToString("g")) : "";
        updateBannerText.Text = availableUpdate == null ? "" : Strings.T("Nova versão disponível: " + availableUpdate.Version);
    }
    private void UpdateUpdateButtons()
    {
        updateCheckButton.IsEnabled = !updateBusy; automaticUpdates.IsEnabled = canInstallUpdate && !updateBusy;
        updateOfferButton.Visibility = availableUpdate == null ? Visibility.Collapsed : Visibility.Visible;
        updateOfferButton.IsEnabled = !updateBusy;
        updateCancelButton.Visibility = updateDownload != null && !updateInstalling ? Visibility.Visible : Visibility.Collapsed;
        updateBanner.Visibility = availableUpdate != null && dismissedUpdate != availableUpdate.Version ? Visibility.Visible : Visibility.Collapsed;
        updateBannerButton.IsEnabled = !updateBusy;
    }
    private void StopUpdateChecks()
    { updateStop.Cancel(); updateDownload?.Cancel(); updater.Dispose(); Strings.Changed -= UpdateLanguage; }
    internal void ApplyUpdateResume(ResumeState state, UpdateResult? result)
    { resumeState = state; if (result != null) { SetUpdateStatus(result.Success ? "Atualização instalada e verificada." : "A atualização não foi instalada. A versão atual foi preservada."); log.Write("ATUALIZAÇÃO", result.Message); } }
    internal Task ExitSmokeAsync() => Exit();
    internal void UpdateAvailableSmoke(UpdateRelease release) { availableUpdate = release; SetUpdateStatus("Nova versão disponível: " + release.Version); UpdateUpdateButtons(); }
}
