using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Downloads-Tab (v0.8.3): Kroste-Card-Look mit Nexus-Enrichment
/// (Cover 140x90, Meta-Zeile mit Author · Version · Size · Datum, Summary,
/// Action-Buttons Install/Details/Löschen). Details öffnet den Nexus-
/// Detail-Dialog aus dem Nexus-Tab-Muster (analog Icarus).</summary>
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
        // Doppelklick auf Row = Detail-Dialog (falls Nexus-Match).
        list.DoubleTapped += (_, _) =>
        {
            if (DataContext is DownloadsViewModel vm && list.SelectedItem is DownloadRow row)
                vm.ShowDetailCommand.Execute(row);
        };

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
        // Cover-Frame 140x90 (analog Nexus-Katalog-Row). Bei Nicht-Nexus-
        // Filenames oder solange Enrichment noch laeuft: 📦-Fallback.
        var coverFrame = new Border
        {
            Width = 140, Height = 90,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
        };
        var coverPanel = new Panel();
        var coverFallback = new TextBlock
        {
            Text = "📦", FontSize = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        coverFallback.Classes.Add("muted");
        coverPanel.Children.Add(coverFallback);
        var coverImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        coverImage.Bind(Image.SourceProperty, new Binding(nameof(DownloadRow.Cover)));
        coverPanel.Children.Add(coverImage);
        coverFrame.Child = coverPanel;

        // Titel = ModName (aus Nexus-Fetch) sonst FileName-Fallback (via DisplayName).
        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadRow.DisplayName)));

        // Meta-Zeile: Author · Version · Size · Datum (analog Icarus Downloads).
        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 2, 0, 0),
        };
        void AddMuted(string path)
        {
            var t = new TextBlock(); t.Classes.Add("muted");
            t.Bind(TextBlock.TextProperty, new Binding(path));
            meta.Children.Add(t);
        }
        void AddSep()
        {
            var s = new TextBlock { Text = "·" }; s.Classes.Add("muted");
            meta.Children.Add(s);
        }
        AddMuted(nameof(DownloadRow.Author));
        AddSep();
        AddMuted(nameof(DownloadRow.VersionDisplay));
        AddSep();
        AddMuted(nameof(DownloadRow.SizeText));
        AddSep();
        AddMuted(nameof(DownloadRow.ModifiedText));

        // Summary max. 2 Zeilen, nur wenn Enrichment was geliefert hat.
        var summaryTb = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 40,
        };
        summaryTb.Classes.Add("secondary");
        summaryTb.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadRow.Summary)));
        summaryTb.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(DownloadRow.HasSummary)));

        // Original-Dateiname klein/muted als Sanity-Info (Nexus-Match kann
        // fehlgehen, dann zeigt DisplayName den Filename).
        var fileNameTb = new TextBlock
        {
            FontSize = 10, Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        fileNameTb.Classes.Add("muted");
        fileNameTb.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadRow.FileName)));

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { title, meta, summaryTb, fileNameTb },
        };

        var installBtn = new Button { Content = Strings.T("btn.install") };
        installBtn.Classes.Add("accent");
        BindRowCommand(installBtn, nameof(DownloadsViewModel.InstallCommand));

        // Details-Button: nur enabled wenn ModId aus Filename lesbar war.
        var detailBtn = new Button { Content = Strings.T("btn.details") };
        BindRowCommand(detailBtn, nameof(DownloadsViewModel.ShowDetailCommand));
        detailBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(DownloadRow.HasNexusMatch)));

        var deleteBtn = new Button { Content = Strings.T("btn.delete_file") };
        deleteBtn.Classes.Add("danger");
        BindRowCommand(deleteBtn, nameof(DownloadsViewModel.DeleteCommand));

        var actions = new StackPanel
        {
            Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { installBtn, detailBtn, deleteBtn },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(coverFrame, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(coverFrame);
        grid.Children.Add(textStack);
        grid.Children.Add(actions);

        var card = new Border { Margin = new Thickness(0, 0, 0, 8), Child = grid };
        card.Classes.Add("card");
        return card;
    }

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
