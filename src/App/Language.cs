using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Automation;

namespace VaultSync.App;

// Presentation-only translation: filenames, note contents and persisted engine logs stay intact.
internal static class Strings
{
    private sealed record Entry(string Pt, string En, string Es);
    private sealed record Value(string Source, string Rendered);
    private static readonly Dictionary<string, Entry> entries;
    private static readonly List<(Regex Pattern, Entry Entry)> patterns = [];
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Value>> originals = new();
    internal static readonly string[] Codes = ["pt-BR", "en", "es"];
    internal static string Code { get; private set; } = "pt-BR";
    internal static event Action? Changed;
    static Strings()
    {
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream("VaultSync.App.Resources.Translations.json") ?? throw new IOException("Translation catalog not found.");
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!.Select(e => new Entry(e.Key, e.Value[0], e.Value[1])).ToList();
        entries = catalog.ToDictionary(e => e.Pt, StringComparer.Ordinal);
        foreach (var entry in catalog.Where(e => e.Pt.Contains("{0}", StringComparison.Ordinal)))
        {
            string pattern = Regex.Escape(entry.Pt);
            for (int i = 0; i < 10; i++) pattern = pattern.Replace(Regex.Escape("{" + i + "}"), "(.+?)", StringComparison.Ordinal);
            patterns.Add((new Regex("^" + pattern + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), entry));
        }
    }
    internal static string T(string source)
    {
        if (Code == "pt-BR" || string.IsNullOrEmpty(source)) return source;
        if (entries.TryGetValue(source, out var entry)) return Code == "en" ? entry.En : entry.Es;
        foreach (var (pattern, template) in patterns)
        {
            var match = pattern.Match(source); if (!match.Success) continue;
            bool nestedMessage = template.Pt.StartsWith("Monitor ativo,", StringComparison.Ordinal) || template.Pt.StartsWith("Nova tentativa", StringComparison.Ordinal) || template.Pt.StartsWith("Backup adiou", StringComparison.Ordinal) || template.Pt.StartsWith("Arquivo/subpasta adiado", StringComparison.Ordinal) || template.Pt.StartsWith("Igualdade será", StringComparison.Ordinal) || template.Pt.StartsWith("Conferência do conflito adiada", StringComparison.Ordinal) || template.Pt.StartsWith("Uma das pastas está indisponível.", StringComparison.Ordinal);
            object[] args = match.Groups.Cast<Group>().Skip(1).Select(g => (object)(nestedMessage || g.Value == "em andamento" ? T(g.Value) : g.Value)).ToArray();
            return string.Format(CultureInfo.InvariantCulture, Code == "en" ? template.En : template.Es, args);
        }
        // Compound status labels and messages with paths keep their payload verbatim.
        if (source.Contains('\n')) return string.Join("\n", source.Split('\n').Select(T));
        foreach (string separator in new[] { " · ", "\n", " | " })
            if (source.Contains(separator, StringComparison.Ordinal)) return string.Join(separator, source.Split(separator).Select(T));
        return source;
    }
    internal static void Set(string code, bool persist = true)
    {
        if (!Codes.Contains(code, StringComparer.Ordinal)) code = "pt-BR";
        if (persist) Theme.SavePreferences(code);
        Code = code; Changed?.Invoke();
    }
    internal static bool Has(string source) => entries.ContainsKey(source);
    internal static void Apply(Window window)
    {
        var seen = new HashSet<DependencyObject>(); Visit(window);
        void Visit(DependencyObject item)
        {
            if (!seen.Add(item)) return;
            if (item is FrameworkElement { Tag: "UserContent" }) return;
            if (item is TextBlock block && block.Tag is not "UserContent") Translate(item, TextBlock.TextProperty);
            if (item is Window) Translate(item, Window.TitleProperty);
            if (item is ContentControl) Translate(item, ContentControl.ContentProperty);
            if (item is HeaderedContentControl) Translate(item, HeaderedContentControl.HeaderProperty);
            if (item is FrameworkElement) Translate(item, FrameworkElement.ToolTipProperty);
            Translate(item, AutomationProperties.NameProperty);
            if (item is DataGrid data)
            {
                foreach (var column in data.Columns) Translate(column, DataGridColumn.HeaderProperty);
                return; // Never translate a filename or bound cell as if it were a UI label.
            }
            if (item is TextBox or FlowDocument or ListBoxItem) return;
            foreach (object child in LogicalTreeHelper.GetChildren(item)) if (child is DependencyObject dependency) Visit(dependency);
            if (item is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) Visit(VisualTreeHelper.GetChild(item, i));
        }
    }
    private static void Translate(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string current || current.Length == 0) return;
        var values = originals.GetOrCreateValue(target);
        string source = values.TryGetValue(property, out var old) && current == old.Rendered ? old.Source : current;
        string rendered = T(source); values[property] = new(source, rendered);
        if (current != rendered) target.SetCurrentValue(property, rendered);
    }
}
