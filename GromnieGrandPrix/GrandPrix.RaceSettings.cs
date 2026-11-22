using Godot;

public partial class GrandPrix
{
    private const string RaceSettingsFile = "user://race-settings.cfg";
    private const string MasterBusName = "Master";

    private sealed record RaceSettings(bool? Fullscreen = null, int Msaa = 2, bool Shadows = true,
        bool SpeedBlur = true, bool MilesPerHour = false, bool MusicHud = true, bool FreeFlight = true,
        int MasterVolume = 100, int MusicVolume = 70, int EffectsVolume = 100,
        RaceUpscaler Upscaler = RaceUpscaler.Off, RaceRenderQuality RenderQuality = RaceRenderQuality.Quality);

    private static readonly string[] UpscalerNames = ["Off", "AMD FSR 1.0", "AMD FSR 2.2", "MetalFX Spatial", "MetalFX Temporal", "Bilinear"];
    private static readonly string[] RenderQualityNames = ["Ultra Quality", "Quality", "Balanced", "Performance"];

    private RaceSettings raceSettings = new();
    private Control racePauseMenu = null!, raceSettingsMenu = null!;
    private Button resumeRace = null!, pauseSettingsButton = null!, settingsGraphicsTab = null!;
    private ButtonGroup settingsTabs = null!;
    private OptionButton upscalerPicker = null!, renderQualityPicker = null!, msaaPicker = null!;
    private Label upscalingStatus = null!;
    private Godot.Timer? raceSettingsSaveTimer;
    private bool UseMilesPerHour => raceSettings.MilesPerHour;
    private bool ShowMusicHud => raceSettings.MusicHud;
    private bool FreeFlightControls => raceSettings.FreeFlight;

    private static RaceSettings DefaultRaceSettings() => OS.HasFeature("android")
        ? new(Msaa: 0, Shadows: false, SpeedBlur: false, Upscaler: RaceUpscaler.Bilinear, RenderQuality: RaceRenderQuality.Performance)
        : new();

    private void MakeRacePauseMenu(bool mobile)
    {
        raceSettings = testMode ? DefaultRaceSettings() : ReadRaceSettings(RaceSettingsFile);

        if (AudioServer.GetBusIndex(RaceAudio.EffectsBusName) < 0)
        {
            AudioServer.AddBus();
            int bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, RaceAudio.EffectsBusName);
            AudioServer.SetBusSend(bus, MasterBusName);
        }
        ApplyRaceSettings();
        if (mobile)
            GD.Print($"Android graphics: 3D scale {GetViewport().Scaling3DScale:P0}, MSAA {GetViewport().Msaa3D}, shadows {raceSettings.Shadows}, speed blur {raceSettings.SpeedBlur}.");
        raceSettingsSaveTimer = new Godot.Timer { OneShot = true, WaitTime = .35 };
        raceSettingsSaveTimer.Timeout += () => WriteRaceSettings(RaceSettingsFile, raceSettings);
        AddChild(raceSettingsSaveTimer);

        racePauseMenu = PausePanel(pauseUi, "PauseMenu");
        MakeModal(racePauseMenu, "RACE PAUSED", 222, 356);
        resumeRace = Action(racePauseMenu, "RESUME", 452, 303, 376, 54, () => SetPaused(false), true);
        pauseSettingsButton = Action(racePauseMenu, "SETTINGS", 452, 369, 376, 49, OpenRaceSettings);
        pauseRestart = Action(racePauseMenu, "RESTART", 452, 429, 376, 49, OnlineRaceButton);
        Action(racePauseMenu, "BACK TO STABLE", 452, 489, 376, 49, StableButton);

        raceSettingsMenu = PausePanel(pauseUi, "Settings");
        MakeModal(raceSettingsMenu, "SETTINGS", 147, 556);
        var graphics = PausePanel(raceSettingsMenu, "Graphics");
        var upscaling = PausePanel(raceSettingsMenu, "Upscaling");
        var audio = PausePanel(raceSettingsMenu, "Audio");
        settingsTabs = new ButtonGroup();
        settingsGraphicsTab = MakeTab("GraphicsTab", "GRAPHICS", 420, graphics);
        MakeTab("UpscalingTab", "UPSCALING", 570, upscaling);
        MakeTab("AudioTab", "AUDIO", 720, audio);
        settingsGraphicsTab.ButtonPressed = true;
        SelectTab(graphics);

        Button MakeTab(string name, string title, int x, Control panel)
        {
            Button tab = null!;
            tab = Action(raceSettingsMenu, title, x, 208, 140, 42, () =>
            {
                tab.ButtonPressed = true;
                SelectTab(panel);
            });
            tab.Name = name;
            tab.ToggleMode = true;
            tab.ButtonGroup = settingsTabs;
            tab.AddThemeStyleboxOverride("pressed", Surface("446d50", "abd1a0", 7));
            return tab;
        }

        int row = mobile ? 289 : 267;
        if (!mobile)
        {
            Select(graphics, "DisplayMode", "Display", row, ["Windowed", "Fullscreen"], IsFullscreen ? 1 : 0,
                value => raceSettings with { Fullscreen = value == 1 });
            row += 51;
        }
        msaaPicker = Select(graphics, "AntiAliasing", "Anti-aliasing", row, ["Off", "2× MSAA", "4× MSAA", "8× MSAA"], raceSettings.Msaa,
            value => raceSettings with { Msaa = value });
        Select(graphics, "DynamicShadows", "Dynamic shadows", row + 51, ["Off", "On"], raceSettings.Shadows ? 1 : 0,
            value => raceSettings with { Shadows = value == 1 });
        Select(graphics, "SpeedBlur", "Speed blur", row + 102, ["Off", "On"], raceSettings.SpeedBlur ? 1 : 0,
            value => raceSettings with { SpeedBlur = value == 1 });
        Select(graphics, "SpeedDisplay", "Speed display", row + 153, ["km/h", "MPH"], UseMilesPerHour ? 1 : 0,
            value => raceSettings with { MilesPerHour = value == 1 });
        Select(graphics, "MusicHud", "Now playing display", row + 204, ["Off", "On"], ShowMusicHud ? 1 : 0,
            value => raceSettings with { MusicHud = value == 1 });
        Select(graphics, "FlightControls", "Flight controls", row + 255, ["Guided", "Free"], FreeFlightControls ? 1 : 0,
            value => raceSettings with { FreeFlight = value == 1 });

        upscalerPicker = Select(upscaling, "Upscaler", "Upscaler", 281, UpscalerNames, 0,
            value => raceSettings with { Upscaler = (RaceUpscaler)value });
        upscalerPicker.AllowReselect = true;
        for (int i = 0; i < UpscalerNames.Length; i++)
        {
            var mode = (RaceUpscaler)i;
            if (UpscalerAvailable(mode)) continue;
            upscalerPicker.SetItemDisabled(i, true);
            upscalerPicker.SetItemTooltip(i, mode is RaceUpscaler.Fsr1 or RaceUpscaler.Fsr2
                ? "Requires the Forward+ renderer."
                : "Requires the Metal driver and compatible Apple graphics.");
        }
        renderQualityPicker = Select(upscaling, "RenderQuality", "Quality preset", 338, RenderQualityNames, 0,
            value => raceSettings with { RenderQuality = (RaceRenderQuality)value });
        Text(upscaling, ActiveRendererLabel(), 421, 405, 437, 16, Soft);
        upscalingStatus = Text(upscaling, "", 421, 442, 437, 17, Mint);
        upscalingStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        RefreshUpscalingControls();

        Volume("MasterVolume", "Master volume", 281, raceSettings.MasterVolume, value => raceSettings with { MasterVolume = value });
        Volume("MusicVolume", "Music volume", 376, raceSettings.MusicVolume, value => raceSettings with { MusicVolume = value });
        Volume("EffectsVolume", "Effects volume", 471, raceSettings.EffectsVolume, value => raceSettings with { EffectsVolume = value });
        Action(raceSettingsMenu, "BACK", 420, 635, 440, 43, () => CloseRaceSettings(focusMenu: true)).Name = "Back";
        raceSettingsMenu.Hide();

        void SelectTab(Control active)
        {
            graphics.Visible = active == graphics;
            upscaling.Visible = active == upscaling;
            audio.Visible = active == audio;
        }

        OptionButton Select(Control parent, string name, string label, int y, string[] choices, int selected, Func<int, RaceSettings> edit)
        {
            Text(parent, label, 421, y + 9, 188, 17);
            var picker = new OptionButton { Name = name, Position = new(623, y), Size = new(237, 39), Theme = ThemeForUi() };
            foreach (string choice in choices) picker.AddItem(choice);
            picker.Select(selected);
            picker.ItemSelected += index => ChangeRaceSettings(edit((int)index));
            parent.AddChild(picker);
            return picker;
        }

        void Volume(string name, string label, int y, int selected, Func<int, RaceSettings> edit)
        {
            Text(audio, label, 421, y, 362, 17);
            var percent = Text(audio, $"{selected}%", 794, y + 1, 65, 16, Gold, HorizontalAlignment.Right);
            var slider = new HSlider
            {
                Name = name, Position = new(421, y + 29), Size = new(437, 34), MaxValue = 100, Step = 1, Value = selected,
                Theme = ThemeForUi(), TooltipText = label
            };
            slider.ValueChanged += value =>
            {
                int level = (int)value;
                percent.Text = $"{level}%";
                ChangeRaceSettings(edit(level));
            };
            audio.AddChild(slider);
        }
    }

    private static Control PausePanel(Control parent, string name)
    {
        var panel = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return panel;
    }

    private void OpenRaceSettings()
    {
        racePauseMenu.Hide();
        raceSettingsMenu.Show();
        (settingsTabs.GetPressedButton() ?? settingsGraphicsTab).GrabFocus();
    }

    private void CloseRaceSettings(bool focusMenu = false)
    {
        raceSettingsMenu.Hide();
        racePauseMenu.Show();
        FlushRaceSettings();
        if (focusMenu) pauseSettingsButton.GrabFocus();
    }

    private bool IsFullscreen => GetWindow().Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;

    private void ChangeRaceSettings(RaceSettings settings)
    {
        if (raceSettings == settings) return;
        bool changeSpeedUnits = raceSettings.MilesPerHour != settings.MilesPerHour;
        raceSettings = settings;
        ApplyRaceSettings();
        RefreshUpscalingControls();
        if (changeSpeedUnits && racing && racers.Count > localRacerIndex) UpdateRaceSpeedUi();
        if (!testMode) raceSettingsSaveTimer?.Start();
    }

    private void ApplyRaceSettings()
    {
        ApplyRaceUpscaling(GetViewport());
        weatherSun.ShadowEnabled = raceSettings.Shadows;
        if (!raceSettings.SpeedBlur) ClearSpeedBlur();
        if (raceSettings.Fullscreen is bool fullscreen && fullscreen != IsFullscreen &&
            !OS.HasFeature("android") && DisplayServer.GetName() != "headless")
            GetWindow().Mode = fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
        SetBusLevel(MasterBusName, raceSettings.MasterVolume);
        SetBusLevel(MusicBusName, raceSettings.MusicVolume);
        SetBusLevel(RaceAudio.EffectsBusName, raceSettings.EffectsVolume);
    }

    private void RefreshUpscalingControls()
    {
        var supported = UpscalerAvailable(raceSettings.Upscaler);
        var effective = supported ? raceSettings.Upscaler : RaceUpscaler.Off;
        upscalerPicker.Select((int)effective);
        renderQualityPicker.Select((int)raceSettings.RenderQuality);
        renderQualityPicker.Disabled = effective == RaceUpscaler.Off;
        msaaPicker.Disabled = effective is RaceUpscaler.Fsr2 or RaceUpscaler.MetalFxTemporal;
        msaaPicker.TooltipText = msaaPicker.Disabled ? "Temporal upscaling handles anti-aliasing." : "";
        upscalingStatus.Text = ActiveUpscalingDescription();
    }

    private static string ActiveRendererLabel()
    {
        var method = RenderingServer.GetCurrentRenderingMethod() switch
        {
            "forward_plus" => "Forward+",
            "mobile" => "Mobile",
            "gl_compatibility" => "Compatibility",
            var value => value
        };
        var driver = RenderingServer.GetCurrentRenderingDriverName() switch
        {
            "d3d12" => "Direct3D 12",
            "vulkan" => "Vulkan",
            "metal" => "Metal",
            "opengl3" => "OpenGL",
            "opengl3_es" => "OpenGL ES",
            "opengl3_angle" => "OpenGL ANGLE",
            var value => value
        };
        return driver.Length == 0 ? method : $"{method} · {driver}";
    }

    private static void SetBusLevel(string name, int level)
    {
        int bus = AudioServer.GetBusIndex(name);
        if (bus < 0) return;
        AudioServer.SetBusMute(bus, level == 0);
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Math.Max(.0001f, level / 100f)));
    }

    private void FlushRaceSettings()
    {
        if (raceSettingsSaveTimer is not { } timer || timer.IsStopped()) return;
        timer.Stop();
        WriteRaceSettings(RaceSettingsFile, raceSettings);
    }

    private static RaceSettings ReadRaceSettings(string path)
    {
        var defaults = DefaultRaceSettings();
        using var config = new ConfigFile();
        if (!Godot.FileAccess.FileExists(path) || config.Load(path) != Error.Ok) return defaults;
        var upscaler = (RaceUpscaler)config.GetValue("graphics", "upscaler", (int)defaults.Upscaler).AsInt32();
        var renderQuality = (RaceRenderQuality)config.GetValue("graphics", "render_quality", (int)defaults.RenderQuality).AsInt32();
        return new RaceSettings(
            config.HasSectionKey("graphics", "fullscreen") ? config.GetValue("graphics", "fullscreen").AsBool() : null,
            Math.Clamp(config.GetValue("graphics", "msaa", defaults.Msaa).AsInt32(), 0, 3),
            config.GetValue("graphics", "shadows", defaults.Shadows).AsBool(),
            config.GetValue("graphics", "speed_blur", defaults.SpeedBlur).AsBool(),
            config.GetValue("graphics", "mph", defaults.MilesPerHour).AsBool(),
            config.GetValue("graphics", "music_hud", defaults.MusicHud).AsBool(),
            config.GetValue("graphics", "free_flight", defaults.FreeFlight).AsBool(),
            Math.Clamp(config.GetValue("audio", "master", defaults.MasterVolume).AsInt32(), 0, 100),
            Math.Clamp(config.GetValue("audio", "music", defaults.MusicVolume).AsInt32(), 0, 100),
            Math.Clamp(config.GetValue("audio", "effects", defaults.EffectsVolume).AsInt32(), 0, 100),
            Enum.IsDefined(upscaler) ? upscaler : defaults.Upscaler,
            Enum.IsDefined(renderQuality) ? renderQuality : defaults.RenderQuality);
    }

    private static void WriteRaceSettings(string path, RaceSettings settings)
    {
        using var config = new ConfigFile();
        if (settings.Fullscreen is bool fullscreen) config.SetValue("graphics", "fullscreen", fullscreen);
        config.SetValue("graphics", "msaa", settings.Msaa);
        config.SetValue("graphics", "shadows", settings.Shadows);
        config.SetValue("graphics", "speed_blur", settings.SpeedBlur);
        config.SetValue("graphics", "mph", settings.MilesPerHour);
        config.SetValue("graphics", "music_hud", settings.MusicHud);
        config.SetValue("graphics", "free_flight", settings.FreeFlight);
        config.SetValue("graphics", "upscaler", (int)settings.Upscaler);
        config.SetValue("graphics", "render_quality", (int)settings.RenderQuality);
        config.SetValue("audio", "master", settings.MasterVolume);
        config.SetValue("audio", "music", settings.MusicVolume);
        config.SetValue("audio", "effects", settings.EffectsVolume);
        Error result = config.Save(path);
        if (result != Error.Ok) GD.PushWarning($"Could not save race settings ({result}).");
    }
}
