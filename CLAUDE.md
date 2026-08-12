# KroModIx.Plugin.Cyberpunk2077 — Projekt-CLAUDE

Plugin für Cyberpunk 2077 (Steam AppId **1091500**) am
[KroModIx](https://github.com/KroModIx/KroModIx). Skeleton aus
`KroModIx.Plugin.LS25` + `KroModIx.Plugin.Icarus` portiert; Konventionen aus
Skill `KroModIx-Plugin` (`~/.claude/skills/KroModIx-Plugin/`).

## Aktueller Stand — v0.1.0

- **Discovery** aller fünf Mod-Typen (Archive, REDmod, CET, RED4ext,
  redscript) via `CyberpunkModScanner`.
- **Enable/Disable** via `.disabled`-Suffix (File → Extension, Ordner →
  Ordner-Name).
- **Uninstall** rekursiv, mit Confirm-Dialog.
- **Bulk-Aktionen** mit Progress-Scope.
- **REDmod-info.json** wird geparst (Name/Version/Author/Description).
- **Code-only View** (keine XAML) — Kroste-Host-Styles.
- **10 Tests** grün (Scanner + InstallService gegen Temp-Fixture-
  Verzeichnisse).

## Cyberpunk-Mod-Landschaft

Cyberpunk 2077 hat historisch gewachsen **fünf** parallele Mod-Loader:

1. **`.archive`** (Vanilla Engine): Assets werden alphabetisch aus
   `archive/pc/mod/` geladen. Keine Metadata, keine „inaktiv"-Semantik —
   nur laden oder nicht (Filename ohne `.archive.disabled`-Suffix).
2. **REDmod** (offiziell CDPR, seit Phantom Liberty): `mods/<name>/info.json`
   + `.archives`/`.reds`/`.tweak` in Sub-Ordnern. Muss nach jedem Install
   mit `redmod.exe deploy` deployt werden — MVP macht das noch **nicht**
   automatisch, User muss den Deployment-Schritt manuell triggern.
3. **Cyber Engine Tweaks (CET)** — Lua-Scripts als DLL-Injection.
   Community-Standard für ingame-Console + Runtime-Tweaks.
4. **RED4ext** — Native-DLL-Plugin-Framework, Basis für ArchiveXL/TweakXL/
   Codeware.
5. **redscript** — Compiler für die REDscript-Sprache, patcht Base-Game-
   Skripte.

Bei Mod-Community-Installationen sind meist **alle fünf** aktiv — der User
lädt eine Nexus-ZIP herunter, entpackt sie ins Game-Root, und je nach
Inhalt landet was in welchem Ordner.

## Nächste Schritte (v0.2+)

- **Nexus-Katalog** (analog Icarus-Plugin): API-Key-Auth, `/games/cyberpunk2077/mods`,
  Row-Cards mit Cover, Kategorien-Facette.
- **Update-Discovery** pro Mod: Version aus `info.json` (REDmod) bzw.
  Community-Convention (CET hat oft `db.json`, RED4ext manche Plugins
  `<name>.json`) gegen Nexus `latest_version` vergleichen.
- **Install aus ZIP**: Layout-Detection (welcher Root-Ordner-Name im ZIP?)
  → passende Ziel-Ordner. Muster: Nexus-ZIPs enthalten meist bereits die
  Zielordner-Struktur (`archive/pc/mod/`, `mods/<name>/`, etc.).
- **REDmod-Deployment**: `redmod.exe deploy` nach jedem Install/Toggle,
  wenn REDmods im Container sind. Ohne Deploy lädt Cyberpunk die
  Änderungen nicht.

## Referenzen

- **Vortex Cyberpunk-Extension** (`Nexus-Mods/vortex-games` auf GitHub) —
  Referenz für Layout-Detection + ZIP-Install-Regeln.
- **REDmod-Docs** ([wiki.redmodding.org](https://wiki.redmodding.org/redmod/))
   — offizielle info.json-Schema-Doku.
- **KroModIx-Plugin-Skill** (`~/.claude/skills/KroModIx-Plugin/`) — alle
  Kroste-Konventionen.
