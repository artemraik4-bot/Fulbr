using System.Windows;
using Fulbr.App.ViewModels;

namespace Fulbr.App;

public partial class App : Application
{
    public static MainViewModel? ViewModel { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ViewModel = new MainViewModel();
            await ViewModel.InitializeAsync();

            var window = new MainWindow(ViewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Fulbr could not start.\n\n{ex.GetType().Name}: {ex.Message}",
                "Fulbr", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
