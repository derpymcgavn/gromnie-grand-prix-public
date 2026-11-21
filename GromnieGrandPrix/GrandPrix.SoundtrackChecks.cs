using Godot;

public partial class GrandPrix
{
    private async void QuitSoundtrackCheck()
    {
        SetPhysicsProcess(false);
        raceAudio?.Clear();
        StopSoundtrack();
        // Let the audio mixer retire queued playback before the test process exits.
        var tree = GetTree();
        await ToSignal(tree.CreateTimer(.12), SceneTreeTimer.SignalName.Timeout);
        tree.Quit();
    }

    private void CheckSoundtrackSettings()
    {
        foreach (var cue in new[] { MainThemeMusic, AerlintheMusic, ArwicMusic, ClashMusic, HoltburgMusic, MosswartMusic,
                     PortalStormMusic, QalabarMusic, HopeslayerMusic, LugianMusic, TouTouMusic, VictoryMusic })
        {
            var stream = GD.Load<AudioStreamMP3>($"res://Data/Music/{cue.File}.mp3");
            Require(stream is not null && stream.GetLength() > 60, $"imported soundtrack cue {cue.Title}");
        }
        Require(CurrentMusicTitle == MainThemeMusic.Title && musicPlayer.Playing && MusicCapture is not null,
            "the main theme plays through the captured Music bus");
        StartVoidCheck();
        Require(CurrentMusicTitle == HopeslayerMusic.Title && musicPlayer.Bus == MusicBusName,
            "the course starts its soundtrack cue");

        Player.Speed = 25;
        UpdateRaceSpeedUi();
        Require(speedText.Text == "90 km/h", "the default speedometer reads metric");
        OpenSoundtrackCheckSettings();
        SelectSoundtrackCheckOption("Graphics/SpeedDisplay", 1);
        SelectSoundtrackCheckOption("Graphics/AntiAliasing", 0);
        SelectSoundtrackCheckOption("Graphics/DynamicShadows", 0);
        SelectSoundtrackCheckOption("Graphics/MusicHud", 0);
        SelectSoundtrackCheckOption("Graphics/FlightControls", 0);
        Require(UseMilesPerHour && speedText.Text == "56 mph" && GetViewport().Msaa3D == Viewport.Msaa.Disabled &&
                !weatherSun.ShadowEnabled && !ShowMusicHud && !FreeFlightControls,
            "speed, flight and graphics options update in the paused race");

        raceSettingsMenu.GetNode<Button>("AudioTab").EmitSignal(Button.SignalName.Pressed);
        Require(raceSettingsMenu.GetNode<Control>("Audio").Visible && !raceSettingsMenu.GetNode<Control>("Graphics").Visible,
            "the Settings audio tab opens");
        raceSettingsMenu.GetNode<HSlider>("Audio/MusicVolume").Value = 0;
        raceSettingsMenu.GetNode<HSlider>("Audio/EffectsVolume").Value = 46;
        Require(AudioServer.IsBusMute(AudioServer.GetBusIndex(MusicBusName)) && !AudioServer.IsBusMute(0) &&
                Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(RaceAudio.EffectsBusName)), Mathf.LinearToDb(.46f)),
            "the music mute and effects volume apply independently");

        string config = $"user://race-settings-test-{Time.GetTicksUsec()}.cfg";
        try
        {
            WriteRaceSettings(config, raceSettings);
            Require(ReadRaceSettings(config) == raceSettings, "graphics, speed units and volume survive saving settings");
        }
        finally { DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(config)); }

        Require(HandleRaceControl(Key.Escape) && paused && racePauseMenu.Visible && !raceSettingsMenu.Visible,
            "Esc from Settings returns to the pause menu");
        Require(HandleRaceControl(Key.Escape) && !paused && !pauseUi.Visible, "Esc from pause resumes the race");
        Player.Finish = 90;
        PreviewVoidFinale();
        Require(CurrentMusicTitle == ClashMusic.Title, "the finale starts Clash of the Immortals");
        TickVoidFinale(VoidFinaleDuration);
        Require(CurrentMusicTitle == VictoryMusic.Title, "the completed race plays the victory cue");
        OpenStable();
        Require(CurrentMusicTitle == MainThemeMusic.Title, "returning to the stable brings back the main theme");
        GD.Print("SOUNDTRACK AND SETTINGS PASS | 12 imported tracks, course cues, audio buses, graphics, mph and persistence.");
    }

    private void OpenSoundtrackCheckSettings()
    {
        Require(HandleRaceControl(Key.Escape) && paused && pauseUi.Visible, "Esc opens the pause menu");
        pauseSettingsButton.EmitSignal(Button.SignalName.Pressed);
        Require(raceSettingsMenu.Visible && !racePauseMenu.Visible, "the pause Settings button opens its panel");
    }

    private void SelectSoundtrackCheckOption(string path, int index)
    {
        var picker = raceSettingsMenu.GetNode<OptionButton>(path);
        picker.Select(index);
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }

    private async Task CaptureSoundtrackSettings(string folder)
    {
        StartVoidCheck();
        SetPhysicsProcess(false);
        countdown = 0;
        Player.Distance = 130;
        Player.Speed = 25;
        Player.Draw(track, 1f / 30, true);
        SetRaceCamera(1);
        UpdateRaceUi();
        OpenSoundtrackCheckSettings();
        SelectSoundtrackCheckOption("Graphics/SpeedDisplay", 1);
        await SaveFrame(Path.Combine(folder, "Graphics-Settings.png"));
        var audioTab = raceSettingsMenu.GetNode<Button>("AudioTab");
        audioTab.GrabFocus();
        audioTab.EmitSignal(Button.SignalName.Pressed);
        await SaveFrame(Path.Combine(folder, "Audio-Settings.png"));
        HandleRaceControl(Key.Escape);
        HandleRaceControl(Key.Escape);
        ulong deadline = Time.GetTicksMsec() + 7000;
        while ((!musicHud.HasAudioSignal || !musicHud.IsVisibleInTree()) && Time.GetTicksMsec() < deadline)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(musicHud.HasAudioSignal && musicHud.IsVisibleInTree() && musicHud.TrackTitle == HopeslayerMusic.Title &&
                speedText.Text == "56 mph", "the active race shows a live music waveform, title and imperial speed");
        await SaveFrame(Path.Combine(folder, "Now-Playing.png"));
        GD.Print("LIVE MUSIC HUD CAPTURE PASS");
    }
}
