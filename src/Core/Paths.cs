using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VaultSync.Core;

public static class Paths
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool Within(string path, string root) => Normalize(path).Equals(Normalize(root), StringComparison.OrdinalIgnoreCase)
        || Normalize(path).StartsWith(Normalize(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void ValidatePair(SyncConfig config, string stateRoot)
    {
        var local = Normalize(config.LocalRoot); var cloud = Normalize(config.CloudRoot);
        if (Within(local, cloud) || Within(cloud, local))
            throw new IOException("As pastas devem ser diferentes e não podem estar uma dentro da outra.");
        if (Within(stateRoot, local) || Within(stateRoot, cloud) || Within(local, stateRoot) || Within(cloud, stateRoot))
            throw new IOException("As pastas de dados não podem se sobrepor ao estado do aplicativo.");
        foreach (var root in new[] { local, cloud })
        {
            if (root.StartsWith("\\\\", StringComparison.Ordinal) || root.Length <= Path.GetPathRoot(root)!.Length)
                throw new IOException("Selecione uma pasta em disco local, não uma unidade inteira ou endereço de rede.");
            CheckAncestors(root);
        }
    }
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')
            || relative.Split('/', '\\').Any(p => p is ".." or "." or ""))
            throw new IOException("Caminho relativo inválido.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!Within(full, root)) throw new IOException("Caminho fora da raiz.");
        CheckAncestors(full);
        return full;
    }
    public static void CheckAncestors(string path)
    {
        for (var p = Normalize(path); p != null; p = Directory.GetParent(p)?.FullName)
        {
            var attr = TryAttributes(p);
            if (attr != null && attr.Value.HasFlag(FileAttributes.ReparsePoint) && !IsCloudPoint(p))
                throw new IOException($"Link/junção não percorrido: {p}");
        }
    }
    public static FileAttributes? TryAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public static bool NeedsDownload(FileAttributes attributes) => ((int)attributes & (0x1000 | 0x40000 | 0x400000)) != 0;
    public static bool IsCloudPoint(string path)
    {
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        return !handle.IsInvalid && GetFileInformationByHandleEx(handle, 9, out var info, 8)
            && (info.Tag & 0xFFFF0FFFu) == 0x9000001Au;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TagInfo { public uint Attributes; public uint Tag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out TagInfo info, uint size);
}
