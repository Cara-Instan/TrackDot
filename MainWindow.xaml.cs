using System;
using System.Windows;
using System.Windows.Input;
using TrackDot.ViewModels;

namespace TrackDot;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!IsActive) Hide();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        // Rounded WPF chrome is intentionally used for Windows 10 compatibility.
    }
}
