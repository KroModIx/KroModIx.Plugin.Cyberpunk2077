using System.Collections.Generic;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.Cyberpunk2077.Services;

/// <summary>Uebersetzungs-Tabelle fuer alle User-facing Strings im
/// Cyberpunk-2077-Plugin. Sprachen: <c>de</c> (Fallback) + <c>en</c>.
///
/// <para>Nutzung: <c>Strings.Init(host.Localization)</c> beim Plugin-Init,
/// dann ueberall <c>Strings.T("key")</c>. Bei fehlendem Key wird der Key
/// selbst zurueckgegeben (macht Missing-Translations sofort sichtbar).</para>
///
/// <para><b>Kein Live-Refresh bei Sprachwechsel:</b> die Strings werden
/// zum View-Constructor-Zeitpunkt gelesen. Bei Sprachwechsel im Host muss
/// der User die Cyberpunk-Kachel neu waehlen (Host-Tab-Cache erzeugt dann
/// neue View-Instanzen mit den frischen Uebersetzungen) oder die App neu
/// starten. Vollreactive-Bindings waeren komplex und lohnen sich fuer den
/// seltenen Anwendungsfall nicht.</para></summary>
public static class Strings
{
    private static ILocalization? _loc;

    public static void Init(ILocalization loc) => _loc = loc;

    public static string T(string key)
    {
        var iso = _loc?.CurrentIso ?? "de";
        if (iso.StartsWith("en") && En.TryGetValue(key, out var en)) return en;
        if (De.TryGetValue(key, out var de)) return de;
        return key;
    }

    private static readonly Dictionary<string, string> De = new()
    {
        // Tab-Labels
        ["tab.installed"] = "Installiert",
        ["tab.nexus"] = "Nexus",
        ["tab.downloads"] = "Downloads",

        // Common buttons
        ["btn.refresh"] = "🔄  Aktualisieren",
        ["btn.open_folder"] = "📂  Downloads-Ordner öffnen",
        ["btn.install_all"] = "📥  Alle installieren",
        ["btn.install"] = "📥  Installieren",
        ["btn.delete"] = "🗑",
        ["btn.delete_file"] = "🗑  Löschen",
        ["btn.uninstall"] = "🗑  Deinstallieren",
        ["btn.close"] = "Schließen",
        ["btn.download"] = "⬇  Download",
        ["btn.download_long"] = "⬇  Herunterladen",
        ["btn.details"] = "🔍  Details",
        ["btn.open_nexus"] = "↗  Nexus öffnen",
        ["btn.open_nexus_long"] = "↗  Auf Nexus öffnen",
        ["btn.open_browser"] = "↗  Im Browser öffnen",
        ["btn.search"] = "🔎  Suchen",
        ["btn.load_more"] = "📚  Mehr laden",
        ["btn.ai_summary"] = "🤖  KI-Zusammenfassung",
        ["btn.prev"] = "◀ Zurück",
        ["btn.next"] = "Weiter ▶",
        ["btn.enable_all"] = "▶▶  Alle aktivieren",
        ["btn.disable_all"] = "⏸⏸  Alle deaktivieren",
        ["btn.enable"] = "▶  Aktivieren",
        ["btn.disable"] = "⏸  Deaktivieren",
        ["btn.open_archive_mod"] = "📂  archive/pc/mod/ öffnen",

        // Placeholders / tooltips
        ["placeholder.search_installed"] = "🔍 Filter nach Name oder Typ (Archive, REDmod, CET …)",
        ["placeholder.search_nexus"] = "🔍 Nexus durchsuchen — Enter zum Suchen",
        ["tooltip.refresh_catalog"] = "Katalog neu laden (erste Seite)",
        ["tooltip.sort"] = "Sortier-Reihenfolge",
        ["tooltip.premium_download"] = "Direct-Download in den Downloads-Ordner (Nexus-Premium nötig)",

        // Sort options
        ["sort.latest_update"] = "Neueste Updates",
        ["sort.latest_add"] = "Neu hinzugefügt",
        ["sort.most_endorsed"] = "Meistgeliked",
        ["sort.most_downloaded"] = "Meistgeladen",

        // Status messages
        ["status.no_api_key"] = "Kein Nexus-API-Key im Host-Settings — bitte unter 🌐 Nexus eintragen.",
        ["status.loading_catalog"] = "Lade Katalog …",
        ["status.detail_loading"] = "Detail wird geladen …",
        ["status.detail_load_error"] = "Fehler beim Laden.",
        ["status.screenshots_loading"] = "Lade Screenshots …",
        ["status.image_loading"] = "Lade …",
        ["status.image_load_error"] = "Ladefehler — Enter zum Retry, Pfeile für nächstes Bild.",
        ["status.downloads_dir_missing"] = "Downloads-Ordner existiert nicht: ",
        ["status.error_prefix"] = "Fehler: ",
        ["status.load_more_error"] = "Load-More-Fehler: ",
        ["status.mods_of"] = "{0} von {1} Mods geladen",
        ["status.mods_count"] = "{0} Mods",
        ["status.search_hint"] = " — Suche '{0}'",

        // Row / mod meta
        ["row.status_active"] = "aktiv",
        ["row.status_inactive"] = "deaktiviert",

        // Notifications
        ["notify.premium_required"] = "Direct-Download braucht Nexus-Premium. Klick \"Nexus öffnen\" für den Browser-Weg.",
        ["notify.premium_required_detail"] = "Direct-Download braucht Nexus-Premium. Klick \"Auf Nexus öffnen\" für den Browser-Weg.",
        ["notify.download_fail_check_log"] = "Download fehlgeschlagen — Log-Detail prüfen (Premium-Status? Rate-Limit?).",
        ["notify.download_no_link"] = "Nexus verweigert Download-URL — Premium-Status prüfen (Verify im Host-Settings-Tab).",
        ["notify.download_error_prefix"] = "Download-Fehler: ",
        ["notify.download_ok_prefix"] = "Heruntergeladen: ",
        ["notify.detail_wait"] = "Bitte warten bis Detail geladen ist.",
        ["notify.ai_unavailable"] = "KI-Provider nicht erreichbar — bitte in den KroModIx-Einstellungen konfigurieren.",
        ["notify.no_enabled_mods"] = "Keine aktiven Mods.",
        ["notify.no_disabled_mods"] = "Keine deaktivierten Mods.",
        ["notify.uninstalled_prefix"] = "Deinstalliert: ",
        ["notify.bulk_disable_result"] = "{0} deaktiviert, {1} Fehler.",
        ["notify.bulk_enable_result"] = "{0} aktiviert, {1} Fehler.",
        ["notify.bulk_install_result"] = "{0} installiert, {1} Fehler.",
        ["notify.no_nexus_id"] = "Keine Nexus-Mod-Id im Dateinamen erkennbar: {0}",

        // Detail-Dialog labels
        ["detail.section.description"] = "Beschreibung",
        ["detail.section.screenshots"] = "📸  Screenshots",
        ["detail.section.ai_summary"] = "🤖 KI-Zusammenfassung",
        ["detail.meta.author"] = "Autor",
        ["detail.meta.version"] = "Version",
        ["detail.meta.category"] = "Kategorie",
        ["detail.meta.updated"] = "Aktualisiert",
        ["detail.meta.endorsements"] = "Endorsements",
        ["detail.desc_placeholder"] = "Detail-Beschreibung wird geladen …",
        ["detail.desc_no_content"] = "Keine Beschreibung im Detail-Endpoint.",
        ["detail.category_unknown"] = "Kategorie #{0}",
        ["detail.busy_download"] = "…Download läuft…",
        ["detail.busy_ai"] = "…KI läuft…",
        ["detail.window_title"] = "Nexus-Mod-Detail",
        ["detail.screenshot_window_title"] = "Screenshot",

        // Dialogs + Downloads-Statusbar
        ["status.no_zips_hint"] = "Keine Archive unter {0} — Nexus-Downloads (ZIP/RAR/7z) landen hier.",
        ["status.zips_ready"] = "{0} Archiv(e) bereit zum Install.",
        ["dialog.install_all_title"] = "Alle installieren?",
        ["dialog.install_all_msg"] = "{0} ZIP(s) werden nacheinander ins Game-Root extrahiert. Fortfahren?",
        ["dialog.install_all_ok"] = "Installieren",
        ["dialog.delete_zip_title"] = "ZIP löschen?",
        ["dialog.delete_zip_msg"] = "{0} wirklich löschen? (Nur die ZIP im Downloads-Ordner, schon installierte Dateien im Game-Root bleiben.)",
        ["dialog.delete_zip_ok"] = "Löschen",
        ["dialog.install_error_title"] = "Install-Fehler",
        ["dialog.error_title"] = "Fehler",
        ["progress.install_zips"] = "Installiere {0} ZIP(s) …",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["tab.installed"] = "Installed",
        ["tab.nexus"] = "Nexus",
        ["tab.downloads"] = "Downloads",

        ["btn.refresh"] = "🔄  Refresh",
        ["btn.open_folder"] = "📂  Open downloads folder",
        ["btn.install_all"] = "📥  Install all",
        ["btn.install"] = "📥  Install",
        ["btn.delete"] = "🗑",
        ["btn.delete_file"] = "🗑  Delete",
        ["btn.uninstall"] = "🗑  Uninstall",
        ["btn.close"] = "Close",
        ["btn.download"] = "⬇  Download",
        ["btn.download_long"] = "⬇  Download",
        ["btn.details"] = "🔍  Details",
        ["btn.open_nexus"] = "↗  Open on Nexus",
        ["btn.open_nexus_long"] = "↗  Open on Nexus",
        ["btn.open_browser"] = "↗  Open in browser",
        ["btn.search"] = "🔎  Search",
        ["btn.load_more"] = "📚  Load more",
        ["btn.ai_summary"] = "🤖  AI summary",
        ["btn.prev"] = "◀ Previous",
        ["btn.next"] = "Next ▶",
        ["btn.enable_all"] = "▶▶  Enable all",
        ["btn.disable_all"] = "⏸⏸  Disable all",
        ["btn.enable"] = "▶  Enable",
        ["btn.disable"] = "⏸  Disable",
        ["btn.open_archive_mod"] = "📂  Open archive/pc/mod/",

        ["placeholder.search_installed"] = "🔍 Filter by name or type (Archive, REDmod, CET …)",
        ["placeholder.search_nexus"] = "🔍 Search Nexus — press Enter",
        ["tooltip.refresh_catalog"] = "Reload catalog (first page)",
        ["tooltip.sort"] = "Sort order",
        ["tooltip.premium_download"] = "Direct download to downloads folder (Nexus Premium required)",

        ["sort.latest_update"] = "Recently updated",
        ["sort.latest_add"] = "Recently added",
        ["sort.most_endorsed"] = "Most endorsed",
        ["sort.most_downloaded"] = "Most downloaded",

        ["status.no_api_key"] = "No Nexus API key configured — set it under 🌐 Nexus in host settings.",
        ["status.loading_catalog"] = "Loading catalog …",
        ["status.detail_loading"] = "Loading details …",
        ["status.detail_load_error"] = "Load error.",
        ["status.screenshots_loading"] = "Loading screenshots …",
        ["status.image_loading"] = "Loading …",
        ["status.image_load_error"] = "Load error — Enter to retry, arrow keys for next image.",
        ["status.downloads_dir_missing"] = "Downloads folder does not exist: ",
        ["status.error_prefix"] = "Error: ",
        ["status.load_more_error"] = "Load-More error: ",
        ["status.mods_of"] = "{0} of {1} mods loaded",
        ["status.mods_count"] = "{0} mods",
        ["status.search_hint"] = " — search '{0}'",

        ["row.status_active"] = "active",
        ["row.status_inactive"] = "disabled",

        ["notify.premium_required"] = "Direct download requires Nexus Premium. Click \"Open on Nexus\" for the browser flow.",
        ["notify.premium_required_detail"] = "Direct download requires Nexus Premium. Click \"Open on Nexus\" for the browser flow.",
        ["notify.download_fail_check_log"] = "Download failed — check log (Premium status? Rate limit?).",
        ["notify.download_no_link"] = "Nexus denied the download URL — check Premium status (Verify in host settings tab).",
        ["notify.download_error_prefix"] = "Download error: ",
        ["notify.download_ok_prefix"] = "Downloaded: ",
        ["notify.detail_wait"] = "Please wait until details are loaded.",
        ["notify.ai_unavailable"] = "AI provider not reachable — configure it in KroModIx settings.",
        ["notify.no_enabled_mods"] = "No active mods.",
        ["notify.no_disabled_mods"] = "No disabled mods.",
        ["notify.uninstalled_prefix"] = "Uninstalled: ",
        ["notify.bulk_disable_result"] = "{0} disabled, {1} error(s).",
        ["notify.bulk_enable_result"] = "{0} enabled, {1} error(s).",
        ["notify.bulk_install_result"] = "{0} installed, {1} error(s).",
        ["notify.no_nexus_id"] = "No Nexus mod-id recognizable in filename: {0}",

        ["detail.section.description"] = "Description",
        ["detail.section.screenshots"] = "📸  Screenshots",
        ["detail.section.ai_summary"] = "🤖 AI summary",
        ["detail.meta.author"] = "Author",
        ["detail.meta.version"] = "Version",
        ["detail.meta.category"] = "Category",
        ["detail.meta.updated"] = "Updated",
        ["detail.meta.endorsements"] = "Endorsements",
        ["detail.desc_placeholder"] = "Loading description …",
        ["detail.desc_no_content"] = "No description in detail endpoint.",
        ["detail.category_unknown"] = "Category #{0}",
        ["detail.busy_download"] = "…download running…",
        ["detail.busy_ai"] = "…AI running…",
        ["detail.window_title"] = "Nexus mod details",
        ["detail.screenshot_window_title"] = "Screenshot",

        ["status.no_zips_hint"] = "No archives in {0} — Nexus downloads (ZIP/RAR/7z) land here.",
        ["status.zips_ready"] = "{0} archive(s) ready to install.",
        ["dialog.install_all_title"] = "Install all?",
        ["dialog.install_all_msg"] = "{0} ZIP(s) will be extracted into the game root sequentially. Continue?",
        ["dialog.install_all_ok"] = "Install",
        ["dialog.delete_zip_title"] = "Delete ZIP?",
        ["dialog.delete_zip_msg"] = "Really delete {0}? (Only the ZIP in the downloads folder — already installed files in the game root stay.)",
        ["dialog.delete_zip_ok"] = "Delete",
        ["dialog.install_error_title"] = "Install error",
        ["dialog.error_title"] = "Error",
        ["progress.install_zips"] = "Installing {0} ZIP(s) …",
    };
}
