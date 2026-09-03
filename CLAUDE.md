# KroModIx.Plugin.Cyberpunk2077 — Projekt-CLAUDE

Plugin für Cyberpunk 2077 (Steam AppId **1091500**) am
[KroModIx](https://github.com/KroModIx/KroModIx). Skeleton aus
`KroModIx.Plugin.LS25` + `KroModIx.Plugin.Icarus` portiert; Konventionen aus
Skill `KroModIx-Plugin` (`~/.claude/skills/KroModIx-Plugin/`).

## Stand

Die maßgebliche Feature-Liste steht in der `description` in `plugin.json` —
sie wird bei jedem Release mitgepflegt und ist damit die einzige Stelle, die
nicht veralten kann. Ergänzend die GitHub-Releases des Repos.

Hier bewusst keine Versions-Momentaufnahme: die vorherige Fassung dieser Datei
beschrieb noch v0.1.0, während das Repo längst deutlich weiter war.

## Cyberpunk-Mod-Landschaft

Cyberpunk 2077 hat historisch gewachsen **fünf** parallele Mod-Loader:

1. **`.archive`** (Vanilla Engine): Assets werden alphabetisch aus
   `archive/pc/mod/` geladen. Keine Metadata, keine „inaktiv"-Semantik —
   nur laden oder nicht (Filename ohne `.archive.disabled`-Suffix).
2. **REDmod** (offiziell CDPR, seit Phantom Liberty): `mods/<name>/info.json`
   + `.archives`/`.reds`/`.tweak` in Sub-Ordnern. Muss nach jedem Install
   mit `redmod.exe deploy` deployt werden. Das Plugin macht das bewusst
   **nicht** automatisch — der Trigger sitzt als Button in der Installiert-
   Toolbar, der User entscheidet wann deployt wird.
3. **Cyber Engine Tweaks (CET)** — Lua-Scripts als DLL-Injection.
   Community-Standard für ingame-Console + Runtime-Tweaks.
4. **RED4ext** — Native-DLL-Plugin-Framework, Basis für ArchiveXL/TweakXL/
   Codeware.
5. **redscript** — Compiler für die REDscript-Sprache, patcht Base-Game-
   Skripte.

Bei Mod-Community-Installationen sind meist **alle fünf** aktiv — der User
lädt eine Nexus-ZIP herunter, entpackt sie ins Game-Root, und je nach
Inhalt landet was in welchem Ordner.

## Erledigt, nicht mehr offen

Die frühere Roadmap dieser Datei (Nexus-Katalog, Update-Discovery, ZIP-Layout-
Detection, REDmod-Deploy) ist vollständig umgesetzt — der Deploy-Trigger sitzt
seit v0.10.0 als Button in der Installiert-Toolbar und ruft `redmod.exe deploy`
aus `tools/redmod/bin/` auf, Windows-nativ. Er bleibt bewusst manuell.

## Referenzen

- **Vortex Cyberpunk-Extension** (`Nexus-Mods/vortex-games` auf GitHub) —
  Referenz für Layout-Detection + ZIP-Install-Regeln.
- **REDmod-Docs** ([wiki.redmodding.org](https://wiki.redmodding.org/redmod/))
   — offizielle info.json-Schema-Doku.
- **KroModIx-Plugin-Skill** (`~/.claude/skills/KroModIx-Plugin/`) — alle
  Kroste-Konventionen.
