using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScreenTranslator.Services;

public sealed class WindowsOcrService
{
    private readonly OcrEngine? _engine;
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);

    public bool IsAvailable => _engine is not null;
    public string? UnavailableReason { get; }

    public WindowsOcrService()
    {
        try
        {
            var english = OcrEngine.AvailableRecognizerLanguages
                .FirstOrDefault(language => language.LanguageTag.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                ?? OcrEngine.AvailableRecognizerLanguages
                    .FirstOrDefault(language => language.LanguageTag.StartsWith("en-", StringComparison.OrdinalIgnoreCase)
                        || language.LanguageTag.Equals("en", StringComparison.OrdinalIgnoreCase));

            if (english is not null)
                _engine = OcrEngine.TryCreateFromLanguage(english);

            if (_engine is null)
                UnavailableReason = "未找到 Windows 英语 OCR。请在“设置 → 时间和语言 → 语言和区域”中添加“英语（美国）”，安装语言选项中的“基本输入 / 光学字符识别”后重启本程序。";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            UnavailableReason = "Windows OCR 初始化失败。请检查英语 OCR 组件并重启本程序；如果组件已安装仍失败，可能是当前系统的桌面 OCR 兼容性问题。";
        }
    }

    public async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        token.ThrowIfCancellationRequested();
        if (_engine is null)
            throw new InvalidOperationException(UnavailableReason);

        await _recognitionGate.WaitAsync(token);
        try
        {
            using Bitmap prepared = PrepareBitmap(bitmap);
            using var png = new MemoryStream();
            prepared.Save(png, ImageFormat.Png);
            token.ThrowIfCancellationRequested();

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png.ToArray());
                await writer.StoreAsync();
                writer.DetachStream();
            }

            token.ThrowIfCancellationRequested();
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            token.ThrowIfCancellationRequested();
            OcrResult result = await _engine.RecognizeAsync(softwareBitmap);
            token.ThrowIfCancellationRequested();
            return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)).Trim();
        }
        finally
        {
            _recognitionGate.Release();
        }
    }

    private static Bitmap PrepareBitmap(Bitmap source)
    {
        double limit = OcrEngine.MaxImageDimension;
        // Modest upscaling helps small subtitles; every side remains within the OS OCR limit.
        double preferredScale = source.Height < 300 ? 2.0 : 1.0;
        double scale = Math.Min(preferredScale, limit / Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Floor(source.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(source.Height * scale));
        var prepared = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(prepared);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            return prepared;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }
}
