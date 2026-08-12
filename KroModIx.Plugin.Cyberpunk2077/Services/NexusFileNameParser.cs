using System.Text.RegularExpressions;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Extrahiert die Nexus-Mod-Id aus einem Cyberpunk-Download-Filename
/// wie die Nexus-CDN sie vergibt. Format (empirisch, Stand 2026):
/// <c>&lt;Mod Name&gt; &lt;mod_id&gt; &lt;version&gt; &lt;yyyy-MM-ddTHH-mmZ&gt; &lt;hash&gt;.&lt;ext&gt;</c>
///
/// <para>Beispiele:</para>
/// <list type="bullet">
/// <item><c>K's H10 Apartment Plus 32605 1.0 2026-08-12T16-25Z X32s3EuCx.rar</c> → 32605</item>
/// <item><c>Liberty Stock 32353 1.1 2026-08-12T17-11Z k8lw8mSW4.zip</c> → 32353</item>
/// </list>
///
/// <para>Der Regex ankert am Timestamp-Muster + Hash + Extension (zip/rar/7z).
/// Unterschied zu Icarus: kein <c>.pak</c>-Suffix — Cyberpunk hat native
/// Archiv-Endungen.</para></summary>
public static class NexusFileNameParser
{
    private static readonly Regex Pattern = new(
        @"^(?<name>.*?)\s+(?<modId>\d+)\s+(?<version>\S+)\s+(?<timestamp>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z)\s+[A-Za-z0-9]+\.(?:zip|rar|7z)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static int? TryExtractModId(string fileName)
    {
        var m = Pattern.Match(fileName);
        if (!m.Success) return null;
        return int.TryParse(m.Groups["modId"].Value, out var id) ? id : null;
    }

    public static string? TryExtractModName(string fileName)
    {
        var m = Pattern.Match(fileName);
        return m.Success ? m.Groups["name"].Value.Trim() : null;
    }

    public static string? TryExtractVersion(string fileName)
    {
        var m = Pattern.Match(fileName);
        return m.Success ? m.Groups["version"].Value.Trim() : null;
    }
}
