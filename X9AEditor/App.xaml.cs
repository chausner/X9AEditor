using System.Windows;

namespace X9AEditor;

/// <summary>
/// Interaktionslogik für "App.xaml"
/// </summary>
public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        string? initialFilePath = e.Args.Length > 0 ? e.Args[0] : null;

        MainWindow mainWindow = new MainWindow(initialFilePath);

        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
