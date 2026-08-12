using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

public sealed class DownloadsView : UserControl
{
    public DownloadsView()
    {
        var refreshBtn = new Button { Content = Strings.T("btn.refresh") };
        refreshBtn.Bind(Button.CommandProperty,
            new Binding(nameof(DownloadsViewModel.RefreshCommand)));

        var openBtn = new Button { Content = Strings.T("btn.open_folder") };
        openBtn.Classes.Add("ghost");
        openBtn.Bind(Button.CommandProperty,
            new Binding(nameof(DownloadsViewModel.OpenDownloadsFolderCommand)));

        var installAllBtn = new Button { Content = Strings.T("btn.install_all") };
        installAllBtn.Classes.Add("accent");
        installAllBtn.Bind(Button.CommandProperty,
            new Binding(nameof(DownloadsViewModel.InstallAllCommand)));

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { refreshBtn, openBtn, installAllBtn },
        };

        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadsViewModel.StatusText)));

        var list = new ItemsControl();
        list.Bind(ItemsControl.ItemsSourceProperty,
            new Binding(nameof(DownloadsViewModel.Rows)));
        list.ItemTemplate = new FuncDataTemplate<DownloadRow>((row, _) =>
        {
            if (row is null) return null;
            var name = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 13 };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadRow.FileName)));
            var infoLine = new TextBlock { FontSize = 11 };
            infoLine.Classes.Add("muted");
            infoLine.Bind(TextBlock.TextProperty, new Binding
            {
                Converter = new Avalonia.Data.Converters.FuncValueConverter<DownloadRow, string>(r =>
                    r is null ? "" : $"{r.ModifiedText}  ·  {r.SizeText}"),
            });
            var metaStack = new StackPanel { Children = { name, infoLine } };

            var installBtn = new Button { Content = Strings.T("btn.install") };
            installBtn.Classes.Add("accent");
            installBtn.Bind(Button.CommandProperty, new Binding
            {
                Path = nameof(DownloadsViewModel.InstallCommand),
                RelativeSource = new RelativeSource
                {
                    Mode = RelativeSourceMode.FindAncestor,
                    AncestorType = typeof(ItemsControl),
                },
            });
            installBtn.Bind(Button.CommandParameterProperty, new Binding("."));

            var deleteBtn = new Button { Content = Strings.T("btn.delete") };
            deleteBtn.Classes.Add("danger");
            deleteBtn.Bind(Button.CommandProperty, new Binding
            {
                Path = nameof(DownloadsViewModel.DeleteCommand),
                RelativeSource = new RelativeSource
                {
                    Mode = RelativeSourceMode.FindAncestor,
                    AncestorType = typeof(ItemsControl),
                },
            });
            deleteBtn.Bind(Button.CommandParameterProperty, new Binding("."));

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { installBtn, deleteBtn },
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(12, 8),
            };
            Grid.SetColumn(metaStack, 0);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(metaStack);
            grid.Children.Add(actions);

            var card = new Border { Margin = new Thickness(0, 0, 0, 6), Child = grid };
            card.Classes.Add("card");
            return card;
        }, true);

        var scroll = new ScrollViewer { Content = list };

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Top),
                scroll,
            },
        };
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
