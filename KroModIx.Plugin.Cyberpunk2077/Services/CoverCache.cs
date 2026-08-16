using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
/// <para><b>v0.11.0:</b> Umstellung auf <see cref="GetOrDownloadBytesAsync"/> —
/// Cache liefert nur noch Rohbytes, den Bitmap-Decode (inkl. WebP/AVIF/DDS-
/// Fallback und Thread-Affinity) macht ab jetzt der zentrale
/// <see cref="IImageDecoder"/>-Baukasten im Host (Contracts v1.18+). Cache-
/// Dateien haben einheitlich Endung <c>.img</c> statt der frueheren
/// Format-abhaengigen Extensions.</para>
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
    private readonly IHostServices _host;
    private readonly string _dir;

    public CoverCache(HttpClient http, IHostServices host)
    {
        _http = http;
        _host = host;
        _dir = Path.Combine(host.PluginCacheDir, "covers");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>SHA1-Hex-Hash der vollstaendigen URL — kollisions-frei,
    /// stabil (SHA1 einer festen URL ist immer dieselbe). Files landen
    /// im Cache-Dir als <c>&lt;40-hex&gt;.img</c>.</summary>
    public static string CacheKeyFor(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hash = SHA1.HashData(bytes);
        var sb = new StringBuilder(40);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Laed URL herunter (oder liest aus Cache) und liefert die
    /// Rohbytes. Beim Cache-Miss: HTTP-GET mit 15 s Timeout, Magic-Byte-
    /// Check via <see cref="IImageDecoder.LooksLikeImage"/> (verhindert dass
    /// eine HTML-Login-Wall im Cache landet), atomarer tmp+move ins Cache-
    /// File. 404 wird als leerer <c>.404</c>-Marker mit 7-Tage-TTL persistiert.
    /// Rueckgabe: Bytes oder null.</summary>
    public async Task<byte[]?> GetOrDownloadBytesAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var basePath = Path.Combine(_dir, CacheKeyFor(url));
        var cachedPath = basePath + ".img";
        if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0)
        {
            try { return await File.ReadAllBytesAsync(cachedPath, ct); }
            catch (Exception ex)
            {
                Log.Debug(ex, "Cover-Cache-Read fehlgeschlagen: {Path}", cachedPath);
            }
        }

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
            if (bytes.Length == 0) return null;
            // Sanity: nicht cachen wenn's kein Bild ist (Login-Wall/HTML/JSON).
            if (!_host.Images.LooksLikeImage(bytes))
            {
                Log.Debug("URL liefert kein Bild — wird nicht gecached: {Url}", url);
                return null;
            }
            // v0.6.2: race-safe temp filename. Vorher war der tmp-Name
            // shared (target + ".tmp") — bei parallelen Downloads derselben
            // URL ueberschrieb Task-A den tmp, Task-B versuchte danach
            // File.Move auf einen tmp der schon wegverschoben war → crash.
            var tmp = cachedPath + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(tmp, bytes, timeout.Token);
            try { File.Move(tmp, cachedPath, overwrite: true); }
            catch (IOException) { try { File.Delete(tmp); } catch { } throw; }
            return bytes;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Cover-Download fehlgeschlagen: {Url}", url);
            return null;
        }
    }
}
