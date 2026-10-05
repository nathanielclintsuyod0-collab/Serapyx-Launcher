using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ProcessBuilder;

namespace SeraphyxLauncher;

public partial class MainWindow : Window
{
    private const string DefaultServerHost = "seraphyx.atbp.fun";
    private const int DefaultServerPort = 20021;
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SeraphyxLauncher");
    private readonly LauncherSettings _settings;
    private MSession? _session;
    private JELoginHandler? _loginHandler;
    private MinecraftLauncher? _launcher;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(_dataDirectory);
        _settings = LoadSettings();
        GameDirectoryText();
        LoadSettingsIntoUi();
        UpdateModeUi();
        Closing += (_, _) => SaveSettings();
    }

    private string GameDirectory => Path.Combine(_dataDirectory, "game");
    private string SettingsPath => Path.Combine(_dataDirectory, "launcher-settings.json");

    private void GameDirectoryText()
    {
        _launcher = new MinecraftLauncher(new MinecraftPath(GameDirectory));
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
        LocalProfileBox.Text = _settings.LocalProfile;
        VersionBox.Text = _settings.Version;
        ServerHostBox.Text = _settings.ServerHost;
        ServerPortBox.Text = _settings.ServerPort.ToString();

        var availableMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
        var safeCap = availableMb > 0 ? Math.Clamp(availableMb / 2, 2048, 8192) : 8192;
        MemorySlider.Maximum = safeCap;
        MemorySlider.Value = Math.Clamp(_settings.MemoryMb, (int)MemorySlider.Minimum, safeCap);
        _settings.Preset = NormalizePreset(_settings.Preset);
        UpdatePresetDescription();
    }

    private void SaveSettings()
    {
        if (VersionBox is null) return;

        _settings.LocalProfile = LocalProfileBox.Text.Trim();
        _settings.Version = VersionBox.Text.Trim();
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
                ModeDescription.Text = _session is null
                    ? "Sign in online first during this launcher session. Offline mode then opens single-player only."
                    : "Authenticated profile is available for offline single-player. Server auto-connect is disabled.";
                LaunchButton.Content = "LAUNCH OFFLINE";
                ModeBadge.Text = _session is null ? "SIGN IN FIRST" : "OFFLINE SINGLE-PLAYER";
                SignInButton.Visibility = Visibility.Visible;
                ServerHostBox.IsEnabled = false;
                ServerPortBox.IsEnabled = false;
                FooterHint.Text = "Offline mode cannot connect to Seraphyx or any multiplayer server.";
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

        AccountStatus.Text = _session is null
            ? "No Microsoft profile connected"
            : "Signed in as " + _session.Username + " — verified for this session";
        AccountStatus.Foreground = _session is null
            ? (Brush)FindResource("Muted")
            : (Brush)FindResource("Green");
        SignInButton.Content = "Sign in with Microsoft";
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

        if (_session is null)
        {
            SetStatus("Sign in with Microsoft first. The local nickname is not a Minecraft account.", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(VersionBox.Text))
        {
            SetStatus("Enter a Minecraft release version supported by Seraphyx.", isError: true);
            return;
        }

        var online = SelectedMode == "Online";
        if (online)
        {
            if (string.IsNullOrWhiteSpace(ServerHostBox.Text) || !int.TryParse(ServerPortBox.Text, out var port) || port is < 1 or > 65535)
            {
                SetStatus("Enter a valid server address and port.", isError: true);
                return;
            }
        }

        await LaunchMinecraftAsync(online);
    }

    private async Task LaunchMinecraftAsync(bool online)
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
            var options = new MLaunchOption
            {
                Session = _session,
                MaximumRamMb = (int)MemorySlider.Value
            };

            if (online)
            {
                options.ServerIp = ServerHostBox.Text.Trim();
                options.ServerPort = int.Parse(ServerPortBox.Text);
            }

            var version = VersionBox.Text.Trim();
            SetStatus(online ? $"Installing or checking Minecraft {version}…" : $"Installing or checking Minecraft {version} for offline play…");
            var process = await _launcher.InstallAndBuildProcessAsync(version, options);
            process.Start();
            LaunchProgress.Value = 100;
            SetStatus(online ? "Minecraft started and is connecting to Seraphyx." : "Minecraft started in offline single-player mode.");
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
                ["renderDistance"] = "6", ["simulationDistance"] = "5", ["graphicsMode"] = "1",
                ["particles"] = "2", ["entityDistanceScaling"] = "0.5", ["clouds"] = "0", ["mipmapLevels"] = "0"
            },
            "Quality" => new Dictionary<string, string>
            {
                ["renderDistance"] = "14", ["simulationDistance"] = "10", ["graphicsMode"] = "0",
                ["particles"] = "0", ["entityDistanceScaling"] = "1.0", ["clouds"] = "2", ["mipmapLevels"] = "4"
            },
            _ => new Dictionary<string, string>
            {
                ["renderDistance"] = "10", ["simulationDistance"] = "8", ["graphicsMode"] = "1",
                ["particles"] = "1", ["entityDistanceScaling"] = "0.75", ["clouds"] = "1", ["mipmapLevels"] = "2"
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
        SetStatus(status);
        return true;
    }

    private void EndBusy()
    {
        _isBusy = false;
        LaunchButton.IsEnabled = true;
        SignInButton.IsEnabled = true;
        UpdateModeUi();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "Danger" : "Muted");
    }

    private void ShowPage(string page, string title)
    {
        HomeView.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        OptimizerView.Visibility = page == "Optimizer" ? Visibility.Visible : Visibility.Collapsed;
        AboutView.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = title;
    }

    private void NavigateHome_Click(object sender, RoutedEventArgs e) => ShowPage("Home", "Your realm is waiting");
    private void NavigateOptimizer_Click(object sender, RoutedEventArgs e) => ShowPage("Optimizer", "Tune your game profile");
    private void NavigateAbout_Click(object sender, RoutedEventArgs e) => ShowPage("About", "Built for the Seraphyx community");
    private void BalancedPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset("Balanced", 4096);
    private void PerformancePreset_Click(object sender, RoutedEventArgs e) => ApplyPreset("Performance", 3072);
    private void QualityPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset("Quality", 6144);

    private sealed class LauncherSettings
    {
        public string LocalProfile { get; set; } = "";
        public string Version { get; set; } = "1.21.4";
        public string ServerHost { get; set; } = DefaultServerHost;
        public int ServerPort { get; set; } = DefaultServerPort;
        public int MemoryMb { get; set; } = 4096;
        public string Preset { get; set; } = "Balanced";
    }
}

