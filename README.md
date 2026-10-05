# Seraphyx Launcher

An original Windows launcher project for Seraphyx SMP. It uses a separate game folder under `%APPDATA%\SeraphyxLauncher\game` so optimizer settings do not overwrite the player's ordinary `.minecraft` profile.

## Modes

- **Online:** Sign in through Microsoft, then launch the selected Java version and connect directly to the configured Seraphyx address.
- **Offline:** Uses the local launcher nickname as an offline profile to launch single-player without Microsoft sign-in. This mode cannot connect to servers. The first launch may still need internet access to download Minecraft files.
- **Preview:** Requires no account; simulates the launcher flow without starting Minecraft.

The local profile field is used only by offline single-player. It does not create a Microsoft/Minecraft account or grant server access. Microsoft passwords are entered in the authentication flow, never in this launcher.

## Performance optimizer

The optimizer provides Balanced, Higher FPS, and Visual quality presets. It adjusts the game's memory allocation and common video settings in the isolated Seraphyx game directory. It creates `options.txt.seraphyx-backup` before changing an existing `options.txt`; it does not alter Windows registry settings, terminate other apps, or claim to improve hardware.

## Build on Windows

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then double-click `Start-Preview.cmd` or open this folder in a terminal and run:

```powershell
dotnet restore
dotnet run
```

## Download a built launcher

GitHub Actions builds a self-contained Windows x64 executable whenever code is pushed to `main`. Open the repository's **Actions** tab, select **Build Windows Launcher**, open the latest successful run, and download the `SeraphyxLauncher-windows-x64` artifact. Extract the downloaded ZIP and run `SeraphyxLauncher.exe`.

You can also start a build manually from **Actions** by selecting **Build Windows Launcher** and choosing **Run workflow**.

The project uses CmlLib.Core and CmlLib.Core.Auth.Microsoft for Minecraft installation/launch and Microsoft authentication. Both packages are MIT licensed. Preserve their license notices if you redistribute the launcher.

The server defaults (`seraphyx.atbp.fun:20021`) and Minecraft version (`1.21.4`) are editable in the launcher.

This is an independent community project and is not affiliated with Mojang Studios or Microsoft.

