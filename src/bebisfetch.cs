using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using static System.String;

const string reset   = "\e[0m";
const string red     = "\e[31m";
const string green   = "\e[32m";
const string yellow  = "\e[33m";
const string blue    = "\e[34m";
const string magenta = "\e[35m";
const string cyan    = "\e[36m";
const string gray    = "\e[90m";
const string white   = "\e[97m";

List<string> driveLines  = [];
var          driveThread = new Thread(() => driveLines = GetDriveLines());
driveThread.Start();

var logo = new[]
{
    "      ▒███████     ▒███████      ",
    "   ▒██████████████████████████   ",
    "   ▒████░ ████  ████░ ███  ███   ",
    "   ▒████░       ████░      ███   ",
    "   ▒████░       ████░      ███   ",
    "   ▒████░       ████░      ███   ",
    "    ▒█████░    ██████░    ███▓▓▓▓",
    "▒▓▓▓▓▓▓██████████▓▓████████▓▓▓▓▓ ",
    "  ▒▓▓▓▓▓▓▓▓▓▓▓▓▓ ██ ▓▓▓▓▓▓▓▓▓▓▓  ",
    "    ▒▓▓▓▓▓▓▓▓▓▓      ▓▓▓▓▓▓▓▓▓▓  ",
    "    ▒▓▓▓▓▓▓▓▓▓▓      ▓▓▓▓▓▓▓▓▓▓  ",
    "      ▒▓▓▓▓▓▓▓▓▓    ▓▓▓▓▓▓▓▓▓    ",
    "         ▒▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓      ",
    "           ▒▓▓▓       ▒▓▓▓       "
};

var username  = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
var hostname  = Environment.MachineName;
var tickCount = Environment.TickCount64;
var uptime    = TimeSpan.FromMilliseconds(tickCount);
var shell     = Environment.GetEnvironmentVariable("SHELL");
var linux     = OperatingSystem.IsLinux();

if (!IsNullOrEmpty(shell))
{
    var span      = shell.AsSpan();
    var lastSlash = span.LastIndexOf('/');

    if (lastSlash != -1)
        span = span[(lastSlash + 1)..];

    shell = span.ToString();
}

var info = new List<string> { $" {gray}: {blue}{username}{gray}@{cyan}{hostname}" };

if (linux)
{
    var kernelSuffix = GetLinuxKernel(out var kernel) ? $"@{yellow}{kernel}" : Empty;
    var line         = $" {gray}: {blue}Linux{gray}{kernelSuffix}";
    info.Add(line);
}

info.Add($"󰥔 {gray}: {reset}{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");

if (shell != null)
    info.Add($" {gray}: {green}{shell}");

if (linux)
{
    if (GetLinuxRamUsage(out var ramUsed, out var ramTotal))
        info.Add($"󰿯 {gray}: {GetBar(ramUsed, ramTotal)}");

    var pacmanPackages = 0;
    var dnfPackages    = 0;

    if (File.Exists("/etc/os-release"))
    {
        var content = ReadSmall("/etc/os-release", 1024).AsSpan();

        foreach (var line in content.EnumerateLines())

            if (line.StartsWith("ID=") || line.StartsWith("ID_LIKE="))
            {
                var val = line[(line.IndexOf('=') + 1)..].Trim('"');

                if (val.IndexOf("arch", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    pacmanPackages = CountDirs("/var/lib/pacman/local");
                    break;
                }

                if (val.IndexOf("fedora", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    if (GetProcessOutput("rpm", "-qa --qf '%{NAME}\\n'", out var output))
                        dnfPackages = CountLines(output);

                    break;
                }
            }
    }

    var flatpakPackages = CountDirs("/var/lib/flatpak/app") + CountDirs("/var/lib/flatpak/runtime");

    if (pacmanPackages > 0 || dnfPackages > 0 || flatpakPackages > 0)
    {
        var parts = new List<string>(3);

        if (pacmanPackages > 0)
            parts.Add($"{reset}{pacmanPackages}{cyan} pacman");
        if (dnfPackages > 0)
            parts.Add($"{reset}{dnfPackages}{cyan} dnf");
        if (flatpakPackages > 0)
            parts.Add($"{reset}{flatpakPackages}{magenta} flatpak");

        var packages = Join($"{gray}, ", parts);

        info.Add($"󰏖 {gray}: {packages}");
    }
}

driveThread.Join();
info.AddRange(driveLines);

var sb = new StringBuilder(256);

for (var i = 0; i < logo.Length; i++)
{
    foreach (var c in logo[i])
        switch (c)
        {
            case '█':
                sb.Append(white).Append(c);
                break;
            case '▓':
                sb.Append(yellow).Append(c);
                break;
            case '▒':
                sb.Append(gray).Append(c);
                break;
            case '░':
                sb.Append(white).Append(c);
                break;
            default:
                sb.Append(c);
                break;
        }

    if (info.Count > i)
        sb.Append("   ").Append(yellow).Append(info[i]);

    sb.Append('\n');
}

sb.Append(reset);

var       bytes  = Encoding.UTF8.GetBytes(sb.ToString());
using var stdout = new FileStream(new SafeFileHandle(1, false), FileAccess.Write, 0, false);
stdout.Write(bytes);

return;

static bool GetLinuxKernel(out string kernel)
{
    kernel = "unknown";

    try
    {
        var content    = ReadSmall("/proc/version", 256).AsSpan();
        var versionIdx = content.IndexOf("version ");

        if (versionIdx != -1)
        {
            var remainder = content[(versionIdx + 8)..];
            var spaceIdx  = remainder.IndexOf(' ');

            if (spaceIdx != -1)
            {
                kernel = remainder[..spaceIdx].ToString();
                return true;
            }
        }
    }
    catch
    {
        return false;
    }

    return false;
}

static string GetBar(ulong used, ulong total)
{
    if (total == 0)
        total = 1;

    var fill = (int)((double)used / total * 10);

    if (fill > 10)
        fill = 10;
    if (fill < 0)
        fill = 0;

    var col = fill switch
    {
        > 8 => red,
        > 6 => yellow,
        _   => green
    };

    var filledPart = fill > 0 ? $"{col}{new string('■',   fill)}{reset}" : Empty;
    var emptyPart  = fill < 10 ? $"{gray}{new string('■', 10 - fill)}{reset}" : Empty;

    return $"{filledPart}{emptyPart} {FormatGiB(used)}{gray} / {reset}{FormatGiB(total)}{gray} GiB{reset}";

    static string FormatGiB(ulong bytes)
    {
        var gib = bytes / 1073741824.0;
        return $"{white}{gib:F1}{reset}";
    }
}

static ulong ParseKbValue(ReadOnlySpan<char> line)
{
    var colon = line.IndexOf(':');
    if (colon == -1)
        return 0;

    var slice = line[(colon + 1)..].Trim();
    var space = slice.IndexOf(' ');
    if (space != -1)
        slice = slice[..space];

    ulong.TryParse(slice, out var val);

    return val;
}

static bool GetLinuxRamUsage(out ulong used, out ulong total)
{
    used  = 0;
    total = 0;

    try
    {
        ulong memTotal     = 0;
        ulong memAvailable = 0;

        foreach (var line in ReadSmall("/proc/meminfo", 256).AsSpan().EnumerateLines())
            if (line.StartsWith("MemTotal:"))
            {
                memTotal = ParseKbValue(line);
            }
            else if (line.StartsWith("MemAvailable:"))
            {
                memAvailable = ParseKbValue(line);
                break;
            }

        total = memTotal * 1024;
        var freeBytes = memAvailable * 1024;
        used = total > freeBytes ? total - freeBytes : 0;
        return true;
    }
    catch
    {
        return false;
    }
}

static bool GetProcessOutput(string file, string args, out string output)
{
    output = "";

    try
    {
        using var process = new Process();
        process.StartInfo.FileName               = file;
        process.StartInfo.Arguments              = args;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute        = false;
        process.StartInfo.CreateNoWindow         = true;
        process.Start();

        output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return true;
    }
    catch
    {
        return false;
    }
}

static int CountDirs(string path)
{
    if (!Directory.Exists(path))
        return 0;

    var entries = new FileSystemEnumerable<bool>(path, static (ref _) => true)
    {
        ShouldIncludePredicate = static (ref e) => e.IsDirectory
    };

    var n = 0;
    foreach (var _ in entries)
        n++;
    return n;
}

static int CountLines(string s)
{
    var count = 0;
    foreach (var _ in s.AsSpan().EnumerateLines())
        count++;
    return count;
}

static List<string> GetDriveLines()
{
    var drives        = new List<(string Name, ulong Used, ulong Total)>();
    var maxNameLength = 0;

    foreach (var drive in DriveInfo.GetDrives())
    {
        var name = drive.Name;

        if (name.StartsWith("/sys") || name.StartsWith("/proc") || name.StartsWith("/dev") || name.StartsWith("/run"))
            continue;

        if (drive is not { IsReady: true, DriveType: DriveType.Fixed })
            continue;

        var format = drive.DriveFormat;

        if (format is "tmpfs" or "sysfs" or "proc" or "efivarfs")
            continue;

        var total = (ulong)drive.TotalSize;

        if (total == 0)
            continue;

        var free = (ulong)drive.AvailableFreeSpace;
        var used = total > free ? total - free : 0;

        if (name.Length > maxNameLength)
            maxNameLength = name.Length;

        drives.Add((name, used, total));
    }

    var lines = new List<string>(drives.Count);

    foreach (var d in drives)
        lines.Add($"󰋊 {gray}{d.Name.PadRight(maxNameLength)} : {GetBar(d.Used, d.Total)}");

    return lines;
}

static string ReadSmall(string path, int max = 512)
{
    Span<byte> buf = stackalloc byte[max];
    using var  h   = File.OpenHandle(path);
    var        n   = RandomAccess.Read(h, buf, 0);
    return Encoding.UTF8.GetString(buf[..n]);
}