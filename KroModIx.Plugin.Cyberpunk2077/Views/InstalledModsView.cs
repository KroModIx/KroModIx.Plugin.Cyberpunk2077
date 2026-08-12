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

/// <summary>Installiert-Tab: Toolbar oben, Filter darunter, Liste der Mods
/// als Row-Cards mit Aktions-Buttons rechts. Alles code-only, Host-Styles
/// (Border.card, Button.accent/ghost/danger, TextBlock.h2/.muted/.section-label).</summary>
public sealed class InstalledModsView : UserControl
{
    public InstalledModsView()
    {
        // --- Toolbar ---
        var refreshBtn = new Button { Content = Strings.T("btn.refresh") };
        refreshBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.RefreshCommand)));
        var openBtn = new Button { Content = Strings.T("btn.open_archive_mod") };
        openBtn.Classes.Add("ghost");
        openBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.OpenModsFolderCommand)));
        var enableAllBtn = new Button { Content = Strings.T("btn.enable_all") };
        enableAllBtn.Classes.Add("ghost");
        enableAllBtn.Bind(Button.CommandProperty,
            new Binding(nameof(InstalledModsViewModel.EnableAllCommand)));
        var disableAllBtn = new Button { Content = Strings.T("btn.disable_all") };
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
            PlaceholderText = Strings.T("placeholder.search_installed"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        filter.Bind(TextBox.TextProperty,
            new Binding(nameof(InstalledModsViewModel.FilterText)) { Mode = BindingMode.TwoWay });

        // --- Row-Liste (ListBox statt ItemsControl: Kroste-Skill-Muster,
        //     damit RelativeSource FindAncestor(ListBox) mit
        //     DataContext.<Command>-Path korrekt aufloest. ItemsControl
        //     hatte den Bug dass Buttons dauerhaft disabled waren, weil
        //     die Command-Bindings nicht griffen). ---
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Single,
        };
        list.Bind(ListBox.ItemsSourceProperty,
            new Binding(nameof(InstalledModsViewModel.Rows)));
        list.ItemTemplate = new FuncDataTemplate<ModRow>((row, _) =>
            row is null ? null : BuildRowCard(), true);

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Top),
                WithDock(filter, Dock.Top),
                list,
            },
        };
    }

    private static Control BuildRowCard()
    {
        // Typ-Icon-Frame 60x60 (📦/⚙/… je Mod-Type) — konsistent mit dem
        // Kroste-Card-Look aus dem Downloads-Tab. Kein Nexus-Cover moeglich
        // bei installierten Mods (nach Install ist der Nexus-Filename-Kontext
        // weg — kein persistierter Install-Manifest im Cyberpunk-Plugin).
        var iconFrame = new Border
        {
            Width = 60, Height = 60,
            CornerRadius = new CornerRadius(6),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var typeIcon = new TextBlock
        {
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        typeIcon.Bind(TextBlock.TextProperty, new Binding("Mod.TypeIcon"));
        iconFrame.Child = typeIcon;

        var name = new TextBlock
        {
            FontWeight = FontWeight.SemiBold, FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.Bind(TextBlock.TextProperty, new Binding("Mod.Name"));

        var subtitle = new TextBlock { FontSize = 11 };
        subtitle.Classes.Add("muted");
        subtitle.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.SubtitleText)));

        var status = new TextBlock
        {
            FontSize = 10, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.StatusLabel)));
        status.Bind(TextBlock.ForegroundProperty,
            new Binding("Mod.IsEnabled") { Converter = EnabledColorConverter.Instance });

        var titleColumn = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { name, subtitle, status },
        };

        var toggleBtn = new Button();
        toggleBtn.Bind(Button.ContentProperty, new Binding(nameof(ModRow.ToggleButtonLabel)));
        BindRowCommand(toggleBtn, nameof(InstalledModsViewModel.ToggleEnabledCommand));

        var uninstallBtn = new Button { Content = Strings.T("btn.uninstall") };
        uninstallBtn.Classes.Add("danger");
        BindRowCommand(uninstallBtn, nameof(InstalledModsViewModel.UninstallCommand));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { toggleBtn, uninstallBtn },
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 8),
        };
        Grid.SetColumn(iconFrame, 0);
        Grid.SetColumn(titleColumn, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(iconFrame);
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

    /// <summary>Row-Command-Helper nach Kroste-Skill-Standard:
    /// FindAncestor(ListBox) + Path „DataContext.<Command>". Ohne dieses
    /// Muster bleiben die Buttons disabled weil das Binding null bleibt.</summary>
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
