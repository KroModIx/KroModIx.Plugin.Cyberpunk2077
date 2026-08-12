using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Katalog-Tab (v0.5): Toolbar mit Refresh + Filter + Kategorie,
/// darunter Row-Karten mit Cover, Meta-Zeile und Buttons (Download,
/// Details, Nexus öffnen). Doppelklick auf eine Row öffnet den
/// Detail-Dialog. Analog Icarus-NexusView.</summary>
public sealed class NexusView : UserControl
{
    public NexusView()
    {
        var refreshBtn = new Button { Content = "🔄  Katalog aktualisieren" };
        refreshBtn.Classes.Add("accent");
        refreshBtn.Bind(Button.CommandProperty,
            new Binding(nameof(NexusViewModel.RefreshCommand)));

        var filter = new TextBox
        {
            PlaceholderText = "🔍 Filter Name/Autor",
            Width = 240,
        };
        filter.Bind(TextBox.TextProperty,
            new Binding(nameof(NexusViewModel.FilterText)) { Mode = BindingMode.TwoWay });

        var catCombo = new ComboBox { Width = 200 };
        catCombo.Bind(ComboBox.ItemsSourceProperty, new Binding(nameof(NexusViewModel.Categories)));
        catCombo.Bind(ComboBox.SelectedItemProperty,
            new Binding(nameof(NexusViewModel.SelectedCategory)) { Mode = BindingMode.TwoWay });
        catCombo.ItemTemplate = new FuncDataTemplate<NexusCategoryOption>((c, _) =>
            c is null ? null : new TextBlock { Text = c.Name }, true);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { refreshBtn, filter, catCombo },
        };

        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(NexusViewModel.StatusText)));

        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Single,
        };
        list.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(NexusViewModel.Rows)));
        list.ItemTemplate = new FuncDataTemplate<NexusRow>((row, _) =>
            row is null ? null : BuildRowCard(), true);
        // Doppelklick auf Row öffnet Detail-Dialog.
        list.DoubleTapped += (_, _) =>
        {
            if (DataContext is NexusViewModel vm && list.SelectedItem is NexusRow row)
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
        // Cover 140x90 (Nexus liefert typischerweise 400x225 als picture_url).
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
            Text = "🌆", FontSize = 32,
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
        coverImage.Bind(Image.SourceProperty, new Binding(nameof(NexusRow.Cover)));
        coverPanel.Children.Add(coverImage);
        coverFrame.Child = coverPanel;

        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(NexusRow.Name)));

        // Meta-Zeile: Autor · Version · Aktualisiert · Endorsements
        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 2, 0, 0),
        };
        void AddMuted(string path)
        {
            var t = new TextBlock();
            t.Classes.Add("muted");
            t.Bind(TextBlock.TextProperty, new Binding(path));
            meta.Children.Add(t);
        }
        void AddSep()
        {
            var s = new TextBlock { Text = "·" };
            s.Classes.Add("muted");
            meta.Children.Add(s);
        }
        AddMuted(nameof(NexusRow.Author));
        AddSep();
        AddMuted(nameof(NexusRow.VersionDisplay));
        AddSep();
        AddMuted(nameof(NexusRow.UpdatedText));
        AddSep();
        AddMuted(nameof(NexusRow.EndorsementsText));

        var summary = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 40,
        };
        summary.Classes.Add("secondary");
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(NexusRow.Summary)));

        var textStack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { title, meta, summary },
        };

        // Premium-Download — Command via FindAncestor (ListBox.DataContext),
        // IsEnabled aber DIREKT an Row.IsPremium (RelativeSource-Bind für
        // bool klappt im FuncDataTemplate in Avalonia 12 nicht zuverlaessig).
        var downloadBtn = new Button { Content = "⬇  Download" };
        downloadBtn.Classes.Add("accent");
        downloadBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource
            { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(NexusViewModel.DownloadRowCommand),
        });
        downloadBtn.Bind(Button.CommandParameterProperty, new Binding("."));
        downloadBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(NexusRow.IsPremium)));
        ToolTip.SetTip(downloadBtn, "Direct-Download in den Downloads-Ordner (Nexus-Premium nötig)");

        var detailBtn = new Button { Content = "🔍  Details" };
        detailBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource
            { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(NexusViewModel.ShowDetailCommand),
        });
        detailBtn.Bind(Button.CommandParameterProperty, new Binding("."));

        var openBtn = new Button { Content = "↗  Nexus öffnen" };
        openBtn.Classes.Add("ghost");
        openBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource
            { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(NexusViewModel.OpenRowInBrowserCommand),
        });
        openBtn.Bind(Button.CommandParameterProperty, new Binding("."));

        var actions = new StackPanel
        {
            Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { downloadBtn, detailBtn, openBtn },
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

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
