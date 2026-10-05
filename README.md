# Seraphyx Launcher

An original Windows launcher project for Seraphyx SMP. It uses a separate game folder under `%APPDATA%\SeraphyxLauncher\game` so optimizer settings do not overwrite the player's ordinary `.minecraft` profile.

## Modes

- **Online:** Sign in through Microsoft, then launch the selected Java version and connect to the configured Seraphyx address. A valid Java Edition profile is required.
- **Offline:** Uses the Microsoft-authenticated profile already obtained in the current launcher session and launches single-player only.
- **Preview:** Simulates the launcher flow without starting Minecraft.

The local profile field is only a saved launcher nickname. It does not create a Minecraft identity or grant server access. Microsoft passwords are entered in the authentication flow, never in this launcher.

## Performance optimizer

Balanced, Higher FPS, and Visual quality presets adjust memory allocation and common video settings in the isolated Seraphyx game directory. Existing options are backed up before changes. The optimizer does not alter Windows registry settings or terminate other apps.

## Download a built launcher

GitHub Actions builds a self-contained Windows x64 executable whenever code is pushed to `main`. Open the repository's **Actions** tab, select **Build Windows Launcher**, open the latest successful run, and download the `SeraphyxLauncher-windows-x64` artifact. Extract the ZIP and run `SeraphyxLauncher.exe`.

You can also start a build manually from **Actions** by selecting **Build Windows Launcher** and choosing **Run workflow**.

## Build locally

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then run `dotnet restore` and `dotnet run` in this folder.

This project uses CmlLib.Core and CmlLib.Core.Auth.Microsoft for Minecraft installation/launch and Microsoft authentication. Both packages are MIT licensed. Preserve their license notices if redistributing the launcher.

The default server (`seraphyx.atbp.fun:20021`) and Minecraft version (`1.21.4`) can be changed in the launcher.

This is an independent community project and is not affiliated with Mojang Studios or Microsoft.
