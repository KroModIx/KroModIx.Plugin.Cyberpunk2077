using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Lädt Nexus-Mod-Files ins <see cref="CyberpunkPaths.DownloadsDir"/>.
/// Nur für Premium-Nexus-Accounts — <see cref="INexusService.GetDownloadLinkAsync"/>
/// liefert bei Non-Premium null. Für Non-Premium-Nutzer bleibt der Browser-
/// Weg (User klickt manuell auf Nexus und legt das ZIP im Downloads-Ordner
/// ab; DownloadsTab pickt es beim nächsten Refresh auf).</summary>
public sealed class CyberpunkDownloader
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly INexusService _nexus;
    private readonly HttpClient _http;
    private readonly CyberpunkPaths _paths;

    public CyberpunkDownloader(INexusService nexus, HttpClient http, CyberpunkPaths paths)
    {
        _nexus = nexus;
        _http = http;
        _paths = paths;
    }

    /// <summary>Sucht das primäre MAIN-File, holt Download-Link, streamt
    /// runter. Rückgabe: lokaler Pfad oder null bei Fehler.</summary>
    public async Task<string?> DownloadPrimaryAsync(int modId, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var files = await _nexus.GetFilesAsync(CyberpunkNexusCatalog.GameSlug, modId, ct);
        var primary = files.FirstOrDefault(f => f.IsPrimary && f.CategoryId == 1) // MAIN + primary
            ?? files.FirstOrDefault(f => f.CategoryId == 1) // MAIN irgendein
            ?? files.FirstOrDefault(); // egal welches
        if (primary is null)
        {
            Log.Warn("Cyberpunk-Download: kein File fuer mod_id={Id}", modId);
            return null;
        }
        return await DownloadFileAsync(modId, primary, progress, ct);
    }

    public async Task<string?> DownloadFileAsync(int modId, NexusFileEntry file,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var url = await _nexus.GetDownloadLinkAsync(CyberpunkNexusCatalog.GameSlug, modId, file.FileId, ct);
        if (url is null)
        {
            Log.Warn("Cyberpunk-Download: kein Link fuer mod_id={Id} file_id={FileId} (Premium noetig?)",
                modId, file.FileId);
            return null;
        }

        // Filename entweder aus dem File-Manifest oder aus dem URL (Query-
        // String-Parameter "response-content-disposition" oder Path-Basename).
        var filename = string.IsNullOrEmpty(file.FileName)
            ? Path.GetFileName(new Uri(url).AbsolutePath)
            : file.FileName;
        // Sanitize — nur ASCII/allowed
        foreach (var c in Path.GetInvalidFileNameChars())
            filename = filename.Replace(c, '_');
        var target = Path.Combine(_paths.DownloadsDir, filename);

        try
        {
            using var resp = await _http.GetAsync(url,
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            var tmp = target + ".part";
            await using (var input = await resp.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(tmp))
            {
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await input.ReadAsync(buf, ct)) > 0)
                {
                    await output.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total is > 0 && progress is not null)
                        progress.Report((double)done / total.Value);
                }
            }
            File.Move(tmp, target, overwrite: true);
            Log.Info("Cyberpunk-Download: {File} ({Size} bytes)", filename, new FileInfo(target).Length);
            return target;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Cyberpunk-Download fehlgeschlagen: mod_id={Id}", modId);
            return null;
        }
    }
}
