# FCP (working title)

**English** | [中文](README.md)

A Minecraft: Java Edition launcher for Windows, built for players coming from **PCL2** (Plain Craft Launcher 2), one of the most widely used launchers in the Chinese Minecraft community.

- **Familiar** – the launch page, version selection, version settings and options follow the layout and workflow PCL2 users already know, so there is nothing new to learn.
- **Simple** – it automatically finds the games, versions, mods and saves already on the computer, picks a suitable Java runtime, allocates memory, and explains problems in plain language.
- **Stable** – existing game folders are used in place. PCL2's files and settings are only read, never modified, so both launchers can be used side by side.

> Status: in development, not yet released. It can already detect PCL2 game folders, versions, mods, saves and settings, and launch vanilla, Forge and Fabric with an offline profile.

## What it does
- Detects existing game folders and versions (vanilla, Forge, NeoForge, Fabric, Quilt, OptiFine; HMCL-style and inherited version JSON).
- Imports the player's PCL2 settings once (memory, version isolation, launcher behaviour, JVM arguments, Java selection) and keeps per-version settings in effect.
- Selects and, when necessary, downloads the official Java runtime that Mojang provides for each game version.
- Launches the game, shows progress, records play time, and reports crashes with the last lines of the game log.
- Lists and manages local mods (enable / disable), resource packs, shader packs and saves.

## What it does not do
- **It does not download or distribute Minecraft itself.** The launcher only starts copies of the game that are already installed on the player's computer. Players must own the game.
- It does not read, decrypt or reuse credentials stored by other launchers.

## Microsoft account sign-in (planned)
Signing in with a Microsoft account is planned and is waiting for access to the Minecraft Services API. When implemented it will work as follows:

- **Flow**: OAuth 2.0 device code flow with the Microsoft identity platform → Xbox Live → XSTS → Minecraft Services, using the launcher's own Azure application. The user signs in on Microsoft's own web page; the launcher never sees the password.
- **Purpose**: only to verify that the player owns Minecraft: Java Edition, obtain the player's profile (name, UUID, skin) and start the game with a valid session.
- **Storage**: tokens are stored only on the player's computer, encrypted with Windows DPAPI for the current Windows user. They are never written to logs and never sent anywhere other than the Microsoft / Xbox / Minecraft endpoints listed above.
- **No telemetry**: the launcher does not collect or upload any personal data.

## Building
Requires Windows and the .NET 10 SDK.

```
dotnet build FCP.slnx
dotnet test --solution FCP.slnx
dotnet publish src/FCP.App -c Release -o publish
```

Every push is built and tested by GitHub Actions; the resulting single-file `FCP.exe` is attached to each workflow run.

## License
Released under the [MIT License](LICENSE). Third-party components are listed in [NOTICE.md](NOTICE.md).

This project is not affiliated with Mojang Studios, Microsoft, or the authors of PCL. Minecraft is a trademark of Mojang Studios.
