using System.ComponentModel;
using ReestrParse.Wpf.ViewModels;

namespace ReestrParse.Wpf;

public partial class ParserMonitorWindow : System.Windows.Window
{
    private bool _allowClose;

    public ParserMonitorWindow(ParserMonitorWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        Hide();
    }

    public void CloseForApplicationExit()
    {
        _allowClose = true;
        Close();
    }
}
