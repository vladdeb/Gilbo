using System.Windows.Forms;

namespace GilbMetricParser;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        /*Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => LogError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError(e.ExceptionObject as Exception);*/

        Application.Run(new MainForm());
    }

    /*private static void LogError(Exception? ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "GilbMetricParser_error.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}\n\n");
            MessageBox.Show(ex?.Message ?? "Неизвестная ошибка", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // последняя линия защиты — игнорируем
        }
    }*/
}
