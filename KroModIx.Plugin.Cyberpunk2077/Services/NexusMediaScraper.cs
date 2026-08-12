using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Scraped die oeffentliche Nexus-Media-Tab-Seite eines Mods
/// (<c>nexusmods.com/{game}/mods/{id}?tab=images</c>) und extrahiert die
/// Screenshot-URLs. Die Nexus-API v1 liefert nur das Haupt-<c>picture_url</c>,
/// nicht die Galerie — deshalb der HTML-Weg.
///
/// <para>Format der Nexus-CDN-URLs (stabil, seit Jahren unveraendert):</para>
/// <list type="bullet">
/// <item>Thumbnail: <c>staticdelivery.nexusmods.com/mods/{gameIntId}/images/thumbnails/{modId}/{file}</c></item>
/// <item>Full-Res: <c>staticdelivery.nexusmods.com/mods/{gameIntId}/images/{modId}/{file}</c> (ohne /thumbnails/)</item>
/// </list>
///
/// <para>Wenn Nexus die HTML-Struktur aendert, muss nur der Regex hier
/// angepasst werden. Die eigentliche URL-Convention (Full-Res = Thumb ohne
/// /thumbnails/) ist eine CDN-Konvention und aendert sich nicht mit
/// Site-Redesigns.</para></summary>
public sealed class NexusMediaScraper
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Extrahiert alle Thumbnail-URLs aus dem Media-Tab-HTML. Die
    /// Regex matcht sowohl <c>src="…thumbnails/…"</c> als auch
    /// <c>data-src="…thumbnails/…"</c> — Nexus setzt beides je nach Lazy-
    /// Loading-Zustand. HashSet-Dedup weil dieselbe URL im HTML mehrfach
    /// vorkommt (Preview-Overlay, Gallery-Item, Full-Screen-Trigger).</summary>
    private static readonly Regex ThumbnailUrlRegex = new(
        @"https://staticdelivery\.nexusmods\.com/mods/(?<gid>\d+)/images/thumbnails/(?<mid>\d+)/(?<file>[^""'\s]+)",
        RegexOptions.Compiled);

    private readonly HttpClient _http;

    public NexusMediaScraper(HttpClient http)
    {
        _http = http;
        // Nexus HTML-Endpoint verhaelt sich sauberer mit einem Browser-UA
        // (v.a. keine Bot-Blocker-Behandlung). Der API-UA "KroModIx/..." kommt
        // hier nicht zum Einsatz weil das nicht die REST-API ist.
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0 KroModIx");
    }

    public async Task<IReadOnlyList<NexusScreenshot>> ScrapeAsync(
        string gameSlug, int modId, CancellationToken ct = default)
    {
        var url = $"https://www.nexusmods.com/{gameSlug}/mods/{modId}?tab=images";
        try
        {
            var html = await _http.GetStringAsync(url, ct);
            var matches = ThumbnailUrlRegex.Matches(html);
            var seen = new HashSet<string>();
            var list = new List<NexusScreenshot>();
            foreach (Match m in matches)
            {
                var gid = m.Groups["gid"].Value;
                var mid = m.Groups["mid"].Value;
                var file = m.Groups["file"].Value;
                var thumb = $"https://staticdelivery.nexusmods.com/mods/{gid}/images/thumbnails/{mid}/{file}";
                if (!seen.Add(thumb)) continue;
                var full = $"https://staticdelivery.nexusmods.com/mods/{gid}/images/{mid}/{file}";
                list.Add(new NexusScreenshot(thumb, full));
            }
            Log.Debug("Nexus-Media-Scrape {Slug}/{Mod}: {N} Screenshots",
                gameSlug, modId, list.Count);
            return list;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Nexus-Media-Scrape fehlgeschlagen: {Slug}/{Mod}", gameSlug, modId);
            return Array.Empty<NexusScreenshot>();
        }
    }
}

/// <summary>Ein Screenshot aus der Nexus-Media-Galerie. <c>ThumbUrl</c> fuer
/// die Thumbnail-Leiste im Detail-Dialog, <c>FullUrl</c> fuer den
/// Fullscreen-Viewer.</summary>
public sealed record NexusScreenshot(string ThumbUrl, string FullUrl);
