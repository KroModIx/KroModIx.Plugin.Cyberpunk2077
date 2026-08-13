# KroModIx.Plugin.Cyberpunk2077

[![CI](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx.Plugin.Cyberpunk2077)](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/releases)

**Cyberpunk 2077 Mod-Manager** — Plugin für den
[KroModIx](https://github.com/KroModIx/KroModIx).

Erkennt alle fünf gängigen Cyberpunk-Mod-Typen, Voll-Katalog vom Nexus mit
Pagination und Server-Search, Detail-Dialog mit Screenshot-Galerie und
KI-Zusammenfassung, ZIP/RAR/7z-Auto-Install, DE+EN-UI.

## Features (v0.10)

### Neu in v0.10.0
- **Update-Install pro Row** (nur REDmods) — der Update-Checker matcht
  installierte REDmods gegen den Nexus-Katalog, Rows mit neuerer Version
  bekommen einen `⬆ vX.Y.Z`-Button. Klick lädt das primary File, installiert
  via ZIP-Installer, refresht. Nur für Premium-Nexus-Accounts (Direct-Download
  ist Premium-only).
- **REDmod-Deployment-Trigger** — `⚙ REDmod deploy`-Button in der Toolbar
  ruft `redmod.exe deploy` (nur Windows-nativ; auf Linux Hinweis auf das
  In-Game-Menu Settings → Mods → Deploy).
- **Client-side Kategorie-Filter** im Nexus-Katalog — Combo neben Sort,
  Kategorien werden live aus den geladenen Einträgen abgeleitet.
- **Cover-Loading-Progress** im Katalog-Header (`🖼 12/40`) während der
  Nachlade-Loop läuft.
- **Retrofit-Dialog** (`🔗 Nexus-Match zuweisen`) für Mods ohne Install-
  Manifest (installiert vor v0.9.0). Dialog parst Mod-URL oder Mod-ID und
  schreibt ein Manifest — ab dem nächsten Refresh greifen Cover-Enrichment
  und Update-Check.

### Installiert-Tab
- **Discovery** aller fünf Mod-Typen (< 100 ms für 200 Mods):

  | Icon | Typ | Verzeichnis | Enable/Disable |
  |---|---|---|---|
  | 📦 | Archive | `archive/pc/mod/*.archive` | `.archive.disabled`-Suffix |
  | 🎮 | REDmod | `mods/<name>/info.json` | `.disabled`-Ordner-Suffix |
  | 🔧 | CET | `bin/x64/plugins/cyber_engine_tweaks/mods/<name>/` | `.disabled`-Ordner-Suffix |
  | 🧩 | RED4ext | `red4ext/plugins/<name>/*.dll` | `.disabled`-Ordner-Suffix |
  | 📜 | redscript | `r6/scripts/<name>/` oder `r6/scripts/*.reds` | `.disabled`-Suffix |

- **Kroste-Card-Row** (v0.9) — 140×90 Cover-Frame, Titel + Meta-Zeile
  (Type · Version · Author · Size), Nexus-Summary, Actions rechts:
  Toggle / **🔍 Details** / **🗑 Deinstallieren**.
- **Cover + Nexus-Enrichment** — via Install-Manifest (`~/.config/KroModIx/
  plugin-data/kroste.cyberpunk2077/install-manifests/{Type}_{Name}.json`).
  Manifest wird beim Install automatisch geschrieben (ModId aus Nexus-CDN-
  Filename), Scanner enrichert Rows daraus.
- **Details-Button** öffnet den gleichen Nexus-Mod-Detail-Dialog wie im
  Katalog-Tab (Beschreibung, Screenshot-Galerie, KI-Zusammenfassung).
- **Enable/Disable** per Rename (Vortex-kompatibel).
- **Bulk-Aktionen** mit Progress-Scope in der Host-Statusbar.
- **REDmod-Metadata** aus `info.json` (Name, Version, Autor).

### Nexus-Tab (v0.7: Voll-Katalog via GraphQL)
- **`SearchModsAsync`** über die öffentliche Nexus-GraphQL-API
  (`api-router.nexusmods.com/graphql`) — deckt den kompletten Cyberpunk-
  Bestand ab (~23000 Mods statt der ~20 Kurzlisten-Einträge der REST-v1).
- **Pagination** mit „📚 Mehr laden"-Button (40 pro Seite), Status-Zeile
  „X von Y Mods geladen".
- **Server-side Volltextsuche** (nameStemmed MATCHES), Suchfeld + Enter
  oder 🔎 Button — kein Auto-Search-per-Keystroke.
- **Sort-Dropdown**: Neueste Updates / Neu hinzugefügt / Meistgeliked /
  Meistgeladen.
- **Kroste-Card-Row** (140×90 Cover + Meta + Actions Download/Details/
  Nexus-öffnen). Doppelklick öffnet den Detail-Dialog.
- **Zentraler Host-Nexus-Baukasten** — API-Key wird im Host-Settings-Fenster
  Tab „🌐 Nexus" verwaltet, geteilt mit Icarus + künftigen Plugins.

### Detail-Dialog (v0.5 + v0.6)
- Titel, Autor, Version, Kategorie, Aktualisiert, Endorsements aus der
  Nexus-Detail-API.
- **📸 Screenshot-Galerie** (v0.6): scraped die öffentliche Media-Tab-Seite,
  Thumbnails 200×113, Klick öffnet Fullscreen-Viewer (Escape schließt,
  Pfeiltasten navigieren). Bis zu 40 Screenshots pro Mod.
- **🤖 KI-Zusammenfassung** über den Host-KI-Provider (Ollama/Cloud).
- **🔞 ADULT-Badge** falls Nexus das Mod markiert.
- **⬇ Premium-Direct-Download** direkt im Footer (nur für Nexus-Premium).

### Downloads-Tab
- **ZIP + RAR + 7z** via SharpCompress 1.0 (v0.8.1). `SupportedExtensions`-
  Konstante — Scan filtert nach allen drei Formaten, Installer erkennt das
  Format automatisch über `ArchiveFactory.Open`.
- **Auto-Install** mit Layout-Detection: bekannte Root-Präfixe →
  direktes Extract ins Game-Root. Flat-Layout-Fallback für `.archive`-only
  → `archive/pc/mod/`.
- **Zip-Slip-Prevention** (`..`-Pfade verworfen).
- **Nexus-Enrichment pro Row** (v0.8.3): Filename-Parser extrahiert ModId
  aus dem Nexus-CDN-Filename, Cover + Author + Version + Summary werden
  async nachgezogen (250 ms Throttling).
- **Details-Button** (Nexus-Detail-Dialog analog Katalog).
- **Bulk-Install** mit Confirm-Dialog + Progress.

### Update-Discovery
- Cross-Reference REDmod-`info.json.version` vs Nexus-Katalog-latest.
- **Grüner ↑-Badge** auf der Cyberpunk-Sidebar-Kachel bei ausstehenden
  Updates (`IUpdateNotifier`) — nur echte Actionable-Updates, nicht neue
  Katalog-Einträge.

### DE+EN-Übersetzung (v0.8)
- Alle User-facing Strings (Tabs, Buttons, Statusmeldungen, Notifications,
  Dialoge, Detail-Meta-Labels) über `Strings.T(key)`-Helper mit DE/EN-
  Dictionary.
- Sprachwechsel im Host wird sofort sichtbar (ab Host v1.14.7 invalidiert
  der Tab-Cache automatisch).

## Installation

Aus [Release](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/releases)
das ZIP entpacken nach:

- **Linux:** `~/.config/KroModIx/plugins/kroste.cyberpunk2077/`
- **Windows:** `%APPDATA%\KroModIx\plugins\kroste.cyberpunk2077\`

Alternativ: 1-Klick-Install über die Install-Karte in der KroModIx-Sidebar.

**Braucht Host v1.15.1 oder neuer** (für `INexusService.SearchModsAsync`
und Sprachwechsel-Tab-Cache-Invalidate). Bei älterem Host bleibt das Plugin
mit `HostTooOld`-Status.

## Bedienung

1. Nexus-API-Key holen unter [nexusmods.com/users/myaccount?tab=api+access](https://www.nexusmods.com/users/myaccount?tab=api+access)
2. Im KroModIx **Einstellungen → 🌐 Nexus** → Key eintragen → „Speichern & Validieren".
3. Cyberpunk-Sidebar-Kachel klicken → **Nexus-Tab** öffnet den Katalog
   (23000+ Mods, sortierbar, durchsuchbar).
4. **🔍 Details** oder Doppelklick öffnet den Detail-Dialog mit
   Screenshots + KI-Zusammenfassung.
5. **⬇ Download** (Premium) legt die ZIP/RAR/7z in den Downloads-Ordner.
6. **Downloads-Tab** → **Installieren** — Auto-Layout-Detection ins Game-
   Root, Manifest wird geschrieben.
7. **Installiert-Tab** zeigt alle Mods mit Cover + Meta + Toggle/Details/
   Uninstall.

## Nicht in v0.9 — kommt später

- **REDmod-Deployment-Trigger** (`redmod.exe deploy`) nach Install/Toggle —
  Cyberpunk lädt REDmod-Änderungen sonst nicht bis zum nächsten Redeploy.
- **Update-Install-Command** pro Row (Download+Install der neuen Version).
- **Preset-Support** (Set-of-enabled-Mods pro Playthrough).
- **Kategorie-Filter im Katalog** (aktuell nur Server-Search + Sort).

## Warum `.disabled`-Suffix statt Config-File?

Cyberpunk 2077 hat keine offizielle Mod-Loader-Convention für „geladen aber
inaktiv". Das REDmod-Framework lädt jeden Unterordner mit `info.json`,
`.archive`-Files werden von der Engine unbedingt geladen. **Rename** ist
der einzige zuverlässige Weg, Mods im Ordner zu behalten aber vom Loader
zu verstecken — sowohl Vortex als auch die Community-Wiki nutzen das
Muster. Reversibel in < 1 Sekunde, keine ZIP-Extraction.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ [buymeacoffee.com/kroste](https://buymeacoffee.com/kroste)
