namespace ScreenTranslator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleton = new Mutex(true, @"Local\ScreenTranslator.Lite", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("屏译 Lite 已在运行，请从任务栏打开。", "屏译 Lite");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, _) => MessageBox.Show(
            "程序遇到异常。请关闭后重新启动；如果反复发生，请记录触发步骤。", "屏译 Lite", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}
