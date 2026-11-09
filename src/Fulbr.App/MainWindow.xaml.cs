using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Fulbr.App.ViewModels;
using Microsoft.Win32;
using H.NotifyIcon;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Fulbr.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private TaskbarIcon? _tray;
    private bool _exiting;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;
        CreateTrayIcon();
        Loaded += (_, _) => _vm.NavigateCommand.Execute("Dashboard");
    }

    private void CreateTrayIcon()
    {
        try
        {
            _tray = new TaskbarIcon
            {
                IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/Fulbr.ico")),
                ToolTipText = "Fulbr — prototype, no tunnel"
            };

            var menu = new ContextMenu();
            var open = new MenuItem { Header = _vm.Loc["Open"] };
            open.Click += (_, _) => ShowFromTray();
            var exit = new MenuItem { Header = _vm.Loc["Exit"] };
            exit.Click += (_, _) => ExitApplication();
            menu.Items.Add(open);
            menu.Items.Add(new Separator());
            menu.Items.Add(exit);
            _tray.ContextMenu = menu;
            _tray.TrayMouseDoubleClick += (_, _) => ShowFromTray();
        }
        catch
        {
            // The tray is optional; the app must still open if the shell rejects it.
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async void ExitApplication()
    {
        _exiting = true;
        await _vm.ShutdownAsync();
        _tray?.Dispose();
        _tray = null;
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _vm.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        try
        {
            _vm.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown is best-effort.
        }

        _tray?.Dispose();
        _tray = null;
        base.OnClosing(e);
        Application.Current.Shutdown();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _vm.Theme = _vm.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
