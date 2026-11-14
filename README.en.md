# Mayday Kick

> **Developed with the assistance of 大肥鱼 (DeepSeek) and DSH.**

*[中文说明 → README.md](README.md)*

Host-side management for *Mayday Airways* lobbies: **kick players, blacklist them, scroll the crew list, show player names, set a custom lobby name**.

Nothing in the game itself is modified. The mod is a single DLL — delete it and everything is back to stock.

> **Reference mod**: the UI layout and packaging here are modeled after [MaydayUnlock](https://www.nexusmods.com/maydayairways/mods/1) by CHmadoka ([source](https://github.com/CHmadoka/more_players)), which raises the co-op player limit from 4 to 16. They touch different parts of the game, so both can be installed side by side.

---

## Features

| Feature | What it does | Who can use it |
| --- | --- | --- |
| Kick player | A red button appears at the right of each crew row in the captain's menu; clicking it removes that player from the session | Host only |
| Kick and blacklist | The lock button next to it kicks the player *and* writes their SteamID to the blacklist, so they get blocked during authentication next time | Host only |
| Crew list scrolling | When more than a screenful of players join, the list scrolls instead of cutting off the last rows | Everyone |
| Player names | The player's name is appended after the "On duty / Flying / Out" status text | Everyone |
| Custom lobby name | The host can rename the lobby from the captain's menu, or from the config file | Host only |
| Blacklist manager | A "Blacklist" button on the main menu (below Discord) opens a panel listing every entry with a per-row Remove button — unblocking takes effect immediately | Everyone (it edits a local file) |

---

## Installation

1. Download `MaydayKick-v1.0.0.zip`
2. Extract **everything** into the game root folder (the folder containing `MaydayAirways.exe`)
3. Launch the game

The archive already ships BepInEx 6 and the .NET runtime it needs — you do not have to install BepInEx separately.

On Steam: right-click the game → Manage → Browse local files to open the game folder.

### Already using BepInEx?

Just drop `BepInEx/plugins/MaydayKick.dll` into your existing `BepInEx/plugins/`. You do not need to overwrite `BepInEx/core` or `dotnet`.

It coexists fine with other BepInEx 6 plugins.

---

## Usage

### Kicking

Open the captain's menu (pause menu) as the host. In the crew list on the right, every row other than your own gets two extra buttons on the far right:

- **Red exit icon** — kick from this session
- **Grey lock icon** — kick and add to the blacklist

The target's connection is dropped and they are returned to the main menu.

The target player is resolved **at the moment you click**, not when the row was drawn, so the row recycling that happens as players join and leave cannot make you kick the wrong person.

### Blacklist

The blacklist is a plain text file, created after the first launch:

```
BepInEx\config\MaydayKick\blacklist.txt
```

One entry per line: SteamID64, a tab, then a note for yourself:

```
7656119xxxxxxxxxx	griefer
7656119yyyyyyyyyy	smoker
```

- Lines starting with `#` are comments
- The file is **hot-reloaded** — no game restart needed
- The blocked player does not need this mod installed; they are rejected during authentication with the game's own "you were removed from the session" message

If you would rather not edit the file by hand, use the **Blacklist** button on the main menu (below the Discord button). Every entry shows its note and SteamID, and the Remove button unblocks it immediately.

### Lobby name

Two ways, either works:

- Type in the field on the left of the captain's menu and press Enter
- Set `[Room] Name` in `BepInEx\config\local.mayday.kick.cfg`; the `{host}` placeholder expands to the host's Steam name. Changes sync within a few seconds.

---

## Configuration

`BepInEx\config\local.mayday.kick.cfg`

```ini
[Room]

## Custom lobby name. Leave empty to keep the game's default. Supports the {host} placeholder (host's Steam name).
# Setting type: String
# Default value:
Name =
```

---

## Did it load?

After launching, the window title becomes:

```
BepInEx 6.0.0-be.793 - MaydayAirways
```

If you see `BepInEx` there, the loader is active. Then check `BepInEx\LogOutput.log` for:

```
[MaydayKick] loaded v1.0.0 (kick + blacklist + crew scroll + room name)
```

---

## Known limitations

- **Only the host can kick.** Clients do not get these buttons — that is intentional, not a failed install.
- **Steam lobbies cannot be edited from the host side.** Steamworks offers no "remove member from lobby" call, so a kick drops the game connection; the kicked client then leaves the lobby on its own. That step happens on their machine, not yours.
- **The blacklist only blocks new connections.** Someone already in the session has to be kicked manually.
- Lobby names are capped at 63 characters by Steam and get truncated past that.

---

## Modpacks and licensing

- The mod's code is **MIT** licensed, see `LICENSE`. **You are welcome to bundle it into modpacks, use it in videos or streams, or run it on your own server — just credit the author, no permission needed.**
- One request: **please do not sell it** (for example inside a paid modpack or a paid membership). MIT does not forbid commercial use, so this is the author's request, not a license term.
- The release bundles BepInEx, Unity Doorstop and the .NET runtime, each under its own license — see `THIRD-PARTY-NOTICES.md` and `LICENSE-BepInEx.txt`.

---

## Uninstall

Delete these from the game folder; the game files were never touched:

```
winhttp.dll
doorstop_config.ini
.doorstop_version
dotnet\
BepInEx\
```

To keep the loader but disable only this mod, delete `BepInEx\plugins\MaydayKick.dll`.

---

## Building from source

Requires .NET SDK 6.0 or newer.

```
dotnet build MaydayKick.csproj -c Release
```

The output lands in `bin/Release/MaydayKick.dll`. The `GameDir` property in the csproj points at your game folder and is used to reference BepInEx and the IL2CPP interop assemblies — adjust it to your own path.

---

## Disclaimer

Modding multiplayer behaviour may violate the game's terms of service. Use it with friends; any report or ban risk is yours.
