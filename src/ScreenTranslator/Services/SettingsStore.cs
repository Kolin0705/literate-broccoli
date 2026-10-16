using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenTranslator.Services;

public sealed class AppSettings
{
    public string AppId { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public Rectangle? CaptureRegion { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public int AutoIntervalSeconds { get; set; } = 3;
    public int TranslationFontSize { get; set; } = 16;
}

public static class SettingsStore
{
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTranslator", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            if (stored is null || stored.Version != 1)
            {
                warning = "配置文件版本无法识别，已使用默认设置。请重新配置翻译接口。";
                return new AppSettings();
            }

            var settings = new AppSettings
            {
                AppId = stored.AppId ?? "",
                AlwaysOnTop = stored.AlwaysOnTop,
                AutoIntervalSeconds = Math.Clamp(stored.AutoIntervalSeconds, 1, 120),
                TranslationFontSize = Math.Clamp(stored.TranslationFontSize, 10, 48),
                CaptureRegion = stored.CaptureRegion is { Width: > 0, Height: > 0 } region
                    ? new Rectangle(region.X, region.Y, region.Width, region.Height)
                    : null
            };

            if (!string.IsNullOrEmpty(stored.ProtectedSecretKey))
            {
                try
                {
                    settings.SecretKey = Unprotect(stored.ProtectedSecretKey);
                }
                catch (Exception exception) when (exception is Win32Exception or CryptographicException or FormatException or ArgumentException)
                {
                    warning = "无法解密保存的密钥。密钥仅适用于原 Windows 用户和电脑，请在设置中重新填写。";
                }
            }

            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            warning = "无法读取配置文件，已使用默认设置。请检查本地应用数据目录权限，并在设置中重新填写接口信息。";
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = new StoredSettings
        {
            Version = 1,
            AppId = settings.AppId.Trim(),
            ProtectedSecretKey = string.IsNullOrEmpty(settings.SecretKey) ? "" : Protect(settings.SecretKey),
            CaptureRegion = settings.CaptureRegion is { Width: > 0, Height: > 0 } region
                ? new StoredRegion { X = region.X, Y = region.Y, Width = region.Width, Height = region.Height }
                : null,
            AlwaysOnTop = settings.AlwaysOnTop,
            AutoIntervalSeconds = Math.Clamp(settings.AutoIntervalSeconds, 1, 120),
            TranslationFontSize = Math.Clamp(settings.TranslationFontSize, 10, 48)
        };

        string directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            // Write and flush in the destination directory before an atomic same-volume replacement.
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, stored, JsonOptions);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Protect(string secret)
    {
        byte[] plain = Encoding.UTF8.GetBytes(secret);
        try { return Convert.ToBase64String(Transform(plain, protect: true)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static string Unprotect(string protectedSecret)
    {
        byte[] plain = Transform(Convert.FromBase64String(protectedSecret), protect: false);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBlob = new DataBlob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var outputBlob = new DataBlob();
        try
        {
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);
            const uint forbidUserInterface = 0x1;
            bool success = protect
                ? CryptProtectData(ref inputBlob, "ScreenTranslator translation API key", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, forbidUserInterface, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, forbidUserInterface, out outputBlob);
            if (!success)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 无法加密或解密本地密钥。");

            var output = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            if (inputBlob.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[inputBlob.Length], 0, inputBlob.Data, inputBlob.Length);
                Marshal.FreeHGlobal(inputBlob.Data);
            }
            if (outputBlob.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[outputBlob.Length], 0, outputBlob.Data, outputBlob.Length);
                LocalFree(outputBlob.Data);
            }
        }
    }

    private sealed class StoredSettings
    {
        public StoredSettings() { }

        public int Version { get; set; } = 1;
        public string? AppId { get; set; } = "";
        public string? ProtectedSecretKey { get; set; } = "";
        public StoredRegion? CaptureRegion { get; set; }
        public bool AlwaysOnTop { get; set; } = true;
        public int AutoIntervalSeconds { get; set; } = 3;
        public int TranslationFontSize { get; set; } = 16;
    }

    private sealed class StoredRegion
    {
        public StoredRegion() { }

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
