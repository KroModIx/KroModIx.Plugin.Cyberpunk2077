using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Downloads-Tab (Kroste-Card-Look, ListBox statt ItemsControl weil
/// die RelativeSource-Bindings pro Row auf DataContext.CommandName gehen
/// muessen — ListBoxItem-Container hat den DataContext).</summary>
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

        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Single,
        };
        list.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(DownloadsViewModel.Rows)));
        list.ItemTemplate = new FuncDataTemplate<DownloadRow>((row, _) =>
            row is null ? null : BuildRowCard(), true);

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Top),
                list,
            },
        };
    }

    private static Control BuildRowCard()
    {
        // Icon-Frame links (analog Cover im Nexus-Tab) — 60x60, KrosteSurface
        // mit 📦-Fallback. Bei Nexus-Downloads koennte man spaeter den Cover
        // via ModId-Parse aus dem Filename holen (Kroste-Skill-Muster).
        var iconFrame = new Border
        {
            Width = 60, Height = 60,
            CornerRadius = new CornerRadius(6),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var iconText = new TextBlock
        {
            Text = "📦", FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconFrame.Child = iconText;

        // Filename gross oben, mit TextTrimming damit die Buttons rechts
        // sichtbar bleiben (Cyberpunk-Downloads-Filenames sind lang:
        // "K's H10 Apartment Plus 32605 1.0 2026-08-12T16-25Z X32s3EuCx.rar").
        var name = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadRow.FileName)));

        // Meta-Zeile: Datum · Groesse (Kroste-Standard mit • Trennern)
        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var dateTb = new TextBlock { FontSize = 11 }; dateTb.Classes.Add("muted");
        dateTb.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadRow.ModifiedText)));
        var sep = new TextBlock { Text = "·", FontSize = 11 }; sep.Classes.Add("muted");
        var sizeTb = new TextBlock { FontSize = 11 }; sizeTb.Classes.Add("muted");
        sizeTb.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadRow.SizeText)));
        meta.Children.Add(dateTb);
        meta.Children.Add(sep);
        meta.Children.Add(sizeTb);

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { name, meta },
        };

        var installBtn = new Button { Content = Strings.T("btn.install") };
        installBtn.Classes.Add("accent");
        BindRowCommand(installBtn, nameof(DownloadsViewModel.InstallCommand));

        var deleteBtn = new Button { Content = Strings.T("btn.delete_file") };
        deleteBtn.Classes.Add("danger");
        BindRowCommand(deleteBtn, nameof(DownloadsViewModel.DeleteCommand));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { installBtn, deleteBtn },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(iconFrame, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(iconFrame);
        grid.Children.Add(textStack);
        grid.Children.Add(actions);

        var card = new Border { Margin = new Thickness(0, 0, 0, 8), Child = grid };
        card.Classes.Add("card");
        return card;
    }

    /// <summary>Row-Command-Binding im Kroste-Standard: FindAncestor(ListBox)
    /// + Path "DataContext.CommandName". Wichtig weil das ItemsControl-Muster
    /// (ohne "DataContext.") die Bindings nicht aufloest — die Row wird als
    /// ListBoxItem gewrappt und der DataContext liegt am Container.</summary>
    private static void BindRowCommand(Button btn, string commandName)
    {
        btn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource
            { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + commandName,
        });
        btn.Bind(Button.CommandParameterProperty, new Binding("."));
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
