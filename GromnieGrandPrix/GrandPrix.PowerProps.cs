using Godot;

public partial class GrandPrix
{
    private sealed class PowerToss(MountedRacer owner, RaceItem item, Node3D node, Vector3 start, Vector3 end)
    {
        public readonly MountedRacer Owner = owner;
        public readonly RaceItem Item = item;
        public readonly Node3D Node = node;
        public readonly Vector3 Start = start, End = end;
        public float Age;
        public int CoinsDropped;
    }

    private sealed record PowerProp(Node3D Node, MountedRacer Owner, RaceItem Item, float EndAt);
    private readonly List<PowerToss> powerTosses = [];
    private readonly List<PowerProp> powerProps = [];

    private void LobPower(MountedRacer racer, RaceItem item)
    {
        bool hammer = item == RaceItem.SmithHammer;
        float range = hammer ? 23f : 27f;
        Vector3 start = RiderTarget(racer) + racer.Heading(track) * 1.05f;
        Vector3 end = racer.FreeDriving
            ? track.FreeGround(start + racer.Heading(track) * range)
            : track.Surface(racer.Distance + range, racer.Lane);
        var node = assets.CreateActor(hammer ? HammerSetup : PyrealSetup, 0, hammer ? 1.28f : 2.15f);
        node.Position = start;
        itemFxRoot.AddChild(node);
        powerTosses.Add(new PowerToss(racer, item, node, start, end));
    }

    private void TickPowerProps(float dt)
    {
        TickPowerContacts();
        for (int i = powerTosses.Count - 1; i >= 0; i--)
        {
            var toss = powerTosses[i];
            bool hammer = toss.Item == RaceItem.SmithHammer;
            toss.Age += dt;
            float t = Math.Min(1f, toss.Age / .78f);
            Vector3 next = toss.Start.Lerp(toss.End, t) + Vector3.Up * (Mathf.Sin(Mathf.Pi * t) * (hammer ? 5.2f : 2.3f));
            toss.Node.Position = next;
            toss.Node.RotateX(dt * 13f);
            if (!hammer)
            {
                int coins = Mathf.FloorToInt(t * 5);
                while (toss.CoinsDropped < coins)
                {
                    float along = ++toss.CoinsDropped / 5f;
                    AddPyreal(toss.Owner, track.FreeGround(toss.Start.Lerp(toss.End, along)));
                }
            }
            if (t < 1) continue;
            powerTosses.RemoveAt(i);
            if (hammer)
            {
                toss.Node.Position = toss.End + Vector3.Up * .25f;
                toss.Node.Rotation = new Vector3(.3f, .7f, -1.15f);
                powerProps.Add(new PowerProp(toss.Node, toss.Owner, toss.Item, elapsed + 6.2f));
                Wave(toss.End + Vector3.Up * .16f, MightTint, 2.25f, .58f);
                foreach (var rival in racers)
                    if (rival != toss.Owner && rival.CoursePosition(track).DistanceSquaredTo(toss.End) < 2.3f * 2.3f)
                        PowerImpact(rival, MightTint, 1.5f, .5f);
            }
            else toss.Node.QueueFree();
        }

        for (int i = powerProps.Count - 1; i >= 0; i--)
        {
            var prop = powerProps[i];
            if (elapsed >= prop.EndAt) { RemovePowerProp(i); continue; }
            bool coin = prop.Item == RaceItem.PyrealSling;
            if (coin) prop.Node.RotateY(dt * 4.25f);
            foreach (var racer in racers)
            {
                if (!CanFrostboltHit(racer) || racer.LeapHeight > .45f || !coin && (racer == prop.Owner || racer.DodgesItems || racer.ItemGrace > 0)) continue;
                float reach = coin ? 1.75f : 1.48f;
                if (racer.CoursePosition(track).DistanceSquaredTo(prop.Node.Position) > reach * reach) continue;
                if (coin)
                {
                    if (racer.Pyreals >= MaxPyreals) continue;
                    CreditPyreal(racer);
                    Wave(prop.Node.Position, PyrealTint, 1.3f, .34f);
                }
                else PowerImpact(racer, MightTint, 1.15f, .58f);
                RemovePowerProp(i);
                break;
            }
        }
    }

    private void AddPyreal(MountedRacer owner, Vector3 ground)
    {
        var node = assets.CreateActor(PyrealSetup, 0, 1.65f);
        node.Position = ground + Vector3.Up * .55f;
        itemFxRoot.AddChild(node);
        var sprite = new Sprite3D
        {
            Texture = ItemIcon(RaceItem.PyrealSling), PixelSize = .031f, Position = Vector3.Up * .28f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        node.AddChild(sprite);
        powerProps.Add(new PowerProp(node, owner, RaceItem.PyrealSling, elapsed + 10));
    }

    private void ClearPowerProps()
    {
        foreach (var toss in powerTosses) toss.Node.QueueFree();
        powerTosses.Clear();
        foreach (var prop in powerProps) prop.Node.QueueFree();
        powerProps.Clear();
    }

    private void RemovePowerProp(int i)
    {
        powerProps[i].Node.QueueFree();
        powerProps.RemoveAt(i);
    }

    private void CastForceBurst(MountedRacer owner)
    {
        const float reach = 7.3f;
        Vector3 center = RiderTarget(owner);
        bool Within(Vector3 point) => point.DistanceSquaredTo(center) < reach * reach;
        Wave(center, ForceTint, reach, .48f, new(.12f, 0, 0));
        Wave(center, new("efffff"), reach * .82f, .39f, new(-.23f, 0, 0));
        itemEffects?.PlayAt(DerethSurvivors.AcCatalog.Projectiles["shock"].Effects, 5, center);
        foreach (var rival in racers)
            if (rival != owner && Within(RiderTarget(rival))) PowerImpact(rival, ForceTint, 1.65f, .48f);
        for (int i = shots.Count - 1; i >= 0; i--)
            if (shots[i].Owner != owner && Within(shots[i].Node.Position)) RemoveItemShot(i);
        for (int i = steinPours.Count - 1; i >= 0; i--)
            if (steinPours[i].Owner != owner && Within(steinPours[i].SplashHead.Position)) RemoveSteinPour(i);
        for (int i = powerTosses.Count - 1; i >= 0; i--)
        {
            if (powerTosses[i].Item != RaceItem.SmithHammer || !Within(powerTosses[i].Node.Position)) continue;
            powerTosses[i].Node.QueueFree();
            powerTosses.RemoveAt(i);
        }
        for (int i = powerProps.Count - 1; i >= 0; i--)
            if (powerProps[i].Item == RaceItem.SmithHammer && Within(powerProps[i].Node.Position)) RemovePowerProp(i);
        for (int i = mud.Count - 1; i >= 0; i--)
        {
            if (!Within(mud[i].Node.Position)) continue;
            mud[i].Node.QueueFree();
            mud.RemoveAt(i);
        }
        for (int i = rabbits.Count - 1; i >= 0; i--)
        {
            var chase = rabbits[i];
            if (chase.Owner == owner || !Within(chase.Root.Position)) continue;
            if (chase == filmedRabbit) ClearRabbitCamera();
            chase.Root.QueueFree();
            rabbits.RemoveAt(i);
        }
    }
}
