using System.Numerics;
using System.Text.Json;

namespace DerethSurvivors;

public sealed class NativeSpellPhysics
{
    public sealed class Profile
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public uint Wcid { get; set; }
        public int Count { get; set; }
        public float Spread { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public int Depth { get; set; }
        public float[] Offset { get; set; } = new float[3];
        public float[] Padding { get; set; } = new float[3];
        public float[] Perturbation { get; set; } = new float[3];
        public bool NonTracking { get; set; }
        public uint Setup { get; set; }
        public uint Effects { get; set; }
        public float Speed { get; set; }
        public float Scale { get; set; }
        public bool Gravity { get; set; }
    }
    private sealed class Document { public Profile[] Profiles { get; set; } = Array.Empty<Profile>(); }
    public Dictionary<uint, Profile> Profiles { get; }
    public NativeSpellPhysics(string json)
    {
        Profiles = (JsonSerializer.Deserialize<Document>(json)?.Profiles ?? throw new InvalidDataException("Missing spell physics profiles.")).ToDictionary(p => p.Id);
        foreach (var p in Profiles.Values)
            if (p.Count < 1 || p.Count > 128 || p.Speed <= 0 || p.Scale <= 0 || p.Setup == 0 || p.Offset.Length != 3 || p.Padding.Length != 3)
                throw new InvalidDataException($"Invalid projectile profile {p.Id}.");
    }
    public record Launch(Vector3 Origin, Vector3 Direction);
    // Returns setup-relative coordinates in Godot axes: +X right, +Y up, -Z forward.
    public static Launch[] Layout(Profile p, float casterHeight, float casterRadius, float projectileRadius)
    {
        var shots = new List<Launch>();
        float forward = 2 * (casterRadius + projectileRadius) * (p.Spread >= 360 ? 0.6f : 1);
        var offset = new Vector3(p.Offset[0], casterHeight * (2f / 3) + p.Offset[2], -forward - p.Offset[1]);
        int columns = Math.Max(1, p.Columns), rows = Math.Max(1, p.Rows), depth = Math.Max(1, p.Depth);
        for (int row = 0; row < rows && shots.Count < p.Count; row++)
        for (int layer = 0; layer < depth && shots.Count < p.Count; layer++)
        for (int column = 0; column < columns && shots.Count < p.Count; column++)
        {
            if (p.Spread > 0)
            {
                // AC odd radial spreads include coincident first/last directions (e.g. 9 rays / 360°).
                int denominator = p.Count % 2 == 1 ? p.Count - 1 : p.Count;
                int step = column == 0 ? 0 : (column + 1) / 2 * (column % 2 == 1 ? 1 : -1);
                float angle = p.Spread / Math.Max(1, denominator) * (MathF.PI / 180) * step;
                var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
                shots.Add(new(Vector3.Transform(offset, rotation), Vector3.Transform(-Vector3.UnitZ, rotation)));
            }
            else
            {
                float x = (column - (Math.Min(columns, p.Count - row * depth * columns - layer * columns) - 1) / 2f) * (p.Padding[0] + 2 * projectileRadius);
                var origin = offset + new Vector3(x, row * (p.Padding[2] + 2 * projectileRadius), -layer * (p.Padding[1] + 2 * projectileRadius));
                shots.Add(new(origin, -Vector3.UnitZ));
            }
        }
        if (shots.Count != p.Count) throw new InvalidDataException("Projectile dimensions do not fit count.");
        return shots.ToArray();
    }
}
