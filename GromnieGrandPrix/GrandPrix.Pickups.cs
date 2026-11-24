using Godot;

public partial class GrandPrix
{
    private RaceItem[] pickupItems = [];
    private RaceItem[] footPickupItems = [];
    private RaceItem[] waterPickupItems = [];
    private StandardMaterial3D[]? regattaPickupColors;
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

    private static bool IsMountedPickup(RaceItem item) =>
        item is RaceItem.Pepper or RaceItem.Gromnatross or RaceItem.OlthoiLift or RaceItem.TuskerMight;

    private static bool IslandWaterPickup(RaceItem item) =>
        item is not (RaceItem.Gromnatross or RaceItem.OlthoiLift or RaceItem.TuskerMight or RaceItem.ZefirHex);

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
        if (track.IsWaterCourse) pickupItems = pickupItems.Where(IslandWaterPickup).ToArray();
        waterPickupItems = track.HasWaterSections ? pickupItems.Where(IslandWaterPickup).ToArray() : [];
        regattaPickupColors = null;
        if (track.HasWaterSections && RegattaPlateTexture() is { } plate)
            regattaPickupColors = pickupColors.Select(material =>
            {
                var textured = (StandardMaterial3D)material.Duplicate();
                textured.AlbedoTexture = plate;
                return textured;
            }).ToArray();
        footPickupItems = track.IsDismountSprint ? pickupItems.Where(item => !IsMountedPickup(item)).ToArray() : [];
        pickups.Clear();
        int rows = track.IsRegional ? Mathf.CeilToInt(track.Length / 225) : 3;
        for (int row = 0; row < rows; row++)
        {
            float at = BogCircuit.Grid + (track.IsXL ? 105 : track.IsMedium ? 86 : 24) + row * track.Length / rows;
            if (track.TravelMode(at) == TrackTravelMode.Flight) continue;
            if (track.IsSpan(at) || track.IsPointToPoint && at >= track.Gate(track.GateCount) - 25 ||
                track.IsDismountSprint && Math.Abs(at - track.DismountLine) < 24) continue;
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
        bool checkRange = !refreshAll && (track.IsVoid || YanshiPerformanceProfile);
        Vector3 eye = checkRange ? camera.Position : Vector3.Zero;
        Vector3? rabbitEye = checkRange && rabbitCameraPanel.Visible ? rabbitCamera.Position : null;
        Vector3? regattaEye = track.HasWaterSections && racers.Count > localRacerIndex ? Player.CoursePosition(track) : null;
        float waterTime = RegattaWaterTime;
        foreach (var pickup in pickups)
        {
            pickup.SetAvailable(time >= pickup.ReadyAt);
            if (!pickup.Available) continue;
            bool water = track.HasWaterSections && track.TravelMode(pickup.Distance) == TrackTravelMode.JetSki;
            if (water && regattaEye is { } ski && pickup.Anchor.DistanceSquaredTo(ski) < PickupAnimationRangeSquared)
                pickup.Node.Position = pickup.Anchor + Vector3.Up * WaveAt(pickup.Anchor, waterTime);

            float phase = time / PickupCycleSeconds + pickup.Index * 1.37f;
            var items = water ? waterPickupItems : pickup.Distance >= track.DismountLine ? footPickupItems : pickupItems;
            var item = items[Mathf.FloorToInt(phase) % items.Length];
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
                pickup.Halo.MaterialOverride = (water ? regattaPickupColors ?? pickupColors : pickupColors)[(int)item - 1];
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
