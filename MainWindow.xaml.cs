using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.ProcessBuilder;

namespace SeraphyxLauncher;

public partial class MainWindow : Window
{
    private const string DefaultServerHost = "seraphyx.atbp.fun";
    private const int DefaultServerPort = 20021;
    private static readonly HttpClient ModrinthClient = CreateModrinthClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> AllowedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
        { "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com" };
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SeraphyxLauncher");
    private readonly LauncherSettings _settings;
    private readonly ObservableCollection<string> _minecraftVersions = new();
    private MSession? _session;
    private JELoginHandler? _loginHandler;
    private MinecraftLauncher? _launcher;
    private bool _isBusy;
    private bool _loadingSettings;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_dataDirectory);
        _settings = LoadSettings();
        InitializeLauncher();
        LoadSettingsIntoUi();
        UpdateModeUi();
        Loaded += async (_, _) =>
        {
            await LoadMinecraftVersionsAsync();
            await TrySilentSignInAsync();
        };
        Closing += (_, _) => SaveSettings();
    }

    private static HttpClient CreateModrinthClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SeraphyxLauncher/1.1 (+https://github.com/nathanielclintsuyod0-collab/Serapyx-Launcher)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private string GameDirectory => Path.Combine(_dataDirectory, "game");
    private string SettingsPath => Path.Combine(_dataDirectory, "launcher-settings.json");

    private void InitializeLauncher()
    {
        _launcher = new MinecraftLauncher(new MinecraftPath(GameDirectory));
        _launcher.FileProgressChanged += (_, e) => Dispatcher.Invoke(() =>
        {
            LaunchProgress.IsIndeterminate = false;
            LaunchProgress.Value = e.TotalTasks == 0 ? 0 : e.ProgressedTasks * 100.0 / e.TotalTasks;
            SetStatus($"Downloading {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})");
        });
    }

    private LauncherSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath)) ?? new LauncherSettings();
            }
        }
        catch
        {
            // Start from safe defaults if the settings file is damaged.
        }

        return new LauncherSettings();
    }

    private void LoadSettingsIntoUi()
    {
        _loadingSettings = true;
        LocalProfileBox.Text = _settings.LocalProfile;
        _minecraftVersions.Clear();
        _minecraftVersions.Add(_settings.Version);
        VersionBox.ItemsSource = _minecraftVersions;
        VersionBox.SelectedItem = _settings.Version;
        SelectGameLoader(_settings.Loader);
        ServerHostBox.Text = _settings.ServerHost;
        ServerPortBox.Text = _settings.ServerPort.ToString();

        var availableMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
        var safeCap = availableMb > 0 ? Math.Clamp(availableMb / 2, 2048, 8192) : 8192;
        MemorySlider.Maximum = safeCap;
        MemorySlider.Value = Math.Clamp(_settings.MemoryMb, (int)MemorySlider.Minimum, safeCap);
        _settings.Preset = NormalizePreset(_settings.Preset);
        UpdatePresetDescription();
        _loadingSettings = false;
    }

    private void SaveSettings()
    {
        if (VersionBox is null) return;

        _settings.LocalProfile = LocalProfileBox.Text.Trim();
        _settings.Version = SelectedMinecraftVersion;
        _settings.Loader = SelectedGameLoader;
        _settings.ServerHost = ServerHostBox.Text.Trim();
        _settings.ServerPort = int.TryParse(ServerPortBox.Text, out var port) ? port : DefaultServerPort;
        _settings.MemoryMb = (int)MemorySlider.Value;
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            SetStatus("Could not save launcher settings: " + ex.Message, isError: true);
        }
    }

    private string SelectedMode => (ModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Preview";
    private string SelectedMinecraftVersion => VersionBox.SelectedItem?.ToString() ?? _settings.Version;
    private string SelectedGameLoader => (GameLoaderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Vanilla";

    private void SelectGameLoader(string loader)
    {
        var item = GameLoaderBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(option => string.Equals(option.Tag?.ToString(), loader, StringComparison.OrdinalIgnoreCase));
        GameLoaderBox.SelectedItem = item ?? GameLoaderBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private async Task LoadMinecraftVersionsAsync()
    {
        try
        {
            _launcher ??= new MinecraftLauncher(new MinecraftPath(GameDirectory));
            var releases = (await _launcher.GetAllVersionsAsync())
                .Where(version => string.Equals(version.Type.ToString(), "Release", StringComparison.OrdinalIgnoreCase))
                .Select(version => version.Name)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (releases.Count == 0) throw new InvalidOperationException("No release versions were returned.");
            if (!releases.Contains(_settings.Version, StringComparer.OrdinalIgnoreCase)) releases.Insert(0, _settings.Version);

            _minecraftVersions.Clear();
            foreach (var release in releases) _minecraftVersions.Add(release);
            VersionBox.SelectedItem = _settings.Version;
            SetStatus($"Loaded {releases.Count} Minecraft release versions.");
        }
        catch (Exception ex)
        {
            var fallback = new[] { _settings.Version, "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.19.4", "1.18.2", "1.16.5", "1.12.2", "1.8.9" }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _minecraftVersions.Clear();
            foreach (var release in fallback) _minecraftVersions.Add(release);
            VersionBox.SelectedItem = _settings.Version;
            SetStatus("Could not refresh Minecraft versions. Showing common releases.");
            ModrinthResultCount.Text = "Minecraft version list is using a saved offline fallback.";
        }
    }

    private void VersionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSettings && IsLoaded) SaveSettings();
    }

    private void GameLoaderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSettings && IsLoaded) SaveSettings();
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateModeUi();
        SaveSettings();
    }

    private void UpdateModeUi()
    {
        switch (SelectedMode)
        {
            case "Online":
                ModeDescription.Text = "Sign in with Microsoft and launch directly into Seraphyx. A valid Java profile is required.";
                LaunchButton.Content = "LAUNCH SERAPHYX";
                ModeBadge.Text = _session is null ? "SIGN-IN REQUIRED" : "MICROSOFT READY";
                SignInButton.Visibility = Visibility.Visible;
                ServerHostBox.IsEnabled = true;
                ServerPortBox.IsEnabled = true;
                FooterHint.Text = "Online mode requires a Microsoft account with Minecraft: Java Edition access.";
                break;
            case "Offline":
                ModeDescription.Text = "Use your local launcher nickname for single-player. Microsoft sign-in is not needed.";
                LaunchButton.Content = "LAUNCH OFFLINE";
                ModeBadge.Text = string.IsNullOrWhiteSpace(LocalProfileBox.Text) ? "SET NICKNAME" : "LOCAL SINGLE-PLAYER";
                SignInButton.Visibility = Visibility.Collapsed;
                ServerHostBox.IsEnabled = false;
                ServerPortBox.IsEnabled = false;
                FooterHint.Text = "No Microsoft sign-in. Local single-player only; this mode cannot join servers.";
                break;
            default:
                ModeDescription.Text = "Preview the launcher and optimizer without starting Minecraft or connecting to a server.";
                LaunchButton.Content = "RUN PREVIEW";
                ModeBadge.Text = "PREVIEW READY";
                SignInButton.Visibility = Visibility.Collapsed;
                ServerHostBox.IsEnabled = false;
                ServerPortBox.IsEnabled = false;
                FooterHint.Text = "Preview works without an account; multiplayer requires Microsoft sign-in.";
                break;
        }

        if (SelectedMode == "Offline")
        {
            var localName = LocalProfileBox.Text.Trim();
            AccountStatus.Text = localName.Length == 0
                ? "Enter a local nickname to launch single-player"
                : "Local single-player profile: " + localName;
            AccountStatus.Foreground = (Brush)FindResource(localName.Length == 0 ? "Muted" : "Lavender");
        }
        else
        {
            AccountStatus.Text = _session is null
                ? "No Microsoft profile connected"
                : "Signed in as " + _session.Username + " — verified for this session";
            AccountStatus.Foreground = _session is null
                ? (Brush)FindResource("Muted")
                : (Brush)FindResource("Green");
        }
        SignInButton.Content = "Sign in with Microsoft";
    }

    private async Task TrySilentSignInAsync()
    {
        try
        {
            _loginHandler ??= JELoginHandlerBuilder.BuildDefault();
            var session = await _loginHandler.AuthenticateSilently();
            if (session is not null && !string.IsNullOrWhiteSpace(session.Username))
            {
                _session = session;
                UpdateModeUi();
            }
        }
        catch
        {
            // No cached account yet; the player signs in manually.
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginBusy("Opening Microsoft sign-in…")) return;
        try
        {
            _loginHandler ??= JELoginHandlerBuilder.BuildDefault();
            var session = await _loginHandler.Authenticate();
            if (session is null || string.IsNullOrWhiteSpace(session.Username))
            {
                throw new InvalidOperationException("Microsoft did not return a Minecraft Java profile. Confirm the account has Java Edition access.");
            }

            _session = session;
            UpdateModeUi();
            SetStatus("Microsoft profile verified for this launcher session.");
        }
        catch (Exception ex)
        {
            SetStatus("Sign-in did not complete: " + ex.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void SaveLocalProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = LocalProfileBox.Text.Trim();
        if (name.Length > 0 && !Regex.IsMatch(name, "^[A-Za-z0-9_]{1,16}$"))
        {
            SetStatus("Use 1–16 letters, numbers, or underscores for the local launcher nickname.", isError: true);
            return;
        }

        _settings.LocalProfile = name;
        SaveSettings();
        UpdateModeUi();
        SetStatus(name.Length == 0 ? "Local launcher nickname cleared." : $"Saved local launcher nickname: {name}.");
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        SaveSettings();

        if (SelectedMode == "Preview")
        {
            await RunPreviewAsync();
            return;
        }

        var online = SelectedMode == "Online";
        var offlineUsername = LocalProfileBox.Text.Trim();

        if (online && _session is null)
        {
            SetStatus("Sign in with Microsoft first. Online play requires a Java Edition profile.", isError: true);
            return;
        }

        if (!online && SelectedMode == "Offline" && !Regex.IsMatch(offlineUsername, "^[A-Za-z0-9_]{1,16}$"))
        {
            SetStatus("Enter a local nickname of 1–16 letters, numbers, or underscores for single-player.", isError: true);
            return;
        }

        var gameVersion = SelectedMinecraftVersion;
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            SetStatus("Choose a Minecraft release version.", isError: true);
            return;
        }

        if (online)
        {
            if (string.IsNullOrWhiteSpace(ServerHostBox.Text) || !int.TryParse(ServerPortBox.Text, out var port) || port is < 1 or > 65535)
            {
                SetStatus("Enter a valid server address and port.", isError: true);
                return;
            }
        }

        await LaunchMinecraftAsync(online, offlineUsername, gameVersion, SelectedGameLoader);
    }

    private async Task LaunchMinecraftAsync(bool online, string offlineUsername, string gameVersion, string loader)
    {
        if (!BeginBusy(online ? "Preparing Seraphyx…" : "Preparing offline single-player…")) return;
        LaunchProgress.Visibility = Visibility.Visible;
        LaunchProgress.Value = 0;
        LaunchProgress.IsIndeterminate = true;

        try
        {
            Directory.CreateDirectory(GameDirectory);
            ApplyGameOptions(backupExisting: true);
            _launcher ??= new MinecraftLauncher(new MinecraftPath(GameDirectory));
            var launchSession = online ? _session! : MSession.CreateOfflineSession(offlineUsername);
            var options = new MLaunchOption
            {
                Session = launchSession,
                MaximumRamMb = (int)MemorySlider.Value
            };

            if (online)
            {
                options.ServerIp = ServerHostBox.Text.Trim();
                options.ServerPort = int.Parse(ServerPortBox.Text);
            }

            SetStatus(online ? $"Preparing Minecraft {gameVersion}…" : $"Preparing Minecraft {gameVersion} for offline play…");
            var launchVersion = await InstallGameLoaderAsync(gameVersion, loader);
            var process = await _launcher.InstallAndBuildProcessAsync(launchVersion, options);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                if (process.ExitCode != 0)
                    SetStatus($"Minecraft exited with code {process.ExitCode}. Check the logs folder in the game directory.", isError: true);
            });
            process.Start();
            LaunchProgress.Value = 100;
            SetStatus(online
                ? $"Minecraft {gameVersion} ({loader}) started and is connecting to Seraphyx."
                : $"Minecraft {gameVersion} ({loader}) started in offline single-player mode.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not launch Minecraft: " + ex.Message, isError: true);
        }
        finally
        {
            LaunchProgress.IsIndeterminate = false;
            LaunchProgress.Visibility = Visibility.Collapsed;
            EndBusy();
        }
    }

    private async Task<string> InstallGameLoaderAsync(string gameVersion, string loader, string? loaderVersion = null)
    {
        var gamePath = new MinecraftPath(GameDirectory);
        return loader switch
        {
            "Fabric" when string.IsNullOrWhiteSpace(loaderVersion) =>
                await new FabricInstaller(ModrinthClient).Install(gameVersion, gamePath),
            "Fabric" => await new FabricInstaller(ModrinthClient).Install(gameVersion, loaderVersion!, gamePath),
            "Quilt" when string.IsNullOrWhiteSpace(loaderVersion) =>
                await new QuiltInstaller(ModrinthClient).Install(gameVersion, gamePath),
            "Quilt" => await new QuiltInstaller(ModrinthClient).Install(gameVersion, loaderVersion!, gamePath),
            _ => gameVersion
        };
    }

    private async void SearchModrinth_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var query = ModrinthSearchBox.Text.Trim();
        var contentType = (ModrinthTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mod";
        var gameVersion = SelectedMinecraftVersion;

        if (!BeginBusy("Searching Modrinth…")) return;
        ModrinthSearchButton.IsEnabled = false;
        try
        {
            var facetList = new List<string[]>
            {
                new[] { $"project_type:{contentType}" },
                new[] { $"versions:{gameVersion}" }
            };
            if (contentType is "mod" or "modpack")
                facetList.Add(new[] { "categories:fabric", "categories:quilt" });
            var facets = JsonSerializer.Serialize(facetList);
            var url = "https://api.modrinth.com/v2/search?query=" + Uri.EscapeDataString(query)
                + "&facets=" + Uri.EscapeDataString(facets)
                + "&index=downloads&limit=30";

            var response = await GetJsonAsync<ModrinthSearchResponse>(url);
            ModrinthResults.ItemsSource = response.Hits;
            ModrinthResultCount.Text = response.Hits.Count == 0
                ? $"No {ContentTypeLabel(contentType).ToLowerInvariant()} found for Minecraft {gameVersion}."
                : $"Showing {response.Hits.Count} {ContentTypeLabel(contentType).ToLowerInvariant()} for Minecraft {gameVersion}.";
            SetStatus("Modrinth search is ready.");
        }
        catch (Exception ex)
        {
            ModrinthResults.ItemsSource = null;
            ModrinthResultCount.Text = "Could not connect to Modrinth.";
            SetStatus("Modrinth search failed: " + ex.Message, isError: true);
        }
        finally
        {
            ModrinthSearchButton.IsEnabled = true;
            EndBusy();
        }
    }

    private void ModrinthSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SearchModrinth_Click(sender, e);
    }

    private async void InstallModrinthProject_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || (sender as Button)?.Tag is not ModrinthProject project) return;
        if (!BeginBusy($"Preparing {project.Title}…")) return;

        try
        {
            switch (project.Type)
            {
                case "mod":
                    await InstallModrinthModAsync(project);
                    break;
                case "modpack":
                    await InstallModrinthPackAsync(project);
                    break;
                case "resourcepack":
                    await InstallSimpleModrinthProjectAsync(project, "resourcepacks");
                    break;
                case "shader":
                    await InstallSimpleModrinthProjectAsync(project, "shaderpacks");
                    break;
                default:
                    throw new InvalidOperationException("This Modrinth content type is not supported.");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Install failed: " + ex.Message, isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task InstallModrinthModAsync(ModrinthProject project)
    {
        var gameVersion = SelectedMinecraftVersion;
        var selectedLoader = SelectedGameLoader.ToLowerInvariant();
        var loaderPreference = selectedLoader is "fabric" or "quilt" ? selectedLoader : null;
        var modVersion = await FindCompatibleModVersionAsync(project.Id, gameVersion, loaderPreference);
        if (modVersion is null)
        {
            throw new InvalidOperationException($"No Fabric or Quilt release of {project.Title} supports Minecraft {gameVersion}.");
        }

        var loader = modVersion.Loaders.Contains("fabric", StringComparer.OrdinalIgnoreCase) &&
                     (string.Equals(loaderPreference, "fabric", StringComparison.OrdinalIgnoreCase) ||
                      !modVersion.Loaders.Contains("quilt", StringComparer.OrdinalIgnoreCase))
            ? "Fabric"
            : "Quilt";
        SelectGameLoader(loader);
        SaveSettings();
        await InstallGameLoaderAsync(gameVersion, loader);

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedCount = await InstallModVersionWithDependenciesAsync(
            modVersion, gameVersion, loader.ToLowerInvariant(), installed);
        SetStatus($"Installed {installedCount} compatible file(s) for {project.Title}. Launch with the {loader} profile.");
    }

    private async Task<int> InstallModVersionWithDependenciesAsync(
        ModrinthVersion version, string gameVersion, string loader, HashSet<string> installedVersions)
    {
        var identity = string.IsNullOrWhiteSpace(version.Id)
            ? version.ProjectId + ":" + version.VersionNumber
            : version.Id;
        if (!installedVersions.Add(identity)) return 0;

        var installedCount = 0;
        foreach (var dependency in version.Dependencies.Where(dependency =>
                     string.Equals(dependency.Type, "required", StringComparison.OrdinalIgnoreCase)))
        {
            ModrinthVersion? dependencyVersion = null;
            if (!string.IsNullOrWhiteSpace(dependency.VersionId))
            {
                dependencyVersion = await GetJsonAsync<ModrinthVersion>(
                    "https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(dependency.VersionId));
            }
            else if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
            {
                dependencyVersion = await FindCompatibleModVersionAsync(dependency.ProjectId, gameVersion, loader);
            }

            if (dependencyVersion is null)
                throw new InvalidOperationException("A required mod dependency has no compatible Fabric or Quilt version.");
            if (!dependencyVersion.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase) ||
                !dependencyVersion.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("A required mod dependency is not compatible with the selected Minecraft profile.");

            installedCount += await InstallModVersionWithDependenciesAsync(
                dependencyVersion, gameVersion, loader, installedVersions);
        }

        var file = SelectPrimaryFile(version);
        if (file is null || !file.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A required mod version did not include a downloadable JAR file.");
        }

        var safeName = Path.GetFileName(file.FileName);
        var destination = Path.Combine(GameDirectory, "mods", safeName);
        await DownloadAndVerifyFileAsync(file.Url, destination, file.Hashes, file.Size);
        return installedCount + 1;
    }

    private async Task InstallSimpleModrinthProjectAsync(ModrinthProject project, string folderName)
    {
        var versions = await GetProjectVersionsAsync(project.Id, SelectedMinecraftVersion);
        var version = versions.FirstOrDefault(IsReleaseVersion) ?? versions.FirstOrDefault();
        var file = version is null ? null : SelectPrimaryFile(version);
        if (file is null) throw new InvalidOperationException("No downloadable version was found for the selected Minecraft version.");

        var destination = Path.Combine(GameDirectory, folderName, Path.GetFileName(file.FileName));
        await DownloadAndVerifyFileAsync(file.Url, destination, file.Hashes, file.Size);
        SetStatus($"Installed {project.Title}. Select it in Minecraft's {folderName} menu.");
    }

    private async Task InstallModrinthPackAsync(ModrinthProject project)
    {
        var versions = await GetProjectVersionsAsync(project.Id, SelectedMinecraftVersion);
        var version = versions.FirstOrDefault(IsReleaseVersion) ?? versions.FirstOrDefault();
        var file = version is null ? null : SelectPrimaryFile(version);
        if (file is null || !file.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("No compatible Modrinth modpack file was found.");
        }

        var downloadDirectory = Path.Combine(_dataDirectory, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var packagePath = Path.Combine(downloadDirectory, Guid.NewGuid().ToString("N") + ".mrpack");
        try
        {
            await DownloadAndVerifyFileAsync(file.Url, packagePath, file.Hashes, file.Size);
            using var archive = ZipFile.OpenRead(packagePath);
            var indexEntry = archive.GetEntry("modrinth.index.json")
                ?? throw new InvalidDataException("The downloaded modpack has no modrinth.index.json file.");
            using var indexStream = indexEntry.Open();
            using var document = await JsonDocument.ParseAsync(indexStream);
            var root = document.RootElement;

            if (!root.TryGetProperty("formatVersion", out var formatVersion) || formatVersion.GetInt32() != 1 ||
                !root.TryGetProperty("game", out var game) || game.GetString() != "minecraft")
            {
                throw new InvalidDataException("This Modrinth pack format is not supported.");
            }

            var dependencies = root.GetProperty("dependencies");
            var gameVersion = dependencies.GetProperty("minecraft").GetString()
                ?? throw new InvalidDataException("The modpack does not specify a Minecraft version.");
            string loader;
            string? loaderVersion;
            if (dependencies.TryGetProperty("fabric-loader", out var fabric))
            {
                loader = "Fabric";
                loaderVersion = fabric.GetString();
            }
            else if (dependencies.TryGetProperty("quilt-loader", out var quilt))
            {
                loader = "Quilt";
                loaderVersion = quilt.GetString();
            }
            else if (dependencies.TryGetProperty("forge", out _) || dependencies.TryGetProperty("neoforge", out _))
            {
                throw new InvalidOperationException("This pack uses Forge or NeoForge. This launcher currently installs Fabric and Quilt packs.");
            }
            else
            {
                loader = "Vanilla";
                loaderVersion = null;
            }

            SetMinecraftVersion(gameVersion);
            SelectGameLoader(loader);
            SaveSettings();
            SetStatus($"Preparing {project.Title} with Minecraft {gameVersion} and {loader}…");
            await InstallGameLoaderAsync(gameVersion, loader, loaderVersion);

            var installedFiles = 0;
            if (root.TryGetProperty("files", out var files))
            {
                foreach (var packFile in files.EnumerateArray())
                {
                    if (packFile.TryGetProperty("env", out var env) &&
                        env.TryGetProperty("client", out var clientEnvironment) &&
                        (string.Equals(clientEnvironment.GetString(), "unsupported", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clientEnvironment.GetString(), "optional", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var relativePath = packFile.GetProperty("path").GetString()
                        ?? throw new InvalidDataException("A modpack file has no destination path.");
                    if (!packFile.TryGetProperty("downloads", out var downloadList) || downloadList.GetArrayLength() == 0)
                    {
                        throw new InvalidDataException($"The modpack file {relativePath} has no download URL.");
                    }

                    var downloadUrl = downloadList[0].GetString()
                        ?? throw new InvalidDataException($"The modpack file {relativePath} has an empty download URL.");
                    var hashes = ReadHashMap(packFile.GetProperty("hashes"));
                    var size = packFile.TryGetProperty("fileSize", out var fileSize) ? fileSize.GetInt64() : 0;
                    await DownloadAndVerifyFileAsync(downloadUrl, ResolveGamePath(relativePath), hashes, size);
                    installedFiles++;
                }
            }

            installedFiles += await CopyPackOverridesAsync(archive, "overrides/");
            installedFiles += await CopyPackOverridesAsync(archive, "client-overrides/");
            SetStatus($"Installed {project.Title}: {installedFiles} required client files with {loader}. Optional files were skipped.");
        }
        finally
        {
            if (File.Exists(packagePath)) File.Delete(packagePath);
        }
    }

    private async Task<int> CopyPackOverridesAsync(ZipArchive archive, string directoryPrefix)
    {
        var count = 0;
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var relativePath = entry.FullName[directoryPrefix.Length..];
            if (string.IsNullOrWhiteSpace(relativePath)) continue;
            var destination = ResolveGamePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(output);
            count++;
        }
        return count;
    }

    private string ResolveGamePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("A modpack contains an absolute destination path.");
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(GameDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, normalized));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A modpack contains a file path outside its game folder.");
        }
        return destination;
    }

    private async Task<ModrinthVersion?> FindCompatibleModVersionAsync(string projectId, string gameVersion, string? preferredLoader)
    {
        var versions = await GetProjectVersionsAsync(projectId, gameVersion);
        var candidates = versions.Where(version => version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase)
                && version.Loaders.Any(loader => loader.Equals("fabric", StringComparison.OrdinalIgnoreCase)
                    || loader.Equals("quilt", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(IsReleaseVersion)
            .ToList();

        if (preferredLoader is not null)
        {
            var preferred = candidates.FirstOrDefault(version => version.Loaders.Contains(preferredLoader, StringComparer.OrdinalIgnoreCase));
            if (preferred is not null) return preferred;
        }
        return candidates.FirstOrDefault();
    }

    private static async Task<List<ModrinthVersion>> GetProjectVersionsAsync(
        string projectId, string? gameVersion = null, IEnumerable<string>? loaders = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            query.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion })));
        }
        if (loaders is not null)
        {
            query.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(loaders.ToArray())));
        }

        var url = "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(projectId) + "/version";
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return await GetJsonAsync<List<ModrinthVersion>>(url);
    }

    private static async Task<T> GetJsonAsync<T>(string url)
    {
        using var response = await ModrinthClient.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Modrinth returned {(int)response.StatusCode}: {body}");
        }
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidDataException("Modrinth returned an empty response.");
    }

    private static bool IsReleaseVersion(ModrinthVersion version) =>
        string.Equals(version.VersionType, "release", StringComparison.OrdinalIgnoreCase);

    private static ModrinthFile? SelectPrimaryFile(ModrinthVersion version) =>
        version.Files.FirstOrDefault(file => file.Primary) ?? version.Files.FirstOrDefault();

    private static Dictionary<string, string> ReadHashMap(JsonElement element)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                hashes[property.Name] = property.Value.GetString() ?? "";
        }
        return hashes;
    }

    private static async Task DownloadAndVerifyFileAsync(
        string url, string destination, IReadOnlyDictionary<string, string> hashes, long expectedSize = 0)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Modrinth provided a download link that is not a secure HTTPS address.");
        }

        if (!AllowedDownloadHosts.Contains(uri.Host))
        {
            throw new InvalidDataException($"Downloads from {uri.Host} are not allowed.");
        }
        if (!hashes.ContainsKey("sha1") && !hashes.ContainsKey("sha512"))
            throw new InvalidDataException("The downloaded content has no supported integrity hash.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await ModrinthClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("The download redirected to an insecure address.");
            if (response.Content.Headers.ContentLength is > 1_073_741_824)
                throw new InvalidDataException("The requested file is larger than the launcher's 1 GB safety limit.");

            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    downloaded += read;
                    if (downloaded > 1_073_741_824)
                        throw new InvalidDataException("The requested file is larger than the launcher's 1 GB safety limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
            }

            var actualSize = new FileInfo(temporaryPath).Length;
            if (expectedSize > 0 && actualSize != expectedSize)
                throw new InvalidDataException("The downloaded file size did not match Modrinth's metadata.");

            foreach (var (algorithm, expectedHash) in hashes)
            {
                await using var fileStream = File.OpenRead(temporaryPath);
                byte[] actualHash = algorithm.ToLowerInvariant() switch
                {
                    "sha1" => await SHA1.HashDataAsync(fileStream),
                    "sha512" => await SHA512.HashDataAsync(fileStream),
                    _ => Array.Empty<byte>()
                };
                if (actualHash.Length > 0 && !Convert.ToHexString(actualHash).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded file failed Modrinth's integrity check.");
            }

            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task RunPreviewAsync()
    {
        if (!BeginBusy("Running launcher preview…")) return;
        LaunchProgress.Visibility = Visibility.Visible;
        LaunchProgress.Value = 0;
        string[] steps = ["Loading Seraphyx profile…", "Checking optimizer settings…", "Preview complete. Minecraft was not started."];
        try
        {
            for (var i = 0; i < steps.Length; i++)
            {
                SetStatus(steps[i]);
                await Task.Delay(450);
                LaunchProgress.Value = (i + 1) * 100.0 / steps.Length;
            }
            SetStatus("Preview complete — no account or game launch was used.");
        }
        finally
        {
            LaunchProgress.Visibility = Visibility.Collapsed;
            EndBusy();
        }
    }

    private void ApplyGameOptions(bool backupExisting)
    {
        Directory.CreateDirectory(GameDirectory);
        var path = Path.Combine(GameDirectory, "options.txt");
        if (backupExisting && File.Exists(path))
        {
            var backup = path + ".seraphyx-backup";
            if (!File.Exists(backup)) File.Copy(path, backup);
        }

        var values = _settings.Preset switch
        {
            "Performance" => new Dictionary<string, string>
            {
                ["renderDistance"] = "6", ["simulationDistance"] = "5", ["graphicsMode"] = "0",
                ["particles"] = "2", ["entityDistanceScaling"] = "0.5", ["renderClouds"] = "\"false\"", ["mipmapLevels"] = "0"
            },
            "Quality" => new Dictionary<string, string>
            {
                ["renderDistance"] = "14", ["simulationDistance"] = "10", ["graphicsMode"] = "1",
                ["particles"] = "0", ["entityDistanceScaling"] = "1.0", ["renderClouds"] = "\"true\"", ["mipmapLevels"] = "4"
            },
            _ => new Dictionary<string, string>
            {
                ["renderDistance"] = "10", ["simulationDistance"] = "8", ["graphicsMode"] = "1",
                ["particles"] = "1", ["entityDistanceScaling"] = "0.75", ["renderClouds"] = "\"fast\"", ["mipmapLevels"] = "2"
            }
        };

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        foreach (var pair in values)
        {
            var index = lines.FindIndex(line => line.StartsWith(pair.Key + ":", StringComparison.Ordinal));
            var updated = pair.Key + ":" + pair.Value;
            if (index >= 0) lines[index] = updated;
            else lines.Add(updated);
        }

        File.WriteAllLines(path, lines);
    }

    private void ApplyPreset(string preset, int memoryMb)
    {
        _settings.Preset = preset;
        MemorySlider.Value = Math.Clamp(memoryMb, (int)MemorySlider.Minimum, (int)MemorySlider.Maximum);
        UpdatePresetDescription();
        SaveSettings();
    }

    private static string NormalizePreset(string? value) => value is "Performance" or "Quality" ? value : "Balanced";

    private void UpdatePresetDescription()
    {
        PresetDescription.Text = _settings.Preset switch
        {
            "Performance" => "Higher FPS: shorter view distance, minimal particles, and lower memory allocation.",
            "Quality" => "Visual quality: longer view distance and higher detail; may reduce FPS on modest PCs.",
            _ => "Balanced: moderate view distance and memory for typical play."
        };
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MemoryLabel is not null) MemoryLabel.Text = $"{(int)MemorySlider.Value} MB";
        if (IsLoaded && _settings is not null) _settings.MemoryMb = (int)MemorySlider.Value;
    }

    private void ApplyOptimization_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyGameOptions(backupExisting: true);
            SaveSettings();
            SetStatus($"Applied {_settings.Preset.ToLowerInvariant()} settings to the isolated Seraphyx game folder.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not apply optimizer settings: " + ex.Message, isError: true);
        }
    }

    private bool BeginBusy(string status)
    {
        if (_isBusy) return false;
        _isBusy = true;
        LaunchButton.IsEnabled = false;
        SignInButton.IsEnabled = false;
        ModrinthSearchButton.IsEnabled = false;
        ModrinthProgress.Visibility = Visibility.Visible;
        SetStatus(status);
        return true;
    }

    private void EndBusy()
    {
        _isBusy = false;
        LaunchButton.IsEnabled = true;
        SignInButton.IsEnabled = true;
        ModrinthSearchButton.IsEnabled = true;
        ModrinthProgress.Visibility = Visibility.Collapsed;
        UpdateModeUi();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "Danger" : "Muted");
        if (ModrinthStatusText is not null)
        {
            ModrinthStatusText.Text = message;
            ModrinthStatusText.Foreground = (Brush)FindResource(isError ? "Danger" : "Muted");
        }
    }

    private void ShowPage(string page, string title)
    {
        HomeView.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        ModrinthView.Visibility = page == "Modrinth" ? Visibility.Visible : Visibility.Collapsed;
        OptimizerView.Visibility = page == "Optimizer" ? Visibility.Visible : Visibility.Collapsed;
        AboutView.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = title;
        PageEyebrow.Text = page switch
        {
            "Modrinth" => "MODRINTH / COMMUNITY CONTENT",
            "Optimizer" => "SERAPHYX / PERFORMANCE",
            "About" => "SERAPHYX / ABOUT",
            _ => "SERAPHYX / JAVA EDITION"
        };
    }

    private void NavigateHome_Click(object sender, RoutedEventArgs e) => ShowPage("Home", "Your realm is waiting");
    private void NavigateModrinth_Click(object sender, RoutedEventArgs e) => ShowPage("Modrinth", "Discover game content");
    private void NavigateOptimizer_Click(object sender, RoutedEventArgs e) => ShowPage("Optimizer", "Tune your game profile");
    private void NavigateAbout_Click(object sender, RoutedEventArgs e) => ShowPage("About", "Built for the Seraphyx community");

    private void SetMinecraftVersion(string version)
    {
        if (!_minecraftVersions.Contains(version, StringComparer.OrdinalIgnoreCase)) _minecraftVersions.Insert(0, version);
        VersionBox.SelectedItem = _minecraftVersions.First(item => item.Equals(version, StringComparison.OrdinalIgnoreCase));
    }

    private static string ContentTypeLabel(string type) => type switch
    {
        "modpack" => "Modpacks",
        "resourcepack" => "Resource packs",
        "shader" => "Shaders",
        _ => "Mods"
    };

    private void ModrinthTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModrinthResultCount is not null && IsLoaded)
        {
            ModrinthResults.ItemsSource = null;
            ModrinthResultCount.Text = "Search Modrinth to explore community content.";
        }
    }

    private void OpenModrinthProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ModrinthProject project) return;
        var slug = string.IsNullOrWhiteSpace(project.Slug) ? project.Id : project.Slug;
        Process.Start(new ProcessStartInfo($"https://modrinth.com/{project.Type}/{Uri.EscapeDataString(slug)}")
        {
            UseShellExecute = true
        });
    }

    private void OpenGameFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(GameDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{GameDirectory}\"") { UseShellExecute = true });
    }

    private sealed class ModrinthSearchResponse
    {
        [JsonPropertyName("hits")]
        public List<ModrinthProject> Hits { get; set; } = new();
    }

    private sealed class ModrinthProject
    {
        [JsonPropertyName("project_id")] public string Id { get; set; } = "";
        [JsonPropertyName("slug")] public string? Slug { get; set; }
        [JsonPropertyName("project_type")] public string Type { get; set; } = "mod";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("author")] public string Author { get; set; } = "";
        [JsonPropertyName("downloads")] public long Downloads { get; set; }
        [JsonPropertyName("versions")] public List<string> Versions { get; set; } = new();
        [JsonIgnore] public string TypeLabel => ContentTypeLabel(Type);
        [JsonIgnore] public string Byline => "by " + Author;
        [JsonIgnore] public string DownloadsLabel => Downloads.ToString("N0") + " downloads";
        [JsonIgnore] public string VersionLabel => Versions.Count == 0 ? "Version compatibility listed on Modrinth" : "Supports " + string.Join(", ", Versions.Take(7));
    }

    private sealed class ModrinthVersion
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
        [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
        [JsonPropertyName("version_type")] public string VersionType { get; set; } = "";
        [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
        [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = new();
        [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = new();
        [JsonPropertyName("dependencies")] public List<ModrinthDependency> Dependencies { get; set; } = new();
    }

    private sealed class ModrinthFile
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("filename")] public string FileName { get; set; } = "";
        [JsonPropertyName("primary")] public bool Primary { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("hashes")] public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ModrinthDependency
    {
        [JsonPropertyName("version_id")] public string? VersionId { get; set; }
        [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
        [JsonPropertyName("dependency_type")] public string Type { get; set; } = "";
    }
    private void BalancedPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset("Balanced", 4096);
    private void PerformancePreset_Click(object sender, RoutedEventArgs e) => ApplyPreset("Performance", 3072);
    private void QualityPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset("Quality", 6144);

    private sealed class LauncherSettings
    {
        public string LocalProfile { get; set; } = "";
        public string Version { get; set; } = "1.21.4";
        public string Loader { get; set; } = "Vanilla";
        public string ServerHost { get; set; } = DefaultServerHost;
        public int ServerPort { get; set; } = DefaultServerPort;
        public int MemoryMb { get; set; } = 4096;
        public string Preset { get; set; } = "Balanced";
    }
}

