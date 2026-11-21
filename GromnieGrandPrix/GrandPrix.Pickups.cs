using Godot;

public partial class GrandPrix
{
    private RaceItem[] pickupItems = [];
    private const float PickupCycleSeconds = 1.18f;
    private const float PickupAnimationRangeSquared = 138 * 138;
    private readonly List<RacePickup> pickups = [];
    private readonly TorusMesh pickupHalo = new() { InnerRadius = .84f, OuterRadius = .88f, Rings = 28, RingSegments = 5 };
    private readonly StandardMaterial3D[] pickupColors = [
        PickupColor("e9a85c"), PickupColor("d5a281"), PickupColor("b7dd99"), PickupColor("9ce4f4"),
        PickupColor("cfdf7c"), PickupColor("c7abf4"), PickupColor("f5bbd2"), PickupColor("8cebe9"), PickupColor("a9dd84"),
        .. RacePower.All.Select(power => PickupColor(power.Tint))
    ];

    private sealed class RacePickup(Node3D node, Sprite3D icon, MeshInstance3D halo, float distance, float lane, int index, Vector3 anchor)
    {
        public readonly Node3D Node = node;
        public readonly Sprite3D Icon = icon;
        public readonly MeshInstance3D Halo = halo;
        public readonly float Distance = distance, Lane = lane;
        public readonly int Index = index;
        public readonly Vector3 Anchor = anchor;
        public bool Available { get; private set; } = true;
        public float ReadyAt;
        public RaceItem Displayed, Painted;

        public void SetAvailable(bool value)
        {
            if (Available == value) return;
            Available = value;
            Node.Visible = value;
        }
    }

    private static StandardMaterial3D PickupColor(string hex)
    {
        var color = new Color(hex);
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(color, .63f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
    }

    private void BuildPickupRows()
    {
        pickupItems =
        [
            RaceItem.Pepper, RaceItem.FrostVolley, RaceItem.MudPie, RaceItem.PyrealSling, RaceItem.Burp,
            RaceItem.PyrealCache, RaceItem.Frostbolt, RaceItem.SmithHammer, RaceItem.TuskerPus, RaceItem.LeapingDraught,
            RaceItem.PortalWard, RaceItem.ZefirHex, RaceItem.WhiteRabbit, RaceItem.TuskerMight, RaceItem.Gromnatross,
            RaceItem.Invulnerability, RaceItem.ForceBurst, RaceItem.WispThief,
            RaceItem.UlgrimFolly,
            .. creation.OlthoiSpitter is null ? [] : new[] { RaceItem.OlthoiLift }
        ];
        pickups.Clear();
        int rows = track.IsRegional ? Mathf.CeilToInt(track.Length / 225) : 3;
        for (int row = 0; row < rows; row++)
        {
            float at = BogCircuit.Grid + (track.IsXL ? 105 : track.IsMedium ? 86 : 24) + row * track.Length / rows;
            if (track.IsSpan(at) || track.IsSprint && at >= track.Gate(track.GateCount) - 25) continue;
            for (int column = -1; column <= 1; column++)
            {
                float lane = column * track.WidthAt(at) * .57f;
                Vector3 anchor = track.Surface(at, lane);
                var root = new Node3D { Position = anchor };
                courseItems.AddChild(root);
                var halo = new MeshInstance3D
                {
                    Mesh = pickupHalo, Position = Vector3.Up * .075f,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = 105, VisibilityRangeEndMargin = 12
                };
                root.AddChild(halo);
                var icon = new Sprite3D
                {
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = 105, VisibilityRangeEndMargin = 12
                };
                root.AddChild(icon);
                pickups.Add(new RacePickup(root, icon, halo, at, lane, pickups.Count, anchor));
            }
        }
        UpdatePickupVisuals(0, refreshAll: true);
    }

    private void UpdatePickupVisuals(float time, bool refreshAll = false)
    {
        bool checkRange = track.IsVoid && !refreshAll;
        Vector3 eye = checkRange ? camera.Position : Vector3.Zero;
        Vector3? rabbitEye = checkRange && rabbitCameraPanel.Visible ? rabbitCamera.Position : null;
        foreach (var pickup in pickups)
        {
            pickup.SetAvailable(time >= pickup.ReadyAt);
            if (!pickup.Available) continue;

            float phase = time / PickupCycleSeconds + pickup.Index * 1.37f;
            var item = pickupItems[Mathf.FloorToInt(phase) % pickupItems.Length];
            pickup.Displayed = item;
            // Item selection follows race time even when the camera is too far away to see its icon.
            if (checkRange && pickup.Anchor.DistanceSquaredTo(eye) > PickupAnimationRangeSquared &&
                (rabbitEye is not { } replay || pickup.Anchor.DistanceSquaredTo(replay) > PickupAnimationRangeSquared))
                continue;
            if (pickup.Painted != item)
            {
                pickup.Painted = item;
                var texture = ItemIcon(item);
                pickup.Icon.Texture = texture;
                pickup.Icon.PixelSize = 1.42f / (texture?.GetWidth() ?? 1);
                pickup.Halo.MaterialOverride = pickupColors[(int)item - 1];
            }

            float beat = phase - Mathf.Floor(phase);
            float bob = Mathf.Sin(time * 3.1f + pickup.Index * .91f);
            // A brief dip at the icon change keeps the swap legible at speed.
            float size = .90f + .10f * Mathf.Sin(beat * Mathf.Pi);
            pickup.Icon.Position = Vector3.Up * (1.18f + bob * .11f);
            pickup.Icon.Scale = Vector3.One * size;
        }
    }
}
