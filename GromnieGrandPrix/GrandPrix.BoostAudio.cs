using Godot;

public partial class GrandPrix
{
    // Calls from the retail creature sound tables, ordered by RaceMountKind.
    private static readonly uint[][] BoostCalls =
    [
        [0x0a000115, 0x0a00010f], [0x0a0000a6], [0x0a00014a, 0x0a000148],
        [0x0a000502, 0x0a000503], [0x0a00037a, 0x0a000379], [0x0a00001a],
        [0x0a0003fc, 0x0a0003fe], [0x0a000585, 0x0a000589], [0x0a00039c, 0x0a00039f],
        [0x0a000365, 0x0a000364], [0x0a00015e, 0x0a000160], [0x0a000187, 0x0a000185],
        [0x0a000027], [0x0a000262], [0x0a0004a7, 0x0a0004a3], [0x0a00049b, 0x0a000491]
    ];
    private static readonly string[] AsheronLines = ["consulted", "toll", "boots"];
    private RaceAudio? raceAudio;

    private AudioStream? BoostSound(RaceMountKind kind, int take)
    {
        var variants = BoostCalls[(int)kind];
        if (kind == RaceMountKind.Asheron && raceAudio?.Dialogue(AsheronLines[take % AsheronLines.Length]) is { } line)
            return line;
        return raceAudio?.Native(variants[take % variants.Length]);
    }

    private void GrantBoost(MountedRacer racer, float duration)
    {
        racer.Boost = Math.Max(racer.Boost, duration);
        if (elapsed < racer.NextBoostSound || raceAudio is null) return;
        bool player = racer == Player;
        if (!player && racer.CoursePosition(track).DistanceSquaredTo(Player.CoursePosition(track)) > 38 * 38) return;

        var kind = racer.Rig.Kind;
        var stream = BoostSound(kind, racer.BoostVoiceTake++);
        racer.NextBoostSound = elapsed + Math.Max(1.0f, (float)(stream?.GetLength() ?? 0));
        raceAudio.Play(stream, racer.Root, player, player ? kind == RaceMountKind.Rabbit ? -5 : -8 : -17,
            kind == RaceMountKind.Rabbit ? .88f : 1);
    }
}
