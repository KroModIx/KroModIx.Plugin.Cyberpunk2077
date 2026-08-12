using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Persistenter Cover-Bild-Cache fuer Nexus-Mod-Rows im Katalog-
/// Tab UND fuer Screenshot-Thumbnails im Detail-Dialog. Cache-Key ist
/// ein SHA1 der vollstaendigen URL — damit sind alle Bilder eindeutig
/// getrennt, auch wenn sie zum selben Mod gehoeren. 404 wird als leerer
/// <c>.404</c>-Marker mit 7-Tage-TTL persistiert damit Recheck nicht bei
/// jedem Refresh die API belastet.
///
/// <para><b>v0.6.1-Fix:</b> zuvor war der Key <c>mod_&lt;id&gt;</c> aus
/// einem Regex-Match auf <c>/mods/{id}/</c>. Nexus-CDN-URLs sind aber
/// <c>.../mods/{gameId}/images/{modId}/...</c> — der Regex matchte die
/// Game-ID (3333 fuer Cyberpunk 2077), nicht die Mod-ID. Alle Bilder aus
/// demselben Game landeten unter demselben Cache-File → jedes Row-Cover
/// und jeder Screenshot zeigte das gleiche Bild.</para></summary>
public sealed class CoverCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(7);

    private readonly HttpClient _http;
    private readonly string _dir;

    public CoverCache(HttpClient http, IHostServices host)
    {
        _http = http;
        _dir = Path.Combine(host.PluginCacheDir, "covers");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>SHA1-Hex-Hash der vollstaendigen URL — kollisions-frei,
    /// stabil (SHA1 einer festen URL ist immer dieselbe). Files landen
    /// im Cache-Dir als <c>&lt;40-hex&gt;.&lt;ext&gt;</c>.</summary>
    public static string CacheKeyFor(string url)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(url);
        var hash = System.Security.Cryptography.SHA1.HashData(bytes);
        var sb = new System.Text.StringBuilder(40);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public string? TryGetCachedPath(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var basePath = Path.Combine(_dir, CacheKeyFor(url));
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var p = basePath + ext;
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Lädt das Cover herunter (wenn nicht gecacht) und liefert
    /// den lokalen Pfad. Bei 404 → leerer .404-Marker, danach 7 Tage kein
    /// Recheck. Timeout 15 s pro Download — bei Netzausfall skip.</summary>
    public async Task<string?> GetOrDownloadCoverAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var cached = TryGetCachedPath(url);
        if (cached is not null) return cached;

        var basePath = Path.Combine(_dir, CacheKeyFor(url));
        var marker404 = basePath + ".404";
        if (File.Exists(marker404))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(marker404) < NegativeCacheTtl)
                return null;
            try { File.Delete(marker404); } catch { }
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var resp = await _http.GetAsync(url, timeout.Token);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                try { File.WriteAllText(marker404, ""); } catch { }
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                Log.Debug("Cover HTTP {Code} für {Url}", (int)resp.StatusCode, url);
                return null;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync(timeout.Token);
            var ext = GuessExtension(bytes);
            if (ext is null) return null;
            var target = basePath + ext;
            // v0.6.2: Race-safe temp filename. Vorher war der tmp-Name
            // shared (target + ".tmp") — bei parallelen Downloads derselben
            // URL (z.B. wenn NexusViewModel.RefreshAsync 8x kurz nacheinander
            // laeuft) ueberschrieb Task-A den tmp, Task-B versuchte danach
            // File.Move auf einen tmp der schon wegverschoben war → crash.
            // Mit einem GUID-suffix hat jeder Task seinen eigenen tmp;
            // File.Move-overwrite auf das gleiche target ist unproblematisch
            // (wer zuletzt gewinnt — beide Bytes-Streams sind identisch).
            var tmp = target + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(tmp, bytes, timeout.Token);
            try { File.Move(tmp, target, overwrite: true); }
            catch (IOException) { try { File.Delete(tmp); } catch { } throw; }
            return target;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Cover-Download fehlgeschlagen: {Url}", url);
            return null;
        }
    }

    private static string? GuessExtension(byte[] b)
    {
        if (b.Length < 8) return null;
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ".webp";
        return null;
    }
}
