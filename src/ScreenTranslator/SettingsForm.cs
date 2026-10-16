using System.Diagnostics;
using ScreenTranslator.Services;

namespace ScreenTranslator;

internal sealed class SettingsForm : Form
{
    private readonly TextBox appId = Theme.Input();
    private readonly TextBox secret = Theme.Input(true);
    private readonly NumericUpDown interval = new() { Minimum = 2, Maximum = 60, Value = 3, Width = 100 };
    private readonly NumericUpDown fontSize = new() { Minimum = 12, Maximum = 32, Value = 16, Width = 100 };
    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings current)
    {
        SuspendLayout();
        Result = current;
        Text = "设置 · 屏译 Lite";
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(550, 610); MinimumSize = new Size(540, 620);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        layout.Controls.Add(Theme.Label("百度翻译 · 通用文本翻译 API", 16F, Theme.Text));
        var privacy = Theme.Label("OCR 在本机完成。只向百度发送识别文字，不上传截图。\n接口需自行开通，额度与费用以百度控制台为准。");
        privacy.MaximumSize = new Size(460, 0); layout.Controls.Add(privacy);
        var docs = new LinkLabel { Text = "打开百度翻译开放平台，开通「通用文本翻译」", AutoSize = true, LinkColor = Theme.Accent, Margin = new Padding(0, 4, 0, 20) };
        docs.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://fanyi-api.baidu.com/") { UseShellExecute = true });
        layout.Controls.Add(docs);
        layout.Controls.Add(Theme.Label("APP ID")); layout.Controls.Add(appId);
        layout.Controls.Add(Theme.Label("密钥（仅保存在当前 Windows 用户的加密配置中）")); layout.Controls.Add(secret);
        appId.Text = current.AppId; secret.Text = current.SecretKey;
        var show = new CheckBox { Text = "显示密钥", AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        show.CheckedChanged += (_, _) => secret.UseSystemPasswordChar = !show.Checked;
        layout.Controls.Add(show);
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
        interval.Value = Math.Clamp(current.AutoIntervalSeconds, 2, 60);
        fontSize.Value = Math.Clamp(current.TranslationFontSize, 12, 32);
        options.Controls.Add(Theme.Label("自动间隔（秒） ")); options.Controls.Add(interval);
        options.Controls.Add(Theme.Label("  译文字号 ")); options.Controls.Add(fontSize);
        layout.Controls.Add(options);
        var note = Theme.Label("建议先手动翻译测试。自动模式会持续调用接口，可随时暂停。\n不要把密钥提交到代码仓库或分享给他人。");
        note.MaximumSize = new Size(460, 0); layout.Controls.Add(note);
        var languageSettings = new LinkLabel { Text = "打开 Windows 语言设置（安装英语 OCR）", AutoSize = true, LinkColor = Theme.Accent, Margin = new Padding(0, 8, 0, 12) };
        languageSettings.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });
        layout.Controls.Add(languageSettings);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        var save = Theme.Button("保存设置", true); var cancel = Theme.Button("取消");
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons);
        save.Click += (_, _) => Save(current);
        cancel.DialogResult = DialogResult.Cancel; AcceptButton = save; CancelButton = cancel;
        ResumeLayout(true);
    }

    private void Save(AppSettings current)
    {
        if (string.IsNullOrWhiteSpace(appId.Text) || string.IsNullOrWhiteSpace(secret.Text))
        {
            MessageBox.Show(this, "请填写百度翻译 APP ID 和密钥。", "设置");
            return;
        }
        var candidate = new AppSettings
        {
            AppId = appId.Text.Trim(), SecretKey = secret.Text.Trim(),
            CaptureRegion = current.CaptureRegion, AlwaysOnTop = current.AlwaysOnTop,
            AutoIntervalSeconds = (int)interval.Value, TranslationFontSize = (int)fontSize.Value
        };
        try { SettingsStore.Save(candidate); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "无法保存加密配置。请检查用户目录权限。", "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Result = candidate; DialogResult = DialogResult.OK;
    }
}
