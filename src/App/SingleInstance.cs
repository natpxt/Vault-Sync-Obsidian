using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VaultSync.App;

// A second launch requests the existing window instead of depending on the tray.
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string eventName;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? registration;
    internal bool IsOwner { get; }
    internal string Name { get; }
    internal SingleInstance(string name)
    {
        Name = name; mutex = new Mutex(false, name); eventName = name + "-Activate";
        try { IsOwner = mutex.WaitOne(0); } catch (AbandonedMutexException) { IsOwner = true; }
    }
    internal void Listen(Action reveal)
    {
        if (!IsOwner) throw new InvalidOperationException("Only the owning instance may listen.");
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        registration = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => reveal(), null, Timeout.Infinite, false);
    }
    internal bool RequestActivation(bool evaluation, bool allowLegacy = true)
    {
        // Covers the short interval between acquiring the mutex and creating the UI.
        for (int i = 0; i < 20; i++)
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out var existing))
            {
                if (allowLegacy) foreach (int pid in CandidateProcesses(evaluation)) AllowSetForegroundWindow(pid);
                using (existing) existing.Set(); return true;
            }
            if (allowLegacy && RestoreLegacy(evaluation)) return true;
            Thread.Sleep(100);
        }
        return false;
    }
    internal static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private static HashSet<int> CandidateProcesses(bool evaluation)
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || !process.ProcessName.StartsWith("VaultSync", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = process.MainModule?.FileName ?? "";
                    if (Path.GetFileNameWithoutExtension(path).EndsWith("-Avaliacao", StringComparison.OrdinalIgnoreCase) == evaluation) ids.Add(process.Id);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return ids;
    }
    internal static bool RestoreLegacy(bool evaluation, bool close = false)
    {
        var ids = CandidateProcesses(evaluation); bool found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out int pid); if (!ids.Contains(pid)) return true;
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (title.ToString() != Program.Product || !name.ToString().StartsWith("HwndWrapper", StringComparison.Ordinal)) return true;
            if (close) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            else if (IsWindowVisible(window)) RestoreWindow(window);
            else return true;
            found = true; return false;
        }, IntPtr.Zero);
        return found;
    }
    internal static void RestoreWindow(IntPtr window)
    {
        ShowWindowAsync(window, IsIconic(window) ? 9 : 5); SetForegroundWindow(window);
    }
    public void Dispose()
    {
        registration?.Unregister(null); activation?.Dispose();
        if (IsOwner) mutex.ReleaseMutex(); mutex.Dispose();
    }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
