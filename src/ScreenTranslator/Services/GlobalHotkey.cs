using System.Runtime.InteropServices;

namespace ScreenTranslator.Services;

public sealed class GlobalHotkey : IDisposable
{
    public const int MessageId = 0x0312;
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Windows = 0x0008;
    private const uint NoRepeat = 0x4000;

    private readonly IntPtr _windowHandle;
    private readonly int _id;
    private bool _registered;

    public GlobalHotkey(IntPtr windowHandle, int id, uint modifiers, uint key)
    {
        if (windowHandle == IntPtr.Zero)
            throw new ArgumentException("注册快捷键需要有效的窗口。", nameof(windowHandle));

        _windowHandle = windowHandle;
        _id = id;
        _registered = RegisterHotKey(windowHandle, id, modifiers | NoRepeat, key);
        if (!_registered)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"无法注册全局快捷键，可能已被其他程序占用（Windows 错误 {error}）。仍可使用窗口中的按钮。");
        }
    }

    public void Dispose()
    {
        if (!_registered)
            return;

        UnregisterHotKey(_windowHandle, _id);
        _registered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
