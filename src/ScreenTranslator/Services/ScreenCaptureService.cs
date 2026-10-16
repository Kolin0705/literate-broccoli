using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenTranslator.Services;

public static class ScreenCaptureService
{
    public static Bitmap Capture(Rectangle region)
    {
        Rectangle desktop = SystemInformation.VirtualScreen;
        if (region.Width < 1 || region.Height < 1 || region.Left < desktop.Left || region.Top < desktop.Top
            || (long)region.Left + region.Width > desktop.Right || (long)region.Top + region.Height > desktop.Bottom)
            throw new ArgumentException("选取的区域已不在当前屏幕范围内，请重新框选。", nameof(region));

        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch (Exception exception) when (exception is Win32Exception or ExternalException or ArgumentException)
        {
            bitmap?.Dispose();
            throw new InvalidOperationException("无法截取屏幕。请将游戏切换到无边框窗口或窗口模式，再重新框选区域。", exception);
        }
        catch
        {
            bitmap?.Dispose();
            throw;
        }
    }
}
