using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Katalog-Tab (v0.2): oben Toolbar (Refresh + Filter + Kategorie),
/// darunter Row-Liste. Klick auf Row → Browser-Open. Cover werden im
/// Hintergrund enrichtet.</summary>
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

        var list = new ItemsControl();
        list.Bind(ItemsControl.ItemsSourceProperty,
            new Binding(nameof(NexusViewModel.Rows)));
        list.ItemTemplate = new FuncDataTemplate<NexusRow>((row, _) =>
        {
            if (row is null) return null;
            return BuildRowCard();
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

    private static Control BuildRowCard()
    {
        var cover = new Image
        {
            Width = 96, Height = 54,
            Stretch = Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        cover.Bind(Image.SourceProperty, new Binding("Cover"));

        var name = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 14 };
        name.Bind(TextBlock.TextProperty, new Binding("Source.Name"));

        var summary = new TextBlock
        {
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap,
        };
        summary.Classes.Add("muted");
        summary.Bind(TextBlock.TextProperty, new Binding("Source.Summary"));

        var subtitle = new TextBlock { FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        subtitle.Classes.Add("secondary");
        subtitle.Bind(TextBlock.TextProperty, new Binding(nameof(NexusRow.SubtitleText)));

        var titleColumn = new StackPanel
        {
            Children = { name, summary, subtitle },
            VerticalAlignment = VerticalAlignment.Center,
        };

        var openBtn = new Button { Content = "🌐  Auf Nexus öffnen" };
        openBtn.Classes.Add("ghost");
        openBtn.Bind(Button.CommandProperty, new Binding
        {
            Path = nameof(NexusViewModel.OpenOnNexusCommand),
            RelativeSource = new RelativeSource
            {
                Mode = RelativeSourceMode.FindAncestor,
                AncestorType = typeof(ItemsControl),
            },
        });
        openBtn.Bind(Button.CommandParameterProperty, new Binding("."));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 8),
        };
        Grid.SetColumn(cover, 0);
        Grid.SetColumn(titleColumn, 1);
        Grid.SetColumn(openBtn, 2);
        grid.Children.Add(cover);
        grid.Children.Add(titleColumn);
        grid.Children.Add(openBtn);

        var card = new Border { Margin = new Thickness(0, 0, 0, 6), Child = grid };
        card.Classes.Add("card");
        return card;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
