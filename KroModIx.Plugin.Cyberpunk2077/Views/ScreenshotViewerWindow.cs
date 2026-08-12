using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Fullscreen-Viewer fuer einen Nexus-Screenshot. Custom-Chrome
/// (Kroste-Standard: BorderOnly, ExtendClientAreaToDecorationsHint), laedt
/// das Full-Res-Bild async vom Nexus-CDN, zeigt Loading-Spinner solange.
/// Escape oder Click auf X schliesst. Prev/Next-Buttons wechseln durch die
/// Screenshot-Liste ohne Fenster neu zu oeffnen.</summary>
public sealed class ScreenshotViewerWindow : Window
{
    private readonly IReadOnlyList<NexusScreenshot> _screenshots;
    private readonly IHostServices _host;
    private int _index;

    private readonly Image _image;
    private readonly TextBlock _loading;
    private readonly TextBlock _counter;

    public ScreenshotViewerWindow(IReadOnlyList<NexusScreenshot> screenshots,
        int startIndex, IHostServices host)
    {
        _screenshots = screenshots;
        _index = Math.Clamp(startIndex, 0, screenshots.Count - 1);
        _host = host;

        Title = "Screenshot";
        Width = 1200;
        Height = 800;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        CanResize = true;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _loading = new TextBlock
        {
            Text = "Lade …",
            FontSize = 14,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _counter = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0),
        };

        Content = BuildContent();
        UpdateCounter();

        // Escape schliesst, Pfeile navigieren.
        KeyDown += OnKeyDown;

        _ = LoadCurrentAsync();
    }

    private Control BuildContent()
    {
        var titlebar = BuildTitleBar();

        var imagePanel = new Panel { ClipToBounds = true };
        imagePanel.Children.Add(_loading);
        imagePanel.Children.Add(_image);

        var footer = BuildFooter();

        var dp = new DockPanel();
        DockPanel.SetDock(titlebar, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        dp.Children.Add(titlebar);
        dp.Children.Add(footer);
        dp.Children.Add(imagePanel);
        return dp;
    }

    private Control BuildTitleBar()
    {
        var titleBlock = new TextBlock
        {
            Text = "Screenshot",
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        var closeBtn = new Button
        {
            Content = "✕",
            Width = 40, Height = 32,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        closeBtn.Classes.Add("chrome");
        closeBtn.Click += (_, _) => Close();

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)),
        };
        Grid.SetColumn(titleBlock, 0);
        Grid.SetColumn(closeBtn, 1);
        grid.Children.Add(titleBlock);
        grid.Children.Add(closeBtn);

        grid.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        return grid;
    }

    private Control BuildFooter()
    {
        var prev = new Button { Content = "◀ Zurück" };
        prev.Click += (_, _) => Move(-1);
        var next = new Button { Content = "Weiter ▶" };
        next.Click += (_, _) => Move(+1);
        var openBrowser = new Button { Content = "↗  Im Browser oeffnen" };
        openBrowser.Classes.Add("ghost");
        openBrowser.Click += (_, _) => _host.Shell.OpenExternalUrl(_screenshots[_index].FullUrl);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { prev, _counter, next, openBrowser },
        };
        var bar = new Border
        {
            Padding = new Thickness(14, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)),
            Child = row,
        };
        return bar;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Left: Move(-1); break;
            case Key.Right: Move(+1); break;
        }
    }

    private void Move(int delta)
    {
        if (_screenshots.Count == 0) return;
        _index = (_index + delta + _screenshots.Count) % _screenshots.Count;
        UpdateCounter();
        _ = LoadCurrentAsync();
    }

    private void UpdateCounter()
        => _counter.Text = $"{_index + 1} / {_screenshots.Count}";

    private async Task LoadCurrentAsync()
    {
        var target = _screenshots[_index].FullUrl;
        _image.Source = null;
        _loading.IsVisible = true;
        try
        {
            using var http = _host.CreateHttpClient("cyberpunk-screenshot-viewer");
            var bytes = await http.GetByteArrayAsync(target);
            // Bitmap-Decode auf einem Background-Thread, damit die UI beim
            // Laden grosser 4K-Screenshots nicht friert.
            var bmp = await Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            });
            // Race: user hat schon weiter geklickt → das aktuelle FullUrl
            // wuerde nicht mehr matchen. Wenn ja, verwerfen.
            if (_screenshots[_index].FullUrl != target) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _image.Source = bmp;
                _loading.IsVisible = false;
            });
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "Screenshot-Load fehlgeschlagen: {Url}", target);
            await Dispatcher.UIThread.InvokeAsync(() =>
                _loading.Text = "Ladefehler — Enter zum Retry, Pfeile fuer naechstes Bild.");
        }
    }
}
