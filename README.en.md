# RoomKit

> **Developed with the assistance of 大肥鱼 (DeepSeek) and DSH.**

*[中文说明 → README.md](README.md)*

Host-side management for *Mayday Airways* lobbies: **kick players, blacklist them, reject zero-level Steam accounts or numeric-only names, scroll the crew list, show player names, set a custom lobby name**.

Nothing in the game itself is modified. The mod is a single DLL — delete it and everything is back to stock.

> **Reference mod**: the UI layout and packaging here are modeled after [MaydayUnlock](https://www.nexusmods.com/maydayairways/mods/1) by CHmadoka ([source](https://github.com/CHmadoka/more_players)), which raises the co-op player limit from 4 to 16. They touch different parts of the game, so both can be installed side by side.

---

## Features

| Feature | What it does | Who can use it |
| --- | --- | --- |
| Kick player | A red button appears at the right of each crew row in the captain's menu; clicking it removes that player from the session | Host only |
| Kick and blacklist | The lock button next to it kicks the player *and* writes their SteamID to the blacklist, so they get blocked during authentication next time | Host only |
| Whitelist | The host can add/remove crew members from the captain's menu; the list is available from the main menu; listed players bypass blacklist and automatic name/Steam-level kicks | Everyone (manages the host's local file) |
| Player filters | Independently checks zero Steam level, numeric-only Steam names, and numeric-only in-game names; Steam level can be logged or used to kick separately | Host only |
| Crew list scrolling | When more than a screenful of players join, the list scrolls instead of cutting off the last rows | Everyone |
| Player names | The player's name is appended after the "On duty / Flying / Out" status text | Everyone |
| Custom lobby name | The host can rename the lobby from the captain's menu, or from the config file | Host only |
| Blacklist manager | A "Blacklist" button on the main menu (below Discord) opens a panel listing every entry with a per-row Remove button — unblocking takes effect immediately | Everyone (it edits a local file) |

---

## Installation

1. Download `RoomKit-v1.0.0.zip`
2. Extract **everything** into the game root folder (the folder containing `MaydayAirways.exe`)
3. Launch the game

The archive already ships BepInEx 6 and the .NET runtime it needs — you do not have to install BepInEx separately.

On Steam: right-click the game → Manage → Browse local files to open the game folder.

### Already using BepInEx?

Just drop `BepInEx/plugins/RoomKit.dll` into your existing `BepInEx/plugins/`. You do not need to overwrite `BepInEx/core` or `dotnet`.

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
BepInEx\config\RoomKit\blacklist.txt

When upgrading, move any existing files from `BepInEx\config\MaydayKick` to `BepInEx\config\RoomKit`.
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

### Whitelist

As the host, use the check button on a crew row to add or remove that player. The **Whitelist** button near the Discord/Blacklist buttons on the main menu opens the list, where entries can also be removed. The file is:

```
BepInEx\config\RoomKit\whitelist.txt
```

Each line contains the SteamID64, a tab, the player's name, and an optional note preceded by another tab:

```
7656119xxxxxxxxxx	FriendName	Trusted teammate
7656119yyyyyyyyyy	Regular player
```

Lines starting with `#` are comments, and edits are hot-reloaded. The whitelist takes precedence over the blacklist and bypasses automatic numeric-name and Steam-level kicks; adding someone to the whitelist also removes them from the blacklist. Whitelisted crew rows hide the blacklist button. To blacklist them, remove them from the whitelist first. The host can still manually kick a whitelisted player.

### Player filters

Steam level is queried asynchronously from the public Steam Community profile at `https://steamcommunity.com/profiles/{SteamID64}`; the player does not need to be the host's friend. Players enter the game first and are then queued for background lookup. `SteamLevelQueryConcurrency` sets the maximum simultaneous requests (default 5, range 1-32). `SteamLevelMinimum` sets the minimum accepted Steam level (default 1, minimum 1); a profile level below it, including level 0, is unqualified. A profile is identified as private only when the page contains the `profile_private_info` marker. A missing level marker is also a recognized profile result, not a lookup failure; when enabled, `KickZeroSteamLevel` handles these according to the profile qualification rules. `KickOnSteamLevelLookupFailure` controls only timeouts, request failures, and other errors that prevent the lookup from completing (default false, so players are kept). `LogSteamLevel` independently controls logging query results and failure details. Results are cached for 10 minutes, failures for 1 minute. Numeric-only Steam and in-game names remain separately controlled by `BlockNumericSteamName` and `BlockNumericInGameName`; leading/trailing spaces do not affect those checks.

### Lobby name

Two ways, either works:

- Type in the field on the left of the captain's menu and press Enter
- Set `[Room] Name` in `BepInEx\config\local.mayday.kick.cfg`; the `{host}` placeholder expands to the host's Steam name. Changes sync within a few seconds.

---

## Configuration

`BepInEx\config\local.mayday.kick.cfg`

Lobby names automatically append ` | 下限Steam公开Lv<level>`, using `SteamLevelMinimum` (minimum 1). The complete name is limited to Steam's 63-character maximum.

```ini
[Room]

## Custom lobby name. Leave empty to keep the game's default. Supports the {host} placeholder (host's Steam name).
# Setting type: String
# Default value:
Name =

[Moderation]

## Log Steam level query results without kicking.
# Setting type: Boolean
# Default value: true
LogSteamLevel = true

## Kick unqualified profiles after players enter the game; failed lookups are controlled separately.
# Setting type: Boolean
# Default value: false
KickZeroSteamLevel = false

## Kick on Steam level lookup timeouts, request failures, or other unexpected errors; false keeps the player.
# Setting type: Boolean
# Default value: false
KickOnSteamLevelLookupFailure = false

## Minimum required Steam level; players below it are kicked when kicking is enabled (minimum 1).
# Setting type: Int32
# Default value: 1
SteamLevelMinimum = 1

## Maximum concurrent Steam profile requests (range 1-32).
# Setting type: Int32
# Default value: 5
SteamLevelQueryConcurrency = 5

## Reject players whose Steam name consists only of digits.
# Setting type: Boolean
# Default value: true
BlockNumericSteamName = true

## Reject players whose in-game name consists only of digits.
# Setting type: Boolean
# Default value: true
BlockNumericInGameName = true
```

---

## Did it load?

After launching, the window title becomes:

```
BepInEx 6.0.0-be.793 - MaydayAirways
```

If you see `BepInEx` there, the loader is active. Then check `BepInEx\LogOutput.log` for:

```
[RoomKit] loaded v1.0.0 (kick + blacklist + whitelist + queued Steam checks + player filters + crew scroll + room name)
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

To keep the loader but disable only this mod, delete `BepInEx\plugins\RoomKit.dll`.

---

## Building from source

Requires .NET SDK 6.0 or newer.

The C# source and project file for this mod live in `src/RoomKit/`. Additional mods can use their own named directories under `src/` and build independently.

```
dotnet build src\RoomKit\RoomKit.csproj -c Release
```

The output lands in `src/RoomKit/bin/Release/RoomKit.dll`. The `GameDir` property in the project file points at your game folder and is used to reference BepInEx and the IL2CPP interop assemblies — adjust it to your own path.

---

## Disclaimer

Modding multiplayer behaviour may violate the game's terms of service. Use it with friends; any report or ban risk is yours.
