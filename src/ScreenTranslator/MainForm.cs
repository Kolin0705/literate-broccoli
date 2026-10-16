using System.Diagnostics;
using ScreenTranslator.Core;
using ScreenTranslator.Services;

namespace ScreenTranslator;

internal sealed class MainForm : Form
{
    private const int SelectHotkey = 1, TranslateHotkey = 2, AutoHotkey = 3;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly WindowsOcrService ocr = new();
    private readonly TranslationCache cache = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly List<GlobalHotkey> hotkeys = [];
    private readonly List<string> hotkeyFailures = [];
    private readonly Label regionLabel = Theme.Label("尚未选择屏幕区域");
    private readonly Label status = Theme.Label("准备就绪");
    private readonly Button selectButton = Theme.Button("① 框选区域");
    private readonly Button translateButton = Theme.Button("② 翻译一次", true);
    private readonly Button autoButton = Theme.Button("开启自动");
    private readonly Button settingsButton = Theme.Button("设置");
    private readonly CheckBox topmost = new() { Text = "窗口置顶", AutoSize = true, ForeColor = Theme.Muted };
    private readonly RichTextBox source = CreateReader(11F);
    private readonly RichTextBox translation = CreateReader(16F);
    private readonly BaiduTranslator translator;
    private readonly NotifyIcon tray;
    private readonly ToolTip tips = new();
    private AppSettings settings;
    private CancellationTokenSource? activeJob;
    private bool busy, selecting, editingSettings, automatic, shuttingDown;
    private long lastRequestTimestamp;
    private readonly string? loadWarning;

    protected override bool ShowWithoutActivation => true;

    public MainForm()
    {
        SuspendLayout();
        settings = SettingsStore.Load(out loadWarning);
        translator = new BaiduTranslator(http);
        Text = "屏译 Lite · 游戏文字翻译";
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 700); MinimumSize = new Size(590, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        TopMost = settings.AlwaysOnTop;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(layout);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(Theme.Label("屏译 Lite", 23F, Theme.Text));
        topmost.Checked = settings.AlwaysOnTop; topmost.Anchor = AnchorStyles.Right;
        header.Controls.Add(topmost); layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(Theme.Label("游戏进行中，也能读懂每一句。   EN → 简体中文", 10F), 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        actions.Controls.AddRange([selectButton, translateButton, autoButton, settingsButton]); layout.Controls.Add(actions, 0, 2);
        regionLabel.AutoEllipsis = true; regionLabel.AutoSize = false; regionLabel.Dock = DockStyle.Fill;
        layout.Controls.Add(regionLabel, 0, 3);
        layout.Controls.Add(Theme.Label("识别原文", 9F, Theme.Accent), 0, 4);
        layout.Controls.Add(source, 0, 5);
        var translatedHeader = Theme.Label("中文译文", 9F, Theme.Accent); translatedHeader.Padding = new Padding(0, 12, 0, 0);
        layout.Controls.Add(translatedHeader, 0, 6);
        layout.Controls.Add(translation, 0, 7);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.Dock = DockStyle.Fill; status.AutoSize = false; status.AutoEllipsis = true; status.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(status);
        var copy = Theme.Button("复制译文"); copy.MinimumSize = new Size(86, 30); copy.Padding = new Padding(6, 2, 6, 2); copy.Margin = Padding.Empty;
        footer.Controls.Add(copy); layout.Controls.Add(footer, 0, 8);
        layout.Controls.Add(Theme.Label("Ctrl+Alt+R 框选   ·   Ctrl+Alt+T 翻译   ·   Ctrl+Alt+P 自动/暂停", 9F), 0, 9);
        tips.SetToolTip(autoButton, "每隔几秒检查选区；相同文字使用缓存。自动模式可能消耗百度 API 额度。");
        tips.SetToolTip(selectButton, "框选需要翻译的字幕或对话区域，按 Esc 取消。");
        tips.SetToolTip(regionLabel, "移动游戏窗口或更改显示器布局后，请重新框选。");
        ApplySettings(); UpdateRegionLabel();

        selectButton.Click += async (_, _) => await SelectRegionAsync();
        translateButton.Click += async (_, _) => await TranslateAsync();
        autoButton.Click += async (_, _) => await ToggleAutomaticAsync();
        settingsButton.Click += (_, _) => EditSettings();
        topmost.CheckedChanged += (_, _) => { TopMost = topmost.Checked; settings.AlwaysOnTop = topmost.Checked; SaveSettings(); };
        copy.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(translation.Text)) return;
            try { Clipboard.SetText(translation.Text); SetStatus("译文已复制"); }
            catch (System.Runtime.InteropServices.ExternalException) { SetStatus("剪贴板正被占用，请重试。", true); }
        };
        timer.Tick += async (_, _) => await TranslateAsync();
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示屏译 Lite", null, (_, _) => ShowMainWindow());
        trayMenu.Items.Add("暂停自动翻译", null, (_, _) => StopAutomatic());
        trayMenu.Items.Add("退出", null, (_, _) => Close());
        tray = new NotifyIcon { Text = "屏译 Lite", Icon = Icon ?? SystemIcons.Application, ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += (_, _) => ShowMainWindow();
        ResumeLayout(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!ocr.IsAvailable)
        {
            SetStatus(ocr.UnavailableReason ?? "Windows 英语 OCR 不可用。", true);
            MessageBox.Show(this, ocr.UnavailableReason + "\n\n可从「设置」打开 Windows 语言设置。", "英语 OCR 不可用", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (loadWarning is not null) SetStatus(loadWarning, true);
        else if (!HasCredentials) SetStatus("首次使用：打开「设置」，填写百度翻译 APP ID 和密钥。");
        else SetStatus("框选游戏字幕后，即可开始翻译。");
        if (hotkeyFailures.Count > 0)
            MessageBox.Show(this, "以下快捷键被其他程序占用：" + string.Join("、", hotkeyFailures) + "。\n仍可使用窗口按钮。", "快捷键提示");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotkey(SelectHotkey, Keys.R, "Ctrl+Alt+R");
        RegisterHotkey(TranslateHotkey, Keys.T, "Ctrl+Alt+T");
        RegisterHotkey(AutoHotkey, Keys.P, "Ctrl+Alt+P");
    }

    private void RegisterHotkey(int id, Keys key, string label)
    {
        try { hotkeys.Add(new GlobalHotkey(Handle, id, 0x0003, (uint)key)); }
        catch (InvalidOperationException) { hotkeyFailures.Add(label); }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        foreach (var hotkey in hotkeys) hotkey.Dispose();
        hotkeys.Clear(); base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && !shuttingDown && !selecting && !editingSettings)
        {
            switch (m.WParam.ToInt32())
            {
                case SelectHotkey: _ = SelectRegionAsync(); break;
                case TranslateHotkey: _ = TranslateAsync(); break;
                case AutoHotkey: _ = ToggleAutomaticAsync(); break;
            }
        }
        base.WndProc(ref m);
    }

    private async Task SelectRegionAsync()
    {
        if (busy || selecting || editingSettings) return;
        StopAutomatic(); selecting = true;
        bool wasVisible = Visible;
        Hide();
        try
        {
            await Task.Delay(180, lifetime.Token);
            using var selector = new RegionSelectionForm();
            if (selector.ShowDialog() == DialogResult.OK && selector.SelectedRegion is Rectangle selected)
            {
                settings.CaptureRegion = selected; SaveSettings(); UpdateRegionLabel();
                source.Clear(); translation.Clear(); SetStatus("区域已保存。按 Ctrl+Alt+T 翻译。");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { SetStatus("无法框选屏幕。请切换游戏到无边框窗口模式后重试。", true); }
        finally { selecting = false; if (wasVisible && !shuttingDown) Show(); }
    }

    private async Task TranslateAsync()
    {
        if (busy || selecting || editingSettings || shuttingDown) return;
        if (!ocr.IsAvailable) { StopAutomatic(); SetStatus(ocr.UnavailableReason ?? "Windows 英语 OCR 不可用。", true); return; }
        if (!HasCredentials) { StopAutomatic(); SetStatus("请先在「设置」中填写百度翻译 APP ID 和密钥。", true); return; }
        if (settings.CaptureRegion is not Rectangle region || !SystemInformation.VirtualScreen.Contains(region))
        { StopAutomatic(); SetStatus("请先框选有效的屏幕区域；显示器布局变化后需重新框选。", true); return; }

        busy = true; SetBusyControls();
        using var job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        activeJob = job;
        try
        {
            SetStatus("正在识别选区文字…");
            using var bitmap = await CaptureAsync(region, job.Token);
            string text = TextNormalizer.Normalize(await ocr.RecognizeAsync(bitmap, job.Token));
            job.Token.ThrowIfCancellationRequested();
            source.Text = text;
            if (string.IsNullOrWhiteSpace(text)) { SetStatus("未识别到文字，保留上次译文。请缩小选区或调大游戏字幕。"); return; }
            if (cache.TryGet(text, out string cached))
            { translation.Text = cached; SetStatus(automatic ? "自动中 · 文字未变，已复用译文" : "已使用本次运行的缓存译文"); return; }
            SetStatus("正在翻译…");
            if (lastRequestTimestamp != 0)
            {
                double remaining = 1200 - Stopwatch.GetElapsedTime(lastRequestTimestamp).TotalMilliseconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), job.Token);
            }
            lastRequestTimestamp = Stopwatch.GetTimestamp();
            string result = await translator.TranslateAsync(text, new TranslationCredentials(settings.AppId, settings.SecretKey), job.Token);
            job.Token.ThrowIfCancellationRequested();
            cache.Store(text, result); translation.Text = result;
            SetStatus($"{(automatic ? "自动中" : "翻译完成")} · {DateTime.Now:HH:mm:ss}");
        }
        catch (OperationCanceledException)
        {
            bool userCancelled = job.IsCancellationRequested;
            if (!shuttingDown) { if (!userCancelled) StopAutomatic(); SetStatus(userCancelled ? "已暂停" : "操作超时，自动模式已暂停。请稍后重试。", !userCancelled); }
        }
        catch (TranslationException ex) { StopAutomatic(); SetStatus(ex.Message + "（自动模式已暂停）", true); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        { StopAutomatic(); SetStatus("屏幕捕获或 OCR 失败。请使用无边框窗口模式，并检查 Windows 英语 OCR。", true); }
        catch (Exception) { StopAutomatic(); SetStatus("本次翻译未完成，自动模式已暂停。请重试或重新启动程序。", true); }
        finally { activeJob = null; busy = false; if (!shuttingDown) SetBusyControls(); }
    }

    private async Task<Bitmap> CaptureAsync(Rectangle region, CancellationToken token)
    {
        bool hideWindow = Visible && WindowState != FormWindowState.Minimized && Bounds.IntersectsWith(region);
        if (hideWindow) Hide();
        try
        {
            if (hideWindow) await Task.Delay(120, token);
            token.ThrowIfCancellationRequested();
            return ScreenCaptureService.Capture(region);
        }
        finally { if (hideWindow && !shuttingDown) Show(); }
    }

    private async Task ToggleAutomaticAsync()
    {
        if (automatic) { StopAutomatic(); SetStatus("自动翻译已暂停"); return; }
        if (busy || selecting || editingSettings) return;
        automatic = true; autoButton.Text = "暂停自动"; autoButton.ForeColor = Theme.Accent;
        timer.Start(); await TranslateAsync();
    }

    private void StopAutomatic()
    {
        bool wasAutomatic = automatic;
        automatic = false; timer.Stop();
        autoButton.Text = "开启自动"; autoButton.ForeColor = Theme.Text;
        if (wasAutomatic) activeJob?.Cancel();
    }

    private void EditSettings()
    {
        if (busy || selecting || editingSettings) return;
        StopAutomatic(); editingSettings = true;
        try
        {
            using var dialog = new SettingsForm(settings);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                settings = dialog.Result; cache.Clear(); ApplySettings();
                SetStatus("设置已保存。建议先使用「翻译一次」测试连接。");
            }
        }
        finally { editingSettings = false; }
    }

    private void ApplySettings()
    {
        timer.Interval = Math.Clamp(settings.AutoIntervalSeconds, 2, 60) * 1000;
        var previous = translation.Font;
        translation.Font = new Font("Microsoft YaHei UI", Math.Clamp(settings.TranslationFontSize, 12, 32));
        previous.Dispose();
    }

    private bool HasCredentials => !string.IsNullOrWhiteSpace(settings.AppId) && !string.IsNullOrWhiteSpace(settings.SecretKey);
    private void UpdateRegionLabel() => regionLabel.Text = settings.CaptureRegion is Rectangle r
        ? $"选区  {r.Width} × {r.Height}  ·  位置 ({r.X}, {r.Y})" : "尚未选择屏幕区域 · 推荐仅框选字幕或对话框";
    private void SetBusyControls() => selectButton.Enabled = translateButton.Enabled = settingsButton.Enabled = !busy;
    private void SetStatus(string message, bool warning = false)
    { if (shuttingDown) return; status.Text = message; status.ForeColor = warning ? Theme.Warning : Theme.Muted; tips.SetToolTip(status, message); }
    private void SaveSettings()
    {
        try { SettingsStore.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        { SetStatus("配置保存失败；当前设置仍可使用，重启后需重新设置。", true); }
    }
    private void ShowMainWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private static RichTextBox CreateReader(float size) => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = Theme.Surface, ForeColor = Theme.Text, DetectUrls = false,
        Font = new Font("Microsoft YaHei UI", size), Margin = Padding.Empty,
        ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = true
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        shuttingDown = true; timer.Stop(); lifetime.Cancel();
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose(); tray?.Dispose(); tips.Dispose(); http.Dispose(); lifetime.Dispose();
            foreach (var hotkey in hotkeys) hotkey.Dispose(); hotkeys.Clear();
        }
        base.Dispose(disposing);
    }
}
