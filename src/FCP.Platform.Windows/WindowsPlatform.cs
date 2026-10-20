using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FCP.Core.Platform;
using Microsoft.Win32;
using RegistryHive = FCP.Core.Platform.RegistryHive;

namespace FCP.Platform.Windows;

public sealed class WindowsRegistryReader : IRegistryReader
{
    public string? ReadString(RegistryHive hive, string subKey, string valueName, bool use32BitView = false)
    {
        try
        {
            using var key = Open(hive, subKey, use32BitView);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey, bool use32BitView = false)
    {
        try
        {
            using var key = Open(hive, subKey, use32BitView);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static RegistryKey? Open(RegistryHive hive, string subKey, bool use32BitView)
    {
        var view = use32BitView ? RegistryView.Registry32 : RegistryView.Registry64;
        var baseKey = RegistryKey.OpenBaseKey(hive == RegistryHive.LocalMachine ? Microsoft.Win32.RegistryHive.LocalMachine : Microsoft.Win32.RegistryHive.CurrentUser, view);
        return baseKey.OpenSubKey(subKey, writable: false);
    }
}

public sealed class WindowsShell : IShell
{
    public string? ResolveShortcut(string lnkPath)
    {
        try
        {
            dynamic? shell = CreateWshShell();
            if (shell is null)
                return null;
            var shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
    }

    public void CreateShortcut(string lnkPath, string targetPath, string arguments, string? workingDirectory, string? description)
    {
        dynamic shell = CreateWshShell() ?? throw new InvalidOperationException("无法创建快捷方式：系统组件不可用");
        var shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(targetPath) ?? "";
        shortcut.Description = description ?? "";
        shortcut.IconLocation = targetPath + ",0";
        shortcut.Save();
    }

    private static object? CreateWshShell() =>
        Type.GetTypeFromProgID("WScript.Shell") is { } type ? Activator.CreateInstance(type) : null;
}

public sealed partial class WindowsWindowLocator : IWindowLocator
{
    public bool HasVisibleWindow(int processId)
    {
        var found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == processId && IsWindowVisible(hwnd) && GetWindowTextLength(hwnd) > 0)
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr hwnd);
}

public sealed partial class WindowsSystemInfo : ISystemInfo
{
    public long TotalPhysicalMemoryMb => Status().TotalPhys / 1024 / 1024;

    public long AvailablePhysicalMemoryMb => Status().AvailPhys / 1024 / 1024;

    private static MemoryStatusEx Status()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref status);
        return status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public long TotalPhys;
        public long AvailPhys;
        public long TotalPageFile;
        public long AvailPageFile;
        public long TotalVirtual;
        public long AvailVirtual;
        public long AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

/// <summary>DPAPI（当前用户范围）：只有同一台电脑上的同一个 Windows 用户才能解密。</summary>
public sealed class DpapiProtectedData : IProtectedData
{
    private static readonly byte[] Entropy = "FCP.Credentials.v1"u8.ToArray();

    public string Protect(string plainText) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedText)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
