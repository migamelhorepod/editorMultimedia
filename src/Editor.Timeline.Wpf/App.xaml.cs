using System;
using System.IO;
using System.Windows;
using Editor.Security;

namespace Editor.Timeline.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show("Ocorreu uma falha e o aplicativo precisará ser finalizado",
                "Falha no aplicativo", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        try
        {
            var recoveryRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MigaMelhore", "EditorMultimedia", "TaskResults");
            new MediaResultRecovery(recoveryRoot).Recover();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Ocorreu uma falha e o aplicativo precisará ser finalizado",
                "Falha de recuperação", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
    }
}
