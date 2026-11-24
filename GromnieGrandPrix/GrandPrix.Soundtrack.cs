using Godot;

public partial class GrandPrix
{
    private const string MusicBusName = "Music";
    private const string SoundtrackArtist = "Derpy";

    private readonly record struct SoundtrackCue(string File, string Title);

    private static readonly SoundtrackCue MainThemeMusic = new("main-theme", "Gromnie Grand Prix!");
    private static readonly SoundtrackCue AerlintheMusic = new("aerlinthe-ignition", "Aerlinthe Ignition");
    private static readonly SoundtrackCue ArwicMusic = new("arwic-afterburn", "Arwic Afterburn");
    private static readonly SoundtrackCue ClashMusic = new("clash-of-the-immortals", "Clash of the Immortals");
    private static readonly SoundtrackCue HoltburgMusic = new("holtburg-hustle", "Holtburg Hustle");
    private static readonly SoundtrackCue MosswartMusic = new("mosswart-mayhem", "Mosswart Mayhem");
    private static readonly SoundtrackCue PortalStormMusic = new("portal-storm-speedway", "Portal Storm Speedway");
    private static readonly SoundtrackCue QalabarMusic = new("qalabar-quickstep", "Qalaba'r Quickstep");
    private static readonly SoundtrackCue HopeslayerMusic = new("the-hopeslayer-highway", "The Hopeslayer Highway");
    private static readonly SoundtrackCue LugianMusic = new("the-lugian-launch", "The Lugian Launch");
    private static readonly SoundtrackCue TouTouMusic = new("tou-tou-turbo", "Tou-Tou Turbo");
    private static readonly SoundtrackCue VictoryMusic = new("victory-for-the-gromnies", "Victory for the Gromnies!");

    private AudioStreamPlayer musicPlayer = null!;
    private SoundtrackCue? currentMusic;
    private AudioEffectCapture? MusicCapture { get; set; }
    private string CurrentMusicTitle => currentMusic?.Title ?? "";

    private void InitializeSoundtrack()
    {
        int bus = AudioServer.GetBusIndex(MusicBusName);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, MusicBusName);
            AudioServer.SetBusSend(bus, "Master");
        }

        for (int slot = 0; slot < AudioServer.GetBusEffectCount(bus); slot++)
        {
            if (AudioServer.GetBusEffect(bus, slot) is not AudioEffectCapture capture) continue;
            MusicCapture = capture;
            break;
        }
        if (MusicCapture is null)
        {
            MusicCapture = new AudioEffectCapture { BufferLength = .12f };
            AudioServer.AddBusEffect(bus, MusicCapture);
        }

        musicPlayer = new AudioStreamPlayer
        {
            Name = "Derpy soundtrack", Bus = MusicBusName, ProcessMode = ProcessModeEnum.Always
        };
        AddChild(musicPlayer);
    }

    private void PlayMusic(SoundtrackCue cue, bool restart = false)
    {
        if (currentMusic == cue && musicPlayer.Playing)
        {
            if (restart)
            {
                MusicCapture?.ClearBuffer();
                musicPlayer.Play();
            }
            return;
        }

        string path = $"res://Data/Music/{cue.File}.mp3";
        AudioStreamMP3? stream = ResourceLoader.Exists(path) ? GD.Load<AudioStreamMP3>(path) : null;
        if (stream is null && Godot.FileAccess.FileExists(path))
            stream = AudioStreamMP3.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(path));
        musicPlayer.Stop();
        MusicCapture?.ClearBuffer();
        musicPlayer.Stream = stream;
        currentMusic = null;
        if (stream is null)
        {
            GD.PushWarning($"Soundtrack cue could not be loaded: {cue.Title}.");
            return;
        }

        stream.Loop = true;
        currentMusic = cue;
        musicPlayer.Play();
    }

    private void StopSoundtrack()
    {
        if (!GodotObject.IsInstanceValid(musicPlayer)) return;
        musicPlayer.Stop();
        musicPlayer.Stream = null;
        MusicCapture?.ClearBuffer();
        currentMusic = null;
    }

    private SoundtrackCue RaceMusic()
    {
        if (track.IsJester) return PortalStormMusic;
        if (track.IsKaleidoscope) return ClashMusic;
        if (track.IsVoid) return HopeslayerMusic;
        if (track.IsCustom) return PortalStormMusic;
        if (track.IsSprint) return HoltburgMusic;

        string courseName = track.Name;
        bool Includes(string label) => courseName.Contains(label, StringComparison.OrdinalIgnoreCase);
        if (Includes("Aerlinthe")) return AerlintheMusic;
        if (Includes("Arwic")) return ArwicMusic;
        if (Includes("Holtburg")) return HoltburgMusic;
        if (Includes("Qalabar") || Includes("Qalaba'r")) return QalabarMusic;
        if (Includes("Tou-Tou")) return TouTouMusic;
        if (Includes("Muckwater") || Includes("Mosswart")) return MosswartMusic;
        if (Includes("Aerbax")) return ClashMusic;
        if (Includes("Obsidian") || Includes("Caul") || Includes("Linvak") || Includes("Lugian") || Includes("Crater Lake")) return LugianMusic;
        return PortalStormMusic;
    }

    private void PlayRaceResultsMusic() => PlayMusic(Player.Finish >= 0 ? VictoryMusic : MainThemeMusic);
}
