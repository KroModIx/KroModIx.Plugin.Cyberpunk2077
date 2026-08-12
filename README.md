# KroModIx.Plugin.Cyberpunk2077

[![CI](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx.Plugin.Cyberpunk2077)](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/releases)

**Cyberpunk 2077 Mod-Manager** — Plugin für den
[KroModIx](https://github.com/KroModIx/KroModIx).

Erkennt alle fünf gängigen Cyberpunk-Mod-Typen, listet sie in einem
Installiert-Tab, ermöglicht Enable/Disable/Uninstall und Bulk-Aktionen.

## Erkannte Mod-Typen

| Icon | Typ | Verzeichnis | Enable/Disable |
|---|---|---|---|
| 📦 | Archive | `archive/pc/mod/*.archive` | `.archive.disabled`-Suffix |
| 🎮 | REDmod | `mods/<name>/info.json` | `.disabled`-Ordner-Suffix |
| 🔧 | CET | `bin/x64/plugins/cyber_engine_tweaks/mods/<name>/` | `.disabled`-Ordner-Suffix |
| 🧩 | RED4ext | `red4ext/plugins/<name>/*.dll` | `.disabled`-Ordner-Suffix |
| 📜 | redscript | `r6/scripts/<name>/` oder `r6/scripts/*.reds` | `.disabled`-Suffix |

## Features (v0.1)

- **Discovery** aller fünf Mod-Typen in einem Rutsch (< 100 ms für 200 Mods).
- **Enable/Disable** per Rename mit `.disabled`-Suffix — Cyberpunk kennt
  selbst keine „disabled"-Semantik, aber das Suffix ist das etablierte
  Community-Muster (Vortex nutzt es auch).
- **Uninstall** — File-/Ordner-Delete rekursiv, mit Confirm-Dialog.
- **Bulk-Aktionen** — „Alle aktivieren" / „Alle deaktivieren" mit
  Progress-Scope im Host-Statusbar.
- **REDmod-Metadata** aus `info.json` (Name, Version, Autor, Description).
- **Filter** live nach Name oder Typ.
- **📂 Ordner öffnen** — springt in `archive/pc/mod/` (häufigster Mod-Ordner).

## Nicht in v0.1 — kommt später

- **Nexus-Katalog** + API-Key-Integration (analog Icarus-Plugin) — v0.2
- **Update-Discovery** pro Mod via Nexus-Version-Compare — v0.2
- **Cover-Enrichment** für Row-Cards — v0.2
- **Install aus ZIP** — v0.3 (jede Nexus-ZIP hat anderes Layout, braucht
  Mod-Metadata für automatische Zuordnung)
- **REDmod-Deployment-Trigger** (`redmod.exe deploy` nach Install) — v0.3
- **Preset-Support** (Set-of-enabled-Mods speichern für Playthroughs) — später

## Installation

Aus [Release](https://github.com/KroModIx/KroModIx.Plugin.Cyberpunk2077/releases)
das ZIP entpacken nach:

- **Linux:** `~/.config/KroModIx/plugins/kroste.cyberpunk2077/`
- **Windows:** `%APPDATA%\KroModIx\plugins\kroste.cyberpunk2077\`

Alternativ: 1-Klick-Install über die Install-Karte in der KroModIx-Sidebar
(sobald der PluginIndex den Eintrag hat).

**Braucht Host v1.7.0 oder neuer.**

## Bedienung

1. Cyberpunk 2077 aus Steam-Discovery → Sidebar-Kachel klicken.
2. **Installiert-Tab** öffnet — Liste aller Mods aus den fünf bekannten
   Ordnern.
3. Pro Row: **▶ Aktivieren / ⏸ Deaktivieren** oder **🗑 Deinstallieren**.
4. Bulk: **▶▶ Alle aktivieren** / **⏸⏸ Alle deaktivieren** in der Toolbar.
5. **🔄 Aktualisieren** nach externer Änderung (Vortex-Install etc.).

## Warum `.disabled`-Suffix statt Config-File?

Cyberpunk 2077 hat keine offizielle Mod-Loader-Convention für „geladen aber
inaktiv". Das REDmod-Framework (`mods/`) lädt jeden Unterordner mit
`info.json`, `.archive`-Files werden von der Engine unbedingt geladen, CET
lädt jeden Ordner in `mods/`. **Rename** ist der einzige zuverlässige Weg,
Mods im Ordner zu behalten aber vom Loader zu verstecken — sowohl Vortex
als auch die Community-Wiki nutzen dieses Muster. Vorteil gegenüber Löschen +
Backup: reversibel in < 1 Sekunde, keine ZIP-Extraction.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ [buymeacoffee.com/kroste](https://buymeacoffee.com/kroste)
