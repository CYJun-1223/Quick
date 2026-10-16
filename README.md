# Quick

Quick is a small Windows launcher for quick calculations, currency conversion, and local Codex usage checks. It starts in the system tray and opens with a global shortcut.

## Features

- Calculator with parentheses, percentages, powers, variables, and multiline expressions.
- Currency conversion using live rates from [ExchangeRate-API](https://www.exchangerate-api.com), with MYR, USD, and SGD fallback rates when offline.
- Codex usage panel that reads rate limits through the local Codex CLI.
- Configurable global shortcut and optional launch-at-startup setting.
- Optional automatic update checks for Velopack-installed builds.
- Self-contained Windows x64 release executable; no .NET runtime installation required.

## Portable use

Quick is currently a self-contained portable executable. Copy `Quick.exe` to any folder and run it; no installer or .NET runtime is required. If you enable launch at startup, keep the executable in a permanent folder because Windows stores its full path.

Windows may show a SmartScreen warning for an unsigned first-party build. Review the source and release contents before allowing it.

## Windows user-folder layout

Quick does not require a special folder for portable use. Put `Quick.exe` in a permanent folder you control; if launch-at-startup is enabled, Windows stores that executable's full path. Moving the file later requires disabling and re-enabling the startup option.

| Data | Portable build | Velopack-installed build |
| --- | --- | --- |
| Application files | The folder containing `Quick.exe` | `%LOCALAPPDATA%\Quick` by default, managed by Velopack (`current\` and `Update.exe`) |
| Quick settings | `HKCU\Software\Quick` | `HKCU\Software\Quick` |
| Startup registration | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Quick` | The same key; uninstall removes Quick's entry |
| Codex credentials | Existing `%USERPROFILE%\.codex\auth.json` or `CODEX_HOME` | The same user-owned location; Quick never copies credentials |

Quick currently creates no user documents, logs, or cache files. Do not place settings inside the executable folder or Velopack's `current\` directory because updates replace application files there.

## Use

The default shortcut is `Alt + Space`.

1. Type a calculation, such as `5603 * 2.7%`.
2. Type a conversion, such as `100 USD to MYR`.
3. Press `Enter` on a calculation to copy the result and replace the input with it.
4. Press `Up` or `Down` to step through earlier and later inputs.
5. Press `Tab` to move to the tool list, then `Up` or `Down` to switch between Calculator, Exchange rate, and Codex usage. Typing, or `Esc`, returns to the input.
6. Press `Shift + Enter` to expand the editor for a multiline expression.
7. Press `Esc` to close the launcher.

Open the tray icon menu and choose **Settings…** to change the shortcut, enable launch at Windows startup, or enable automatic updates for installed builds.

### Codex usage

The Codex panel requires the Codex CLI to be installed and signed in on the same Windows account. Quick starts `codex app-server --stdio`, reads the local account rate limits, and refreshes them every 60 seconds while the panel is open.

If the CLI is missing or not authenticated, the panel reports that state instead of showing usage data.

## Build from source

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build --configuration Release
dotnet run --configuration Release -- --self-test
dotnet test tests/Quick.Tests/Quick.Tests.csproj --configuration Release
dotnet publish Quick.csproj --configuration Release -p:PublishProfile=Portable
```

The portable output is written to `release/Quick-portable/Quick.exe`.

### Installer build

The installer build uses Velopack and keeps its publish output separate from the portable executable:

```powershell
.\packaging\Build-Setup.ps1 -Version 0.1.0
```

The local installer is written to `release/Quick-installer/Quick-win-Setup.exe`. Installer and update packages are build outputs; they are not committed to the repository. Public distribution should add code signing before release.

Add `-SmokeTest` to install the generated package into a temporary directory, run the packaged self-check, and verify Velopack uninstall before sharing it.

The installed build checks the public GitHub Releases feed at startup and every 24 hours when **Check for updates automatically** is enabled in Settings. If a newer release is found, Quick asks before downloading it and restarting. Portable builds do not offer this option because their executable can be moved after download. A public GitHub release must exist before updates can be found.

Pushing a tag such as `v0.1.0-dev.1` (development) or `v0.1.0` (stable) runs `.github/workflows/release.yml`, verifies the application, packages the installer, creates a portable zip, and publishes all assets to that GitHub Release. Versions containing `-` are marked as GitHub prereleases. The repository may remain private during development, but the in-app updater intentionally has no embedded token and cannot read private releases.

The application uses Windows Forms and Windows global-hotkey APIs, so it is not cross-platform.

## Project layout

```text
Quick/
├─ Program.cs                         process entry point
├─ GlobalUsings.cs                    shared imports
├─ Domain/
│  └─ Calculation.cs                  calculator and currency logic
├─ Infrastructure/
│  ├─ CodexUsage.cs                   Codex CLI integration
│  ├─ QuickSettingsStore.cs           Windows Registry adapter
│  └─ QuickUpdateService.cs           Velopack update adapter
├─ Presentation/
│  ├─ LauncherForm.cs                 main launcher UI
│  ├─ LauncherForm.Updates.cs         update UI orchestration
│  ├─ CodexUsageView.cs               usage panel
│  ├─ EditorScrollbar.cs              editor control
│  └─ ShortcutSettingsForm.cs         settings window
└─ Diagnostics/
   └─ SelfTest.cs                     executable self-check
```

- `Quick.csproj` — Windows Forms project definition.
- `Assets/` — application icon and embedded logo.
- `Properties/PublishProfiles/` — repeatable portable and installer publish settings.
- `packaging/Build-Setup.ps1` — local Velopack installer packaging script.
- `tests/Quick.Tests/` — isolated MSTest coverage for calculation and shortcut behavior.
- `.github/workflows/` — CI verification and tag-based release automation.

## License

No license has been selected yet. Until one is added, the repository is not licensed for redistribution or modification.
