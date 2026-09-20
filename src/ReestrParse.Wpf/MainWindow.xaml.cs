using ReestrParse.Wpf.ViewModels;

namespace ReestrParse.Wpf;

public partial class MainWindow : System.Windows.Window
{
    private readonly ParserMonitorWindow _monitorWindow;

    public MainWindow(
        MainWindowViewModel viewModel,
        ParserMonitorWindow monitorWindow)
    {
        InitializeComponent();
        DataContext = viewModel;
        _monitorWindow = monitorWindow;

        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    private void OpenMonitor_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_monitorWindow.IsVisible)
        {
            if (_monitorWindow.WindowState == System.Windows.WindowState.Minimized)
                _monitorWindow.WindowState = System.Windows.WindowState.Normal;

            _monitorWindow.Activate();
            return;
        }

        _monitorWindow.Owner = this;
        _monitorWindow.Show();
        _monitorWindow.Activate();
    }
}
