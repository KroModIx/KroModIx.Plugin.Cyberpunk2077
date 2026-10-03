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

## Archive kommen aus dem Host (ab v0.16.0)

`CyberpunkZipInstaller` bekommt `IHostServices.Archives` eingespritzt; das
Plugin entscheidet weiter, **welches** Layout ein Archiv hat und **wohin**
seine Dateien gehören — das ist Cyberpunk-Wissen. Das Öffnen der Formate und
der Ausbruch-Schutz kommen aus dem Host, SharpCompress ist aus dem Plugin
verschwunden.

**Das war kein Aufräumen.** Der eigene Schutz prüfte
`name.Contains("..")`. Am 03.10.2026 gegen den damaligen Code gemessen, mit
einem ZIP, dessen zweiter Eintrag `/tmp/ausserhalb.txt` heißt:

```
MESSUNG: Install.Success = True, Dateien = 2
MESSUNG: Datei ausserhalb des InstallDir vorhanden = True
MESSUNG: Inhalt = UEBERNOMMEN
```

Ein absoluter Eintragsname enthält kein `..`, kommt also durch den Test, und
`Path.Combine(installDir, "/tmp/ausserhalb.txt")` gibt
`/tmp/ausserhalb.txt` zurück — das Zielverzeichnis wird verworfen. Der
Install meldete Erfolg. Als Test festgehalten
(`Absoluter_Eintragsname_bricht_nicht_aus`).

**Der alte Zip-Slip-Test konnte nicht fehlschlagen.** Er benutzte
`archive/pc/mod/../../../evil.archive` — drei Ebenen hinauf aus drei Ebenen
hinein, das landet genau wieder im Spielverzeichnis. Geprüft wurde dann, dass
die Datei nicht an einer Stelle liegt, an der sie auch nie gelandet wäre.
Jetzt mit einer Ebene mehr, und der Fall „`..` bleibt unter dem Ziel" steht
als eigener Test daneben.

**Drei Entscheidungen, die der Code allein nicht hergibt:**

- **Ein Ausbruchsversuch bricht den Install ab**, statt still das zu
  installieren, was durchkam. Das trifft auch ein bloß kaputtes Archiv, bei
  dem ein Eintrag von zweihundert krumm ist — bewusst: ein Archiv, das aus
  dem Spielverzeichnis herausschreiben will, ist nicht „überwiegend in
  Ordnung", und die Entscheidung gehört dem Nutzer.
- **Die Meldung nennt, was schon geschrieben wurde.** Nach dem Abbruch muss
  der Nutzer wissen, was er aufräumen soll.
- **Der Endungs-Vorfilter kommt aus `_archives.SupportedExtensions`**, nicht
  aus einer Plugin-Konstante. Ein im Host neu unterstütztes Format muss nicht
  in neun Plugins nachgetragen werden.

Die Tests nutzen `KroModIx.Plugin.TestKit` (Paket aus demselben Host-Tag);
der Ausbruch-Schutz darin ist **nicht** nachgebaut, sondern dieselbe Funktion
`ArchivePathSafety` aus den Contracts.

## Referenzen

- **Vortex Cyberpunk-Extension** (`Nexus-Mods/vortex-games` auf GitHub) —
  Referenz für Layout-Detection + ZIP-Install-Regeln.
- **REDmod-Docs** ([wiki.redmodding.org](https://wiki.redmodding.org/redmod/))
   — offizielle info.json-Schema-Doku.
- **KroModIx-Plugin-Skill** (`~/.claude/skills/KroModIx-Plugin/`) — alle
  Kroste-Konventionen.
