using Godot;

public partial class GrandPrix
{
    private void CheckRaceEncounters()
    {
        Require(raceEncounters.Select(site => site.Kind).Distinct().Count() == Enum.GetValues<EncounterKind>().Length &&
            raceEncounters.All(site => CanStageEncounter(site.Distance) && site.Lamps.Count == 2),
            "the regional test course stages every roadside encounter away from gates, pickups and flight routes");
        CheckOlthoiCrossingArt();
        var spells = archive!.ReadSpells().ToDictionary(spell => spell.Id);
        Require(RollingDeathSpells.All(spell => spells.TryGetValue(spell.Id, out var row) && row.Name == "Rolling Death" &&
            assets.Setup(spell.Setup).DefaultAnimation != 0 && assets.EffectScript(spell.Effects, 4) != 0 &&
            assets.EffectScript(spell.Effects, 5) != 0), "all four Rolling Death spells match the native animation and effect records");
        foreach (var kind in Enum.GetValues<EncounterKind>())
        {
            ResetRace();
            var site = raceEncounters.First(row => row.Kind == kind);
            countdown = 0;
            const float step = 1f / 60;
            Player.Distance = site.Distance - 47;
            Player.Speed = 20;
            TickEncounters(step, Player.CoursePosition(track));
            Require(site.Stage is not null && site.Lamps.All(lamp => lamp.MaterialOverride == lampOn) &&
                site.Bodies.All(body => !body.Dangerous) && message == site.Callout,
                $"{kind} gives racers a visible warning before contact is possible");
            bool swarm = kind == EncounterKind.OlthoiCrossing;
            bool quarry = kind == EncounterKind.LugianQuarry;
            bool rolling = kind == EncounterKind.RollingDeath;
            float crossing = kind switch
            {
                EncounterKind.RollingDeath => DeathRollRelease + DeathRollAlongTime * .51f,
                EncounterKind.LugianQuarry => QuarryFirstThrow + QuarryTravel * .47f,
                _ => EncounterWarningTime + (swarm ? site.Reach / 9.8f : TossTravel * .5f)
            };
            int startingEffects = itemEffects!.PlayedScripts;
            if (rolling) Require(site.Cast.All(orb => !orb.Visible), "the Rolling Death warning precedes both launches");
            while (elapsed < site.Start + crossing)
            {
                elapsed += step;
                TickEncounters(step, Player.CoursePosition(track));
                if (rolling) itemEffects.Tick(step);
            }
            var body = site.Bodies[0];
            float crossingStation = site.Distance;
            if (rolling)
            {
                crossingStation += DeathRollOffset(0, DeathRollProgress(0, elapsed - site.Start));
                Require(CanStageRollingDeath(site.Distance, site.Side) && site.Bodies.All(orb => orb.Dangerous) &&
                    site.Cast.Select(actor => actor.Setup.Id).SequenceEqual(site.Bodies.Select((_, i) => DeathRollElement(site, i).Setup)) &&
                    site.Cast[0].Position == body.Position && body.Heading.Dot(track.Forward(crossingStation)) < -.7f &&
                    Math.Abs(site.Bodies[1].Heading.Dot(track.Right(site.Distance + DeathRollCrossing))) > .95f &&
                    itemEffects.PlayedScripts >= startingEffects + 2 && itemEffects.ActiveParticles > 0,
                    "native Rolling Death orbs play elemental effects along the road and across it");
            }
            else
                Require(body.Dangerous && (swarm
                    ? site.Cast.Count == 7 && site.Cast.Count(actor => actor.Visible && actor.CurrentCommand is 0x44000007 or 0x45000005) >= 2
                    : site.Cast.Count == 2 && site.Projectile?.Position == body.Position),
                    $"{kind} animates the DAT cast around its moving hit volumes");
            if (quarry)
                Require(site.Cast[0].HeldSetup == QuarryPickaxe && site.Cast[0].HasVisibleHeldMesh &&
                    site.Cast.All(lugian => lugian.LastActionAnimations.Length > 0) && site.QuarryPile.All(rock => !rock.Visible),
                    "the Lugian miner swings a native pickaxe, the thrower heaves and the pile starts empty");
            Vector3 hit = body.Position - Vector3.Up * (swarm ? 0 : rolling ? DeathRollHeight : 2.29f);
            var across = track.Forward(crossingStation) * 4;
            Player.Distance = crossingStation;
            float checkpoint = Player.Checkpoint;
            Player.Ward = 1;
            CheckEncounterHits(Player, 0, hit - across, hit + across);
            Require(Player.Ward == 0 && Player.EjectTime == 0, $"{kind} can be blocked with a Portal Ward");

            var rival = racers[1];
            rival.Distance = crossingStation - 8;
            rival.Speed = 18;
            Require(SenseRivalObstacles(rival).Any(obstacle => obstacle.Crossing), $"rivals can see the approaching {kind} in time");
            CheckEncounterHits(rival, 1, hit - across + Vector3.Up * 8, hit + across + Vector3.Up * 8);
            Require(rival.EjectTime == 0, $"{kind} does not hit a racer high above the crossing");
            if (rolling)
            {
                Vector3 away = track.Right(crossingStation) * -site.Side * 5.2f;
                CheckEncounterHits(rival, 1, hit - across + away, hit + across + away);
                Require(rival.EjectTime == 0, "steering into the open lane avoids Rolling Death");
            }
            CheckEncounterHits(rival, 1, hit - across, hit + across);
            Require(rival.EjectTime > 0 && rival.Rider.TopLevel && rival.Speed == 0,
                $"{kind} uses the standard dismount on a swept collision");
            site.HitMask = 0;
            Player.Distance = crossingStation - 1.5f;
            CheckEncounterHits(Player, 0, hit - across, hit + across);
            Require(Player.EjectTime > 0 && Player.Stability == 0, $"{kind} can knock the player off too");
            for (int frame = 0; frame < 90 && Player.EjectTime > 0; frame++) TickRace(step, false, false, 0, false, false);
            Require(!Player.Rider.TopLevel && Player.Distance == checkpoint && Player.ResetHold > 0,
                $"{kind} remounts at the last checkpoint");
            if (quarry) CheckQuarryPile(site);
            if (rolling)
            {
                elapsed = site.Start + DeathRollRelease + DeathRollAlongTime + .10f;
                TickEncounters(step, site.Center);
                Require(site.Bodies.All(orb => !orb.Dangerous) && site.Cast.All(actor => !actor.Visible),
                    "Rolling Death dissipates at the end of its lanes");
            }
        }
        ResetRace();
        Require(raceEncounters.All(site => float.IsPositiveInfinity(site.Start) && site.HitMask == 0 && site.Stage is null),
            "retry resets all encounter state");
        GD.Print($"RACE ENCOUNTERS PASS | {raceEncounters.Count} sites on {track.Name} | warnings, native cast, dodging, dismount and checkpoint recovery.");
    }

    private void CheckQuarryPile(RaceEncounter site)
    {
        elapsed = Math.Max(elapsed, site.Start + QuarryFirstThrow + QuarryTravel + .04f);
        TickEncounters(1f / 60, site.Center);
        Require(site.QuarryPile.Count(rock => rock.Visible) == 1 && site.Projectile?.Visible == false,
            "the first boulder lands and becomes part of the growing pile");
        elapsed = site.Start + site.Duration + .04f;
        TickEncounters(1f / 60, site.Center);
        Require(site.QuarryPile.Count(rock => rock.Visible) == QuarryThrows && site.Lamps.All(lamp => lamp.MaterialOverride == lampOff),
            "the finished pile stays on the far shoulder after the warning ends");
        TickEncounters(1f / 60, site.Center + Vector3.Up * 200);
        Require(site.Stage is null, "the quarry unloads from a distance");
        TickEncounters(1f / 60, site.Center);
        Require(site.QuarryPile.Count(rock => rock.Visible) == QuarryThrows && site.Projectile?.Visible == false,
            "coming back past the quarry restores the same pile without starting another toss");
    }

    private void CheckOlthoiCrossingArt()
    {
        var crossingSprites = roadsideRoot.GetChildren().OfType<Node3D>()
            .SelectMany(node => node.GetChildren().OfType<Sprite3D>())
            .Where(sprite => sprite.Name == "Olthoi crossing artwork").ToArray();
        Require(crossingSprites.Length >= 2 && crossingSprites.All(sprite => sprite.Texture?.ResourcePath == CrossingArt),
            "crossing signs use the imported texture resource");
        GD.Print($"OLTHOI CROSSING ART PASS | {CourseName(activeTown)} | {crossingSprites.Length} signs.");
    }

    private void StartEncounterCheckCourse(string? name = null)
    {
        int id = name is null ? mediumRoutes.Keys.First() : mediumRoutes.Keys.Single(course =>
            string.Equals(CourseName(course), name, StringComparison.OrdinalIgnoreCase));
        OpenStable();
        ChooseCourse(id);
        StartRace();
        Require(track.IsMedium && racing, "encounter showcase course is playable");
    }

    private async Task CaptureRaceEncounters(string folder)
    {
        StartEncounterCheckCourse();
        foreach (var kind in Enum.GetValues<EncounterKind>())
        {
            ResetRace();
            countdown = 0;
            paused = true;
            var site = raceEncounters.First(row => row.Kind == kind);
            bool swarm = kind == EncounterKind.OlthoiCrossing;
            bool quarry = kind == EncounterKind.LugianQuarry;
            bool rolling = kind == EncounterKind.RollingDeath;
            if (rolling) site.DeathRollElement = 1;
            Player.Distance = site.Distance - (rolling ? 24 : swarm ? 14 : quarry ? 9.6f : 5.3f);
            Player.Lane = track.HalfWidth * (rolling ? -.39f * site.Side : -.35f);
            Player.Speed = 17;
            Player.Draw(track, .17f, true);
            site.Start = elapsed = 1;
            float age = kind switch
            {
                EncounterKind.RollingDeath => DeathRollRelease + DeathRollAlongTime * .53f,
                EncounterKind.LugianQuarry => QuarryFirstThrow + (QuarryThrows - 1) * QuarryPeriod + QuarryTravel * .46f,
                _ => EncounterWarningTime + (swarm ? site.Reach / 10 + .70f : TossTravel * .57f)
            };
            while (elapsed < site.Start + age)
            {
                elapsed += 1f / 60;
                TickEncounters(1f / 60, Player.CoursePosition(track));
                dust.Tick(1f / 60);
                itemEffects!.Tick(1f / 60);
            }
            Vector3 forward = track.Forward(site.Distance), right = track.Right(site.Distance);
            camera.Position = rolling
                ? track.Surface(Player.Distance - 10, Player.Lane * .63f) + Vector3.Up * 6.0f
                : site.Center - forward * (swarm ? 28 : quarry ? 25 : 22) + right * (swarm ? -2 : quarry ? -site.Side * 2.5f : -3.3f) +
                    Vector3.Up * (quarry ? 7.0f : 5.65f);
            camera.Fov = swarm ? 63 : 69;
            camera.LookAt(site.Center + (swarm ? right * 3.15f : Vector3.Zero) + Vector3.Up * 1.4f);
            Callout(site.Callout, 2);
            UpdateRaceUi();
            await SaveFrame(Path.Combine(folder, kind switch
            {
                EncounterKind.RollingDeath => "Rolling-Death.png",
                EncounterKind.LugianQuarry => "Lugian-Quarry.png",
                EncounterKind.OlthoiCrossing => "Olthoi-Crossing.png",
                _ => "Tusker-Barrel-Toss.png"
            }));
        }
    }
}
