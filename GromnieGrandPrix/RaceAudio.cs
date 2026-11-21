using Godot;
using DerethSurvivors;

internal sealed partial class RaceAudio : Node3D
{
    public const string EffectsBusName = "Effects";
    private readonly DatArchive archive;
    private readonly AudioStreamPlayer playerVoice = new() { VolumeDb = -8, Bus = EffectsBusName };
    private readonly Dictionary<uint, AudioStream?> waves = [];
    private readonly Dictionary<string, AudioStream?> clips = [];
    private readonly Dictionary<uint, Dictionary<uint, AcAudioData.Cue[]>> tables = [];
    private readonly List<(AudioStreamPlayer3D Player, Node3D Anchor)> nearbyVoices = [];
    public int Played { get; private set; }

    public RaceAudio(DatArchive archive)
    {
        this.archive = archive;
        Name = "Race audio";
        AddChild(playerVoice);
    }

    public AudioStream? Native(uint id)
    {
        if (waves.TryGetValue(id, out var stream)) return stream;
        try
        {
            if (!archive.Entries.ContainsKey(id)) return waves[id] = null;
            var sound = AcAudioData.ReadWave(archive.Read(id));
            return waves[id] = sound.Encoding == 85 ? new AudioStreamMP3 { Data = sound.Data } : new AudioStreamWav
            {
                Data = sound.Data, MixRate = sound.Rate, Stereo = sound.Channels == 2,
                Format = sound.Bits == 16 ? AudioStreamWav.FormatEnum.Format16Bits : AudioStreamWav.FormatEnum.Format8Bits
            };
        }
        catch (Exception error) when (error is IOException or NotSupportedException or InvalidDataException)
        {
            GD.PushWarning($"Could not load DAT sound {id:X8}. {error.Message}");
            return waves[id] = null;
        }
    }

    public AudioStream? Dialogue(string name) => Clip($"Asheron-{name}");

    public AudioStream? Clip(string name)
    {
        if (clips.TryGetValue(name, out var stream)) return stream;
        string path = $"res://Data/Audio/{name}.wav";
        return clips[name] = Godot.FileAccess.FileExists(path)
            ? AudioStreamWav.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(path))
            : ResourceLoader.Exists(path) ? GD.Load<AudioStreamWav>(path) : null;
    }

    public void Play(AudioStream? stream, Node3D anchor, bool foreground, float volumeDb, float pitch = 1)
    {
        if (stream is null || !anchor.IsInsideTree()) return;
        if (foreground)
        {
            playerVoice.Stream = stream;
            playerVoice.VolumeDb = volumeDb;
            playerVoice.PitchScale = pitch;
            playerVoice.Play();
        }
        else
        {
            if (nearbyVoices.Count >= 10) return;
            var source = new AudioStreamPlayer3D
            {
                Stream = stream, Bus = EffectsBusName, VolumeDb = volumeDb, PitchScale = pitch, UnitSize = 12, MaxDistance = 56,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
            };
            AddChild(source);
            source.GlobalPosition = anchor.GlobalPosition;
            nearbyVoices.Add((source, anchor));
            source.Play();
        }
        Played++;
    }

    public bool PlayEmote(AcFormats.Hook hook, AcActor actor)
    {
        uint id = hook.Asset;
        float volume = hook.SoundVolume;
        if (hook.Type == 2)
        {
            if (!archive.Entries.ContainsKey(actor.SoundTable)) return false;
            if (!tables.TryGetValue(actor.SoundTable, out var table))
            {
                try { tables[actor.SoundTable] = table = AcAudioData.ReadTable(archive.Read(actor.SoundTable)); }
                catch (Exception error) when (error is IOException or InvalidDataException)
                {
                    tables[actor.SoundTable] = [];
                    return false;
                }
            }
            if (!table.TryGetValue(id, out var samples) || samples.Length == 0) return false;
            var sample = samples[Random.Shared.Next(samples.Length)];
            if (Random.Shared.NextSingle() > sample.Probability) return false;
            id = sample.Wave;
            volume *= sample.Volume;
        }
        else if (hook.Type != 1 && (hook.Type != 21 || Random.Shared.NextSingle() > hook.SoundProbability)) return false;
        if (id >> 24 != 0x0a || !float.IsFinite(volume) || volume <= 0) return false;
        int before = Played;
        Play(Native(id), actor, false, -14 + Mathf.LinearToDb(Math.Min(volume, 1)));
        return Played != before;
    }

    public void Tick()
    {
        for (int i = nearbyVoices.Count - 1; i >= 0; i--)
        {
            var (player, anchor) = nearbyVoices[i];
            if (player.Playing && IsInstanceValid(anchor) && anchor.IsInsideTree())
                player.GlobalPosition = anchor.GlobalPosition;
            else
            {
                player.QueueFree();
                nearbyVoices.RemoveAt(i);
            }
        }
    }

    public void Pause(bool value)
    {
        playerVoice.StreamPaused = value;
        foreach (var (voice, _) in nearbyVoices) voice.StreamPaused = value;
    }

    public void Clear()
    {
        playerVoice.Stop();
        playerVoice.StreamPaused = false;
        foreach (var (voice, _) in nearbyVoices) { voice.Stop(); voice.QueueFree(); }
        nearbyVoices.Clear();
    }
}
