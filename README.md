# KroModIx.Plugin.Cyberpunk2077

[![CI](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx.Plugin.Cyberpunk2077)](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/releases)

**Cyberpunk 2077 Mod-Manager** — Plugin für den
[KroModIx](https://github.com/KroModIx/KroModIx).

Erkennt alle fünf gängigen Cyberpunk-Mod-Typen, listet sie in einem
Installiert-Tab, ermöglicht Enable/Disable/Uninstall. Nexus-Katalog +
Downloads mit Auto-Install und Update-Discovery.

## Features (v0.4)

### Installiert-Tab
- **Discovery** aller fünf Mod-Typen (< 100 ms für 200 Mods):

  | Icon | Typ | Verzeichnis | Enable/Disable |
  |---|---|---|---|
  | 📦 | Archive | `archive/pc/mod/*.archive` | `.archive.disabled`-Suffix |
  | 🎮 | REDmod | `mods/<name>/info.json` | `.disabled`-Ordner-Suffix |
  | 🔧 | CET | `bin/x64/plugins/cyber_engine_tweaks/mods/<name>/` | `.disabled`-Ordner-Suffix |
  | 🧩 | RED4ext | `red4ext/plugins/<name>/*.dll` | `.disabled`-Ordner-Suffix |
  | 📜 | redscript | `r6/scripts/<name>/` oder `r6/scripts/*.reds` | `.disabled`-Suffix |

- **Enable/Disable** per Rename (Community-Standard, auch Vortex nutzt es).
- **Uninstall** mit Confirm-Dialog.
- **Bulk-Aktionen** mit Progress-Scope im Host-Statusbar.
- **REDmod-Metadata** aus `info.json` (Name, Version, Autor).
- **Filter** live nach Name oder Typ.

### Nexus-Tab (v0.2+)
- Aggregierter Katalog (latest_added + latest_updated + trending) für
  game_slug `cyberpunk2077` — nutzt den **zentralen Host-Nexus-Baukasten**
  (Contracts v1.14+). API-Key wird im Host-Settings-Fenster (Tab „🌐 Nexus")
  verwaltet, geteilt mit Icarus + künftigen Nexus-basierten Plugins.
- Cover-Enrichment im Hintergrund (persistenter Cache in
  `~/.cache/KroModIx/plugin-cache/kroste.cyberpunk2077/covers/`).
- Kategorien-Filter, Live-Text-Filter.
- Klick → Nexus-Detail im Browser.

### Downloads-Tab (v0.3+)
- Sammelt ZIPs in `~/.config/KroModIx/plugin-data/kroste.cyberpunk2077/downloads/`
  (Premium-Direct-Downloads oder manueller Browser-Download).
- **Auto-Install** mit Layout-Detection: erkennt ZIP-Root mit bekannten
  Präfixen (`archive/pc/mod/`, `mods/`, `bin/x64/plugins/...`, `red4ext/`,
  `r6/`) → direktes Extract ins Game-Root. Flat-Layout-Fallback für
  `.archive`-only-ZIPs → `archive/pc/mod/`.
- **Zip-Slip-Prevention** (`..`-Pfade werden verworfen).
- Bulk-Install mit Progress-Scope.

### Update-Discovery (v0.4+)
- Cross-Reference REDmod-`info.json.version` vs Nexus-Katalog-latest.
- **Grüner ↑-Badge** auf der Cyberpunk-Sidebar-Kachel bei ausstehenden
  Updates (`IUpdateNotifier`).
- Fuzzy-Name-Matching (Whitespace-strip, beide Richtungen).
- Auto-Check nach 15 s Bootstrap-Delay, keine Live-API-Belastung.
- Nur REDmods in v0.4 — andere Mod-Typen haben keine konsistente
  Version-Convention (kommt später mit Nexus-File-Metadata).

## Installation

Aus [Release](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/releases)
das ZIP entpacken nach:

- **Linux:** `~/.config/KroModIx/plugins/kroste.cyberpunk2077/`
- **Windows:** `%APPDATA%\KroModIx\plugins\kroste.cyberpunk2077\`

Alternativ: 1-Klick-Install über die Install-Karte in der KroModIx-Sidebar
(PluginIndex-Eintrag ist vorhanden).

**Braucht Host v1.14.0 oder neuer** (für den zentralen Nexus-Baukasten).

## Bedienung

1. Nexus-API-Key holen unter [nexusmods.com/users/myaccount?tab=api+access](https://www.nexusmods.com/users/myaccount?tab=api+access)
2. Im KroModIx **Einstellungen → 🌐 Nexus** → Key eintragen → „Speichern & Validieren".
3. Cyberpunk-Sidebar-Kachel klicken → **Nexus-Tab** öffnet den Katalog.
4. Cover pluppen ins Bild, „Auf Nexus öffnen" für den Detail-Flow.
5. ZIP herunterladen (Browser oder Direct wenn Premium) → landet in
   **Downloads-Tab** → **Installieren** klicken.
6. **Installiert-Tab** zeigt alle Mods, Toggle/Uninstall per Row.
7. Bei neuen Versionen erscheint der **grüne ↑-Badge** auf der Sidebar-
   Kachel und im Installiert-Tab.

## Nicht in v0.4 — kommt später

- **REDmod-Deployment-Trigger** (`redmod.exe deploy`) nach Install/Toggle —
  Cyberpunk lädt REDmod-Änderungen sonst nicht bis zum nächsten Redeploy.
- **Update-Discovery für Non-REDmod** (Archive/CET/RED4ext) via Nexus-
  Filename-Version-Extraction (analog Icarus).
- **Update-Install-Command** pro Row (Download+Install der neuen Version
  mit Alt-Uninstall).
- **Preset-Support** (Set-of-enabled-Mods pro Playthrough speichern).

## Warum `.disabled`-Suffix statt Config-File?

Cyberpunk 2077 hat keine offizielle Mod-Loader-Convention für „geladen aber
inaktiv". Das REDmod-Framework lädt jeden Unterordner mit `info.json`,
`.archive`-Files werden von der Engine unbedingt geladen. **Rename** ist
der einzige zuverlässige Weg, Mods im Ordner zu behalten aber vom Loader
zu verstecken — sowohl Vortex als auch die Community-Wiki nutzen dieses
Muster. Reversibel in < 1 Sekunde, keine ZIP-Extraction.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ [buymeacoffee.com/kroste](https://buymeacoffee.com/kroste)
