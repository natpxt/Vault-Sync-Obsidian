using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;
using Microsoft.Win32;
using VaultSync.Core;

namespace VaultSync.App;

internal enum ThemeMode { System, Light, Dark }

// Mutable, shared brushes also update FlowDocuments and windows already open.
internal static class Theme
{
    private static readonly Dictionary<string, SolidColorBrush> brushes = new();
    private static readonly Palette palette = new();
    private static string settingsRoot = AppFiles.DataRoot;
    private static bool listening;
    internal static Func<bool> SystemDark = ReadSystemDark;
    internal static ThemeMode Mode { get; private set; } = ThemeMode.System;
    internal static bool IsDark { get; private set; }
    internal static event Action? Changed;
    static Theme() => Apply();
    internal static SolidColorBrush Get(string name) => brushes[name];
    internal static void Initialize(string? root = null)
    {
        settingsRoot = root ?? AppFiles.DataRoot;
        Mode = ThemeMode.System;
        string language = "pt-BR";
        try
        {
            var file = Path.Combine(settingsRoot, "ui-settings.json");
            if (File.Exists(file))
            {
                var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file));
                if (Enum.TryParse<ThemeMode>(preferences?.Theme, out var mode) && Enum.IsDefined(mode)) Mode = mode;
                language = preferences?.Language ?? "pt-BR";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* A damaged UI preference must not prevent startup. */ }
        Strings.Set(language, false); Apply();
        if (!listening) { SystemEvents.UserPreferenceChanged += PreferenceChanged; listening = true; }
    }
    internal static void Set(ThemeMode mode, bool persist = true)
    {
        if (persist) WritePreferences(mode, Strings.Code);
        Mode = mode; Apply();
    }
    internal static void SavePreferences(string language) => WritePreferences(Mode, language);
    private static void WritePreferences(ThemeMode mode, string language)
    {
        Directory.CreateDirectory(settingsRoot);
        string file = Path.Combine(settingsRoot, "ui-settings.json"), temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Preferences(mode.ToString(), language))); File.Move(temp, file, true);
    }
    internal static void RefreshSystem() { if (Mode == ThemeMode.System) Apply(); }
    private static void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(RefreshSystem);
    }
    internal static void Shutdown() { if (listening) SystemEvents.UserPreferenceChanged -= PreferenceChanged; listening = false; }
    private static bool ReadSystemDark()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        catch (System.Security.SecurityException) { return true; }
    }
    private static void Apply()
    {
        IsDark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && SystemDark());
        string[] keys = ["Background", "Surface", "Raised", "Border", "Ink", "Muted", "Accent", "Primary", "OnPrimary", "Selection", "Success", "Warning", "Danger", "Diff", "Code"];
        string[] dark = ["#1C1C1C", "#232323", "#282828", "#3F3F3F", "#DADADA", "#B3B3B3", "#B49AFF", "#7852C7", "#FFFFFF", "#30283E", "#9BD5B2", "#EAC281", "#F0A3A3", "#493B26", "#171717"];
        string[] light = ["#FFFFFF", "#F6F6F6", "#EFEFEF", "#DADADA", "#222222", "#5C5C5C", "#6940BC", "#6940BC", "#FFFFFF", "#EEE7F9", "#246440", "#805515", "#A33232", "#FFF0CC", "#EFEFEF"];
        for (int i = 0; i < keys.Length; i++)
        {
            var color = (Color)ColorConverter.ConvertFromString((IsDark ? dark : light)[i]);
            palette.Colors[keys[i]] = color;
            if (!brushes.ContainsKey(keys[i]))
            {
                var brush = new SolidColorBrush(color);
                // Data-bound Freezables cannot be frozen by WPF style/template optimization.
                BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding($"[{keys[i]}]") { Source = palette, Mode = BindingMode.OneWay });
                brushes[keys[i]] = brush;
            }
        }
        palette.Notify();
        Changed?.Invoke();
    }
    internal static void AddResources(ResourceDictionary resources) { foreach (var pair in brushes) resources[pair.Key] = pair.Value; }
    private sealed record Preferences(string Theme, string Language = "pt-BR");
    private sealed class Palette : INotifyPropertyChanged
    {
        internal readonly Dictionary<string, Color> Colors = new();
        public Color this[string name] => Colors[name];
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
