using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using KroModIx.Plugin.Cyberpunk2077.Services;

namespace KroModIx.Plugin.Cyberpunk2077.Views;

/// <summary>v0.10.0: Retrofit-Dialog fuer alte Mods (installiert vor v0.9.0)
/// die kein InstallManifest haben. User klebt Nexus-URL oder tippt ModId ein,
/// Dialog parst und liefert die ID an den Aufrufer zurueck. Aufrufer schreibt
/// dann ein Install-Manifest damit im naechsten Refresh Cover + Meta + Update-
/// Check greift.
///
/// <para>Klein und fokussiert: kein Search-in-Nexus (das wuerde eine ganze
/// zweite Katalog-UI brauchen). Copy-Paste-URL reicht — jeder User der eh
/// den Mod von Nexus geholt hat kennt die URL.</para></summary>
public sealed class NexusMatchDialog : Window
{
    private static readonly Regex ModIdInUrl = new(
        @"/mods/(?<id>\d+)(?:\?|/|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly TextBox _input;
    private readonly TextBlock _hint;
    private int? _parsedModId;

    /// <summary>Result nach ShowDialog: parsed Nexus-Mod-Id oder null wenn abgebrochen.</summary>
    public int? Result { get; private set; }

    public NexusMatchDialog(string modName)
    {
        Title = Strings.T("dialog.nexus_match_title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var header = new TextBlock
        {
            Text = string.Format(Strings.T("dialog.nexus_match_header"), modName),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };

        _input = new TextBox
        {
            PlaceholderText = Strings.T("dialog.nexus_match_placeholder"),
            Margin = new Thickness(0, 0, 0, 6),
        };
        _input.TextChanged += (_, _) => UpdateHint();

        _hint = new TextBlock { FontSize = 11 };
        _hint.Classes.Add("muted");

        var okBtn = new Button
        {
            Content = Strings.T("dialog.nexus_match_ok"),
            IsDefault = true,
            IsEnabled = false,
        };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, e) => OnOk(e);

        var cancelBtn = new Button { Content = Strings.T("btn.close"), IsCancel = true };
        cancelBtn.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelBtn, okBtn },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children = { header, _input, _hint, buttons },
        };

        // OK-Enable-State abhaengig vom parsedModId — via UpdateHint gesetzt.
        _input.TextChanged += (_, _) => okBtn.IsEnabled = _parsedModId is not null;
        _input.Focus();
    }

    private void UpdateHint()
    {
        var text = _input.Text?.Trim() ?? "";
        _parsedModId = TryParseModId(text);
        _hint.Text = _parsedModId is int id
            ? string.Format(Strings.T("dialog.nexus_match_hint_ok"), id)
            : Strings.T("dialog.nexus_match_hint_no");
    }

    private void OnOk(RoutedEventArgs _)
    {
        Result = _parsedModId;
        Close();
    }

    /// <summary>Parst eine Nexus-URL (<c>.../cyberpunk2077/mods/12345</c>)
    /// oder eine reine numerische ID. Alles andere → null.</summary>
    public static int? TryParseModId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();
        if (int.TryParse(input, out var pure) && pure > 0) return pure;
        var m = ModIdInUrl.Match(input);
        if (m.Success && int.TryParse(m.Groups["id"].Value, out var urlId)) return urlId;
        return null;
    }
}
