using Godot;

public partial class GrandPrix
{
    private const string RaceSettingsFile = "user://race-settings.cfg";
    private const string MasterBusName = "Master";

    private sealed record RaceSettings(bool? Fullscreen = null, int Msaa = 2, bool Shadows = true,
        bool SpeedBlur = true, bool MilesPerHour = false, bool MusicHud = true, bool FreeFlight = true,
        int MasterVolume = 100, int MusicVolume = 70, int EffectsVolume = 100);

    private RaceSettings raceSettings = new();
    private Control racePauseMenu = null!, raceSettingsMenu = null!;
    private Button resumeRace = null!, pauseSettingsButton = null!, settingsGraphicsTab = null!, settingsAudioTab = null!;
    private Godot.Timer? raceSettingsSaveTimer;
    private bool UseMilesPerHour => raceSettings.MilesPerHour;
    private bool ShowMusicHud => raceSettings.MusicHud;
    private bool FreeFlightControls => raceSettings.FreeFlight;

    private void MakeRacePauseMenu(bool mobile)
    {
        if (!testMode) raceSettings = ReadRaceSettings(RaceSettingsFile);

        if (AudioServer.GetBusIndex(RaceAudio.EffectsBusName) < 0)
        {
            AudioServer.AddBus();
            int bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, RaceAudio.EffectsBusName);
            AudioServer.SetBusSend(bus, MasterBusName);
        }
        ApplyRaceSettings();
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
        var audio = PausePanel(raceSettingsMenu, "Audio");
        var tabs = new ButtonGroup();
        settingsGraphicsTab = Action(raceSettingsMenu, "GRAPHICS", 420, 208, 211, 42, () => SelectTab(false));
        settingsAudioTab = Action(raceSettingsMenu, "AUDIO", 649, 208, 211, 42, () => SelectTab(true));
        settingsGraphicsTab.Name = "GraphicsTab";
        settingsAudioTab.Name = "AudioTab";
        foreach (var tab in new[] { settingsGraphicsTab, settingsAudioTab })
        {
            tab.ToggleMode = true;
            tab.ButtonGroup = tabs;
            tab.AddThemeStyleboxOverride("pressed", Surface("446d50", "abd1a0", 7));
        }
        SelectTab(false);

        int row = mobile ? 289 : 267;
        if (!mobile)
        {
            Select(graphics, "DisplayMode", "Display", row, ["Windowed", "Fullscreen"], IsFullscreen ? 1 : 0,
                value => raceSettings with { Fullscreen = value == 1 });
            row += 51;
        }
        Select(graphics, "AntiAliasing", "Anti-aliasing", row, ["Off", "2× MSAA", "4× MSAA", "8× MSAA"], raceSettings.Msaa,
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

        Volume("MasterVolume", "Master volume", 281, raceSettings.MasterVolume, value => raceSettings with { MasterVolume = value });
        Volume("MusicVolume", "Music volume", 376, raceSettings.MusicVolume, value => raceSettings with { MusicVolume = value });
        Volume("EffectsVolume", "Effects volume", 471, raceSettings.EffectsVolume, value => raceSettings with { EffectsVolume = value });
        Action(raceSettingsMenu, "BACK", 420, 635, 440, 43, () => CloseRaceSettings(focusMenu: true)).Name = "Back";
        raceSettingsMenu.Hide();

        void SelectTab(bool showAudio)
        {
            (showAudio ? settingsAudioTab : settingsGraphicsTab).ButtonPressed = true;
            graphics.Visible = !showAudio;
            audio.Visible = showAudio;
        }

        void Select(Control parent, string name, string label, int y, string[] choices, int selected, Func<int, RaceSettings> edit)
        {
            Text(parent, label, 421, y + 9, 188, 17);
            var picker = new OptionButton { Name = name, Position = new(623, y), Size = new(237, 39), Theme = ThemeForUi() };
            foreach (string choice in choices) picker.AddItem(choice);
            picker.Select(selected);
            picker.ItemSelected += index => ChangeRaceSettings(edit((int)index));
            parent.AddChild(picker);
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
        (settingsAudioTab.ButtonPressed ? settingsAudioTab : settingsGraphicsTab).GrabFocus();
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
        if (changeSpeedUnits && racing && racers.Count > localRacerIndex) UpdateRaceSpeedUi();
        if (!testMode) raceSettingsSaveTimer?.Start();
    }

    private void ApplyRaceSettings()
    {
        var viewport = GetViewport();
        if (viewport.Msaa3D != (Viewport.Msaa)raceSettings.Msaa) viewport.Msaa3D = (Viewport.Msaa)raceSettings.Msaa;
        weatherSun.ShadowEnabled = raceSettings.Shadows;
        if (!raceSettings.SpeedBlur) ClearSpeedBlur();
        if (raceSettings.Fullscreen is bool fullscreen && fullscreen != IsFullscreen &&
            !OS.HasFeature("android") && DisplayServer.GetName() != "headless")
            GetWindow().Mode = fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
        SetBusLevel(MasterBusName, raceSettings.MasterVolume);
        SetBusLevel(MusicBusName, raceSettings.MusicVolume);
        SetBusLevel(RaceAudio.EffectsBusName, raceSettings.EffectsVolume);
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
        var defaults = new RaceSettings();
        using var config = new ConfigFile();
        if (!Godot.FileAccess.FileExists(path) || config.Load(path) != Error.Ok) return defaults;
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
            Math.Clamp(config.GetValue("audio", "effects", defaults.EffectsVolume).AsInt32(), 0, 100));
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
        config.SetValue("audio", "master", settings.MasterVolume);
        config.SetValue("audio", "music", settings.MusicVolume);
        config.SetValue("audio", "effects", settings.EffectsVolume);
        Error result = config.Save(path);
        if (result != Error.Ok) GD.PushWarning($"Could not save race settings ({result}).");
    }
}
