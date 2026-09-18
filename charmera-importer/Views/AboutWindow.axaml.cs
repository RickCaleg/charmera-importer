using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace charmera_importer.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    // Each link button carries its URL in Tag, so the XAML stays the single list of links.
    private async void OnLinkClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
