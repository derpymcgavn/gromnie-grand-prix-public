using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const uint CheerEmote = 0x1300004c, ClapEmote = 0x1300007e, YmcaEmote = 0x1200009b, DanceEmote = 0x13000151;
    private enum Spectacle { Cheer, Dance, Duel }
    private sealed class Spectator(AcActor actor, uint emote, float next)
    {
        public readonly AcActor Actor = actor;
        public readonly uint Emote = emote;
        public float Next = next;
    }
    private sealed class SpectacleSite(float distance, int side, Vector3 center, Spectacle kind)
    {
        public readonly float Distance = distance;
        public readonly int Side = side;
        public readonly Vector3 Center = center;
        public readonly Spectacle Kind = kind;
        public readonly List<Spectator> Cast = [];
        public Node3D? Stage;
        public bool Reacted;
    }

    private readonly List<SpectacleSite> roadsideSites = [];
    private Node3D roadsideRoot = null!;
    private float nextCrowdScan, nextCrowdSound;

    private void BuildRoadside()
    {
        roadsideSites.Clear();
        roadsideRoot = new Node3D { Name = "Roadside spectators and hazards" };
        courseItems.AddChild(roadsideRoot);
    }

    private void ResetRoadside()
    {
        foreach (var child in roadsideRoot.GetChildren())
        {
            roadsideRoot.RemoveChild(child);
            child.QueueFree();
        }
        roadsideSites.Clear();
        nextCrowdScan = nextCrowdSound = 0;
        int count = Mathf.Clamp(Mathf.CeilToInt(track.Length / (track.IsRegional ? 440 : 145)), 3, 16);
        for (int i = 0; i < count; i++)
        {
            float at = BogCircuit.Grid + Math.Min(27, track.Length * .12f) + i * track.Length / count;
            int side = i % 2 == 0 ? 1 : -1;
            Vector3 center = track.FreeGround(track.Surface(at, side * (track.HalfWidth + 3.8f)));
            if (!track.CanStageSpectator(center))
            {
                side = -side;
                center = track.FreeGround(track.Surface(at, side * (track.HalfWidth + 3.8f)));
            }
            if (track.CanStageSpectator(center)) roadsideSites.Add(new SpectacleSite(at, side, center, (Spectacle)(i % 3)));
        }
        BuildRoadHazards();
        ResetEncounters();
    }

    private void TickRoadside(float dt)
    {
        Vector3 near = Player.CoursePosition(track);
        if (elapsed >= nextCrowdScan)
        {
            nextCrowdScan = elapsed + .30f;
            var visible = roadsideSites.Where(site => site.Center.DistanceSquaredTo(near) < (site.Stage is null ? 104 * 104 : 119 * 119))
                .OrderBy(site => site.Center.DistanceSquaredTo(near)).Take(2).ToArray();
            foreach (var site in roadsideSites)
                if (site.Stage is not null && !visible.Contains(site))
                {
                    roadsideRoot.RemoveChild(site.Stage);
                    site.Stage.QueueFree();
                    site.Stage = null;
                    site.Cast.Clear();
                }
            // Load one nearby group at a time to spread character construction across frames.
            var arriving = visible.FirstOrDefault(site => site.Stage is null);
            if (arriving is not null) StageSpectacle(arriving);
        }

        foreach (var site in roadsideSites)
        {
            if (site.Stage is null) continue;
            float distance = site.Center.DistanceSquaredTo(near);
            if (distance > 44 * 44) site.Reacted = false;
            if (!site.Reacted && site.Cast.Count >= 4 && distance < 33 * 33 && raceAudio is { } audio)
            {
                audio.Play(audio.Clip("Crowd-cheer"), site.Cast[^1].Actor, false, -12.5f);
                site.Reacted = true;
            }
            foreach (var performer in site.Cast)
            {
                var actor = performer.Actor;
                if (!actor.ActionBusy && elapsed >= performer.Next)
                {
                    if (site.Kind == Spectacle.Duel && performer.Emote == 0)
                        actor.Attack();
                    else if (!actor.Play(performer.Emote, true))
                        actor.Play(CheerEmote, true);
                    performer.Next = elapsed + (site.Kind == Spectacle.Duel ? 1.4f : 2.8f) + .18f * (actor.GetIndex() % 3);
                }
                actor.Tick(dt, false, Vector3.Zero);
            }
        }
        TickRoadHazards(near);
        TickEncounters(dt, near);
    }

    private void StageSpectacle(SpectacleSite site)
    {
        site.Stage = new Node3D { Name = site.Kind.ToString() };
        roadsideRoot.AddChild(site.Stage);
        if (site.Kind == Spectacle.Duel)
        {
            var first = AcCatalog.Enemies[0];
            var second = AcCatalog.Enemies[1];
            Vector3 lengthwise = track.Forward(site.Distance) * 1.18f;
            Vector3 drudgeAt = track.FreeGround(site.Center - lengthwise);
            Vector3 tuskerAt = track.FreeGround(site.Center + lengthwise);
            if (track.CanStageSpectator(drudgeAt) && track.CanStageSpectator(tuskerAt))
            {
                AddSpectator(site, assets.CreateActor(first.Setup, first.Motion, 1.0f), drudgeAt, tuskerAt, 0, .12f);
                AddSpectator(site, assets.CreateActor(second.Setup, second.Motion, 1.0f), tuskerAt, drudgeAt, 0, .8f);
            }
        }

        bool duel = site.Cast.Count > 0;
        int spectators = duel ? 5 : 10;
        for (int i = 0; i < spectators; i++)
        {
            float offset = (i % 5 - 2) * 1.65f + (i / 5) * .57f;
            float lane = site.Side * (track.HalfWidth + (duel ? 6.4f : 2.0f) + i / 5 * 2.0f);
            Vector3 position = track.FreeGround(track.Surface(site.Distance + offset, lane));
            if (!track.CanStageSpectator(position)) continue;
            var look = creation.Resolve(RivalLook(1 + i % 4));
            var actor = assets.CreateActor(look.Setup, look.Motion, look.Scale * (i % 3 == 0 ? .93f : 1));
            actor.SetCharacterAppearance(look);
            uint emote = site.Kind == Spectacle.Dance ? (i % 4 == 3 ? DanceEmote : YmcaEmote)
                : (i % 3 == 0 ? ClapEmote : CheerEmote);
            AddSpectator(site, actor, position, track.Point(site.Distance + offset), emote,
                site.Kind == Spectacle.Dance ? .18f + i % 4 * .08f : .19f * i);
        }
    }

    private void AddSpectator(SpectacleSite site, AcActor actor, Vector3 at, Vector3 face, uint emote, float delay)
    {
        site.Stage!.AddChild(actor);
        actor.Position = at;
        actor.Face(new Vector3(face.X - at.X, 0, face.Z - at.Z));
        actor.OnHook = (hook, _) =>
        {
            if (raceAudio is null || elapsed < nextCrowdSound || actor.Position.DistanceSquaredTo(Player.CoursePosition(track)) > 40 * 40) return;
            if (raceAudio.PlayEmote(hook, actor)) nextCrowdSound = elapsed + .55f;
        };
        site.Cast.Add(new Spectator(actor, emote, elapsed + delay));
    }
}
