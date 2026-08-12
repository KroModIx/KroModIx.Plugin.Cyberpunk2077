using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>Installiert-Tab: Toolbar oben, Filter darunter, Liste der Mods
/// als Row-Cards mit Aktions-Buttons rechts. Alles code-only, Host-Styles
/// (Border.card, Button.accent/ghost/danger, TextBlock.h2/.muted/.section-label).</summary>
public sealed class InstalledModsView : UserControl
{
    public InstalledModsView()
    {
        // --- Toolbar ---
        var refreshBtn = new Button { Content = "🔄  Aktualisieren" };
        refreshBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.RefreshCommand)));
        var openBtn = new Button { Content = "📂  archive/pc/mod/ öffnen" };
        openBtn.Classes.Add("ghost");
        openBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.OpenModsFolderCommand)));
        var enableAllBtn = new Button { Content = "▶▶  Alle aktivieren" };
        enableAllBtn.Classes.Add("ghost");
        enableAllBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.EnableAllCommand)));
        var disableAllBtn = new Button { Content = "⏸⏸  Alle deaktivieren" };
        disableAllBtn.Classes.Add("ghost");
        disableAllBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.DisableAllCommand)));

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { refreshBtn, openBtn, enableAllBtn, disableAllBtn },
        };

        // --- Status + Filter ---
        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty,
            new Binding(nameof(InstalledModsViewModel.StatusText)));

        var filter = new TextBox
        {
            PlaceholderText = "🔍 Filter nach Name oder Typ (Archive, REDmod, CET …)",
            Margin = new Thickness(0, 0, 0, 8),
        };
        filter.Bind(TextBox.TextProperty,
            new Binding(nameof(InstalledModsViewModel.FilterText)) { Mode = BindingMode.TwoWay });

        // --- Row-Liste ---
        var list = new ItemsControl();
        list.Bind(ItemsControl.ItemsSourceProperty,
            new Binding(nameof(InstalledModsViewModel.Rows)));
        list.ItemTemplate = new FuncDataTemplate<ModRow>((row, _) =>
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
                WithDock(filter, Dock.Top),
                scroll,
            },
        };
    }

    private static Control BuildRowCard()
    {
        // Icon (Typ) + Name (fett) + Subtitle (Typ · Version · Author · Size)
        var typeIcon = new TextBlock
        {
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        typeIcon.Bind(TextBlock.TextProperty, new Binding("Mod.TypeIcon"));

        var name = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 14 };
        name.Bind(TextBlock.TextProperty, new Binding("Mod.Name"));

        var subtitle = new TextBlock { FontSize = 11 };
        subtitle.Classes.Add("muted");
        subtitle.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.SubtitleText)));

        var status = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
        };
        // Farbe je nach enabled/disabled — via Style-Class-Toggle wäre
        // sauberer, aber ein einfacher Foreground-Bind reicht.
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.StatusLabel)));
        status.Bind(TextBlock.ForegroundProperty,
            new Binding("Mod.IsEnabled") { Converter = EnabledColorConverter.Instance });

        var titleColumn = new StackPanel
        {
            Children = { name, subtitle, status },
            VerticalAlignment = VerticalAlignment.Center,
        };

        var toggleBtn = new Button();
        toggleBtn.Bind(Button.ContentProperty, new Binding(nameof(ModRow.ToggleButtonLabel)));
        toggleBtn.Bind(Button.CommandProperty, new Binding
        {
            Path = nameof(InstalledModsViewModel.ToggleEnabledCommand),
            RelativeSource = new RelativeSource
            {
                Mode = RelativeSourceMode.FindAncestor,
                AncestorType = typeof(ItemsControl),
            },
        });
        toggleBtn.CommandParameter = null;
        toggleBtn.Bind(Button.CommandParameterProperty, new Binding("."));

        var uninstallBtn = new Button { Content = "🗑  Deinstallieren" };
        uninstallBtn.Classes.Add("danger");
        uninstallBtn.Bind(Button.CommandProperty, new Binding
        {
            Path = nameof(InstalledModsViewModel.UninstallCommand),
            RelativeSource = new RelativeSource
            {
                Mode = RelativeSourceMode.FindAncestor,
                AncestorType = typeof(ItemsControl),
            },
        });
        uninstallBtn.Bind(Button.CommandParameterProperty, new Binding("."));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(toggleBtn);
        actions.Children.Add(uninstallBtn);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 8),
        };
        Grid.SetColumn(typeIcon, 0);
        Grid.SetColumn(titleColumn, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(typeIcon);
        grid.Children.Add(titleColumn);
        grid.Children.Add(actions);

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid,
        };
        card.Classes.Add("card");
        return card;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}

/// <summary>Grün wenn enabled, rot wenn disabled. Direkt gebunden an
/// <c>Mod.IsEnabled</c>. Nutzt Kroste-Palette-Farben via DynamicResource
/// wäre sauberer — hier Konstanten für Einfachheit (Success = grün,
/// Danger = rot). Bei Bedarf refactoren.</summary>
internal sealed class EnabledColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly EnabledColorConverter Instance = new();
    private static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    public object? Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => (value is bool b && b) ? Green : Muted;

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new System.NotSupportedException();
}
