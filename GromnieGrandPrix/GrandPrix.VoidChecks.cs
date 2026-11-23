using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private void StartVoidCheck(int id = VoidCourseId)
    {
        ChooseCourse(id);
        StartRace();
        Require(racing && activeTown == id && track.IsVoid && weatherEnvironment.Sky == voidSky,
            $"the dark circuit launches from the course picker ({courseProblem})");
    }

    private void CheckVoidCourse()
    {
        var originalSky = weatherEnvironment.Sky;
        PopulateCoursePicker(VoidCourseName);
        Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == VoidCourseId,
            "the special course is findable by name");
        StartVoidCheck();
        Require(track.Length is > 4500 and < 4800 && track.Laps == 5 && track.GateCount == 14 && track.TightestInsideRadius() > 70,
            $"the dark circuit sweeps through two 4.6 km combined orbits with generous turns ({track.Length:N0} m, {track.TightestInsideRadius():N1} m inside radius)");
        var road = course.GetNode<MeshInstance3D>("Rynthid energy track/Rynthid cloak road");
        var material = (ShaderMaterial)road.MaterialOverride;
        Require(material.GetShaderParameter("cloak_energy").AsGodotObject() == assets.Texture(RynthidEnergyTexture),
            "the road samples the original Rynthid cloak texture");
        var surface = road.Mesh.SurfaceGetArrays(0);
        var vertices = surface[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv = surface[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Require(vertices[0] == vertices[^2] && vertices[1] == vertices[^1] &&
            Mathf.IsEqualApprox(uv[^1].Y, Mathf.Round(uv[^1].Y)), "the energy road and its texture join across the seam");
        var caster = course.GetNode<Node3D>("The Summoning/Bael’Zharon");
        Require(caster.GlobalPosition.Abs().Dot(new Vector3(1, 0, 1)) < .01f && caster.Scale.X >= 37 &&
                course.HasNode("The Summoning/Event Horizon") && course.HasNode("The Summoning/Asheron’s binding chains"),
            "a giant Bael’Zharon, his singularity and Asheron’s bindings occupy the middle of the race scene");
        var chains = course.GetNode<Node3D>("The Summoning/Asheron’s binding chains");
        var shard = course.GetNode<Node3D>("The Summoning/Shard of the Herald");
        var captive = shard.GetNode<AcActor>("Trapped Bael’Zharon/Native Bael’Zharon");
        Require(!chains.Visible && !shard.Visible && shard.GetNode<AcActor>("Native crystal").NativeAppearanceId == 8542,
            "the finale starts hidden and uses the original Shard of the Herald appearance");
        Require(!captive.IsVisibleInTree() && captive.NativeAppearanceId == 8503 &&
                shard.HasNode("Splintered facets") && !shard.GetNode<Node3D>("Crimson flame smoke").IsVisibleInTree(),
            "the shard has jagged glass and hides the captured native Bael and smoke before sealing");
        var route = track.Curve.GetBakedPoints();
        Require(route.All(p => new Vector2(p.X, p.Z).Length() > 145), "both orbits keep clear of Bael’Zharon’s prison");
        Require(route.Max(p => p.Y) - route.Min(p => p.Y) > 115, "the circuit climbs and drops through more than 115 m of elevation");
        var upper = track.Project(new Vector3(-298, 162, 133), 825);
        var lower = track.Project(new Vector3(-298, 40, 133), 3790);
        Vector3 highDeck = track.Surface(upper.Distance, upper.Lane);
        Vector3 lowDeck = track.Surface(lower.Distance, lower.Lane);
        Require(highDeck.Y - lowDeck.Y > 115 && new Vector2(highDeck.X - lowDeck.X, highDeck.Z - lowDeck.Z).Length() < 5,
            "the two orbits cross at different heights and project to their own stations");
        foreach (var deck in new[] { highDeck, lowDeck })
            Require(Math.Abs(track.GroundOrRoadHeight(deck - Vector3.Up * 2) - deck.Y) < .12f,
                "camera clearance and rider contact resolve the correct deck at the overpass");
        Require(course.HasNode("Aerbax's charging dais/Aerbax") &&
                course.HasNode("Asheron's binding circle/Tower Guardian West") &&
                course.HasNode("Asheron's binding circle/Tower Guardian East"),
            "Aerbax and Asheron’s two Tower Guardians occupy the outer stages");
        CheckVoidGuardianBlades();
        CheckVoidBattle();
        CheckVoidFlyoverAndDrop(highDeck, lowDeck);

        for (int frame = 0; frame < 80000 && !finished; frame++)
        {
            bool sudsed = IsSudsed(Player);
            float steer = Mathf.Clamp((6.2f - Player.Lane) * (sudsed ? -.42f : .42f), -1, 1);
            TickRace(1f / 60, !sudsed, sudsed, steer, false, false);
        }
        Require(finished && Player.GatesCleared == track.GateCount * track.Laps + 1 && bestByCourse.ContainsKey(VoidCourseId),
            $"all five laps of the Rynthid circuit record a finish (station {Player.Distance:F1}, gates {Player.GatesCleared})");
        Require(Player.LapCrossings.Count == track.Laps && Player.LapCrossings[^1] == Player.Finish &&
            racers.Skip(1).Any(racer => racer.LapCrossings.Count == track.Laps && racer.Finish == racer.LapCrossings[^1]) &&
            voidSealAt == racers.Where(racer => racer.Finish >= 0).Min(racer => racer.Finish),
            "every lap split uses the checkpoint clock and the first finisher starts the seal");
        if (voidFinaleCamera) TickVoidFinale(VoidFinaleDuration);
        Require(!caster.Visible && !chains.Visible && shard.Visible && captive.IsVisibleInTree() &&
                shard.GetNode<Node3D>("Crimson flame smoke").IsVisibleInTree() && resultsUi.Visible,
            "Bael is sealed inside the native shard before results appear");
        int lapCount = track.Laps;
        float length = track.Length;
        StartVoidCheck();
        Require(caster.Visible && !shard.Visible && !chains.Visible && !captive.IsVisibleInTree() && voidSealAt < 0 &&
            racers.All(racer => racer.LapCrossings.Count == 0), "starting another race resets the seal and lap times");
        OpenStable();
        Require(weatherEnvironment.Sky == originalSky && !racing, "leaving the special course restores the normal sky");
        GD.Print($"RYNTHID SINGULARITY PASS | {lapCount} laps, {length * lapCount:N0} m, overpass, flyover, edge recovery, and the native Shard of the Herald finale.");
        CheckVoidLayouts();
    }

    private void CheckVoidLayouts()
    {
        foreach (int id in new[] { VoidFootworkCourseId, VoidAerbaxCourseId })
        {
            string name = CourseName(id);
            PopulateCoursePicker(name);
            Require(courseList.ItemCount == 1 && courseList.GetItemMetadata(0).AsInt32() == id,
                $"the picker finds {name} by its own name and ID");
            StartVoidCheck(id);
            var points = track.Curve.GetBakedPoints();
            bool footwork = id == VoidFootworkCourseId;
            var boss = footwork ? Vector3.Zero : VoidAerbaxPosition;
            float nearest = points.Min(p => new Vector2(p.X - boss.X, p.Z - boss.Z).Length());
            float radius = track.TightestInsideRadius();
            Require(track.Length > 2300 && track.Laps == 5 && radius >= BogCircuit.MinimumInsideRadius,
                $"{name} is a wide, sweeping five-lap circuit ({track.Length:F0} m, {radius:F1} m inside radius)");
            Require(nearest > (footwork ? 65 : 95) && nearest < (footwork ? 105 : 150),
                $"{name} passes close to its featured boss clear of the stage ({nearest:F1} m)");
            Require(footwork ? points.Any(p => p.Y < 29 && new Vector2(p.X, p.Z).Length() < 100)
                : points.All(p => new Vector2(p.X, p.Z).Length() > 130), $"{name} clears Bael’Zharon’s body");
            Require(points[0].DistanceTo(points[^1]) < .05f && track.Surface(0, track.HalfWidth).IsEqualApprox(track.Surface(track.Length, track.HalfWidth)),
                $"{name} joins continuously across the center and shoulder of the loop");
            Require(flyover is { } route && track.Gate(1) < route.Start && track.Gate(2) > route.End,
                $"{name} keeps its flyover between two checkpoints");
            Require(voidBattle!.Attacks.All(attack => attack.Sites.Length >= 5), $"{name} has strike sites for all three casters");

            countdown = 0;
            for (int frame = 0; frame < 24000 && Player.LapCrossings.Count == 0; frame++)
            {
                bool sudsed = IsSudsed(Player);
                float steer = Mathf.Clamp((6.2f - Player.Lane) * (sudsed ? -.42f : .42f), -1, 1);
                TickRace(1f / 60, !sudsed, sudsed, steer, false, false);
            }
            Require(Player.LapCrossings.Count >= 1 && Player.GatesCleared >= track.GateCount + 1,
                $"{name} completes a timed lap through all checkpoints (station {Player.Distance:F1}, gates {Player.GatesCleared})");
            GD.Print($"VOID LAYOUT PASS | {name} · {track.Length:F0} m/lap · {nearest:F0} m boss pass · {radius:F0} m inside radius · timed lap.");
            OpenStable();
        }
    }

    private void CheckVoidFlyoverAndDrop(Vector3 highDeck, Vector3 lowDeck)
    {
        const float step = 1f / 60;
        ResetRace();
        countdown = 0;
        var sector = VoidFlyover(track);
        float secondLap = track.Length;
        Player.Distance = secondLap + sector.Start - .20f;
        Player.Lane = 0;
        Player.Checkpoint = track.Gate(track.GateCount + 1);
        Player.GatesCleared = track.GateCount + 2;
        Player.Speed = 28;
        TickRace(step, true, false, 0, false, false);
        Require(Player.Flight is { Direct: true } && secondLap + sector.End < track.Gate(track.GateCount + 2),
            "the optional flight launches between checkpoints on a later lap");
        float altitude = 0;
        for (int frame = 0; frame < 900 && Player.Flight is { } flight; frame++)
        {
            altitude = Math.Max(altitude, flight.Position.Y);
            TickRace(step, true, false, 0, false, false);
        }
        Require(Player.Flight is null && Math.Abs(Player.Distance - (secondLap + sector.End)) < 1 &&
                altitude > highDeck.Y + 60 && Player.GatesCleared == track.GateCount + 2,
            "the flyover rises above both decks and lands with race progress intact");

        ResetRace();
        countdown = 0;
        Player.Distance = track.Project(lowDeck, track.Length * .8f).Distance;
        Player.Checkpoint = track.Gate(0);
        Player.GatesCleared = 1;
        Player.Speed = 24;
        Player.Lane = track.HalfWidth + .22f;
        MovePlayer(.10f, true, false, 1, false);
        Require(Player.Falling, "steering past the unguarded void edge starts a fall");
        for (int frame = 0; frame < 90 && Player.Falling; frame++) TickTrackFall(Player, step);
        Require(!Player.Falling && Math.Abs(Player.Distance - Player.Checkpoint) < 1 && Player.GatesCleared == 1,
            "an edge fall recovers at the saved checkpoint");
        ResetRace();
    }

    private async Task CaptureVoidCourse(string folder, bool video)
    {
        if (OS.GetCmdlineUserArgs().Contains("--void-layouts"))
        {
            await CaptureVoidLayouts(folder);
            return;
        }
        StartVoidCheck();
        SetPhysicsProcess(false);
        countdown = 0;
        if (OS.GetCmdlineUserArgs().Contains("--void-guardians"))
        {
            await CaptureVoidGuardians(folder, video);
            return;
        }
        var redSite = voidBattle!.Attacks[0].Sites[0];
        float stageSpan = VoidVolleyPeriod / 3;
        ClearSpeedBlur();
        raceUi.Hide();
        if (video)
        {
            for (int frame = 0; frame < 102; frame++)
            {
                PreviewBattle(stageSpan + 2.15f + frame / 30f);
                StageCamera(new Vector3(220 - frame * 1.55f, 474, 1040), new Vector3(0, 83, -55), 65);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            for (int frame = 0; frame < 105; frame++)
            {
                PreviewBattle(1.68f + frame / 30f);
                StageCamera(new Vector3(-560 + frame * .94f, 99 - frame * .11f, 396 - frame * 1.02f), new Vector3(-125, 118, 48), 59);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            for (int frame = 0; frame < 83; frame++)
            {
                PreviewBattle(8.0f + frame / 30f);
                PoseFlyover(.26f + frame / 190f);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        else
        {
            PreviewBattle(stageSpan + VoidFireAt - .10f);
            StageCamera(new Vector3(150, 474, 1040), new Vector3(0, 83, -55), 65);
            await SaveFrame(Path.Combine(folder, "Rynthid-Battle-Arena.png"));
            PreviewBattle(VoidFireAt - .14f);
            StageCamera(new Vector3(-551, 98, 378), new Vector3(-140, 117, 55), 57);
            await SaveFrame(Path.Combine(folder, "Rynthid-Overpass.png"));
            PreviewBattle(stageSpan + VoidImpactAt + .38f);
            PoseFlyover(.43f);
            await SaveFrame(Path.Combine(folder, "Rynthid-Flyover.png"));
            var crystalBatches = course.GetNode<Node3D>("Rynthid energy track").GetChildren()
                .OfType<MultiMeshInstance3D>().Where(batch => batch.Name.ToString().StartsWith("Floating prisms ", StringComparison.Ordinal));
            var crystal = crystalBatches.SelectMany(batch => Enumerable.Range(0, batch.Multimesh.InstanceCount)
                .Select(i => batch.GlobalTransform * batch.Multimesh.GetInstanceTransform(i)))
                .OrderByDescending(instance => instance.Basis.Y.LengthSquared()).First();
            Vector3 crystalTarget = crystal.Origin + Vector3.Up * 4;
            Vector3 outside = new Vector3(crystalTarget.X, 0, crystalTarget.Z).Normalized();
            StageCamera(crystalTarget + outside * 84 + outside.Cross(Vector3.Up) * 22 + Vector3.Up * 11, crystalTarget, 49);
            await SaveFrame(Path.Combine(folder, "Rynthid-Amethyst.png"));
        }

        ResetRace();
        countdown = 0;
        elapsed = video ? .98f : 1.85f;
        float start = redSite.Station - (video ? 76 : 40);
        float approach = redSite.Lane * .76f;
        float safeLane = -Mathf.Sign(redSite.Lane) * 5.9f;
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.Distance = start + i * (video ? 8.8f : 5.8f);
            racer.Lane = i == 0 ? approach : Mathf.Clamp((i - 2.5f) * 4.5f, -10.5f, 10.5f);
            racer.Speed = 23 + i * .5f;
            racer.GatesCleared = Enumerable.Range(0, track.GateCount).Count(gate => track.Gate(gate) <= racer.Distance);
            racer.Checkpoint = racer.GatesCleared > 0 ? track.Gate(racer.GatesCleared - 1) : 0;
            racer.Draw(track, .15f, true);
        }
        raceUi.Show();
        TickVoidBattle(0);
        HighlightCheckpoint();
        SetRaceCamera(1);
        UpdateRaceUi();
        if (video)
        {
            for (int frame = 0; frame < 128; frame++)
            {
                for (int tick = 0; tick < 2; tick++)
                {
                    const float step = 1f / 60;
                    float targetLane = elapsed < 1.55f ? approach : safeLane;
                    TickRace(step, true, false, Mathf.Clamp((targetLane - Player.Lane) * .3f, -.85f, .85f), false, false);
                    SetRaceCamera(1 - Mathf.Exp(-8.2f * step));
                    UpdateSpeedBlur(step);
                }
                UpdateRaceUi();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            Require(Player.Distance > redSite.Station + 35 && Player.EjectTime <= 0 && !Player.FreeDriving,
                $"the void capture dodges Aerbax’s marked lane and drives the energy road ({Player.Distance:F0} m)");
            PreviewVoidFinale();
            for (int frame = 0; frame < 213; frame++)
            {
                TickVoidFinale(1f / 30);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        else
        {
            await SaveFrame(Path.Combine(folder, "Rynthid-Race-Fallout.png"));
            PreviewVoidFinale();
            TickVoidFinale(2.15f);
            await SaveFrame(Path.Combine(folder, "Rynthid-Chains.png"));
            TickVoidFinale(3.9f);
            await SaveFrame(Path.Combine(folder, "Shard-of-the-Herald.png"));
        }
        GD.Print("RYNTHID SINGULARITY CAPTURE PASS");

        void PreviewBattle(float seconds)
        {
            elapsed = seconds;
            SetVoidGalaxyProgress(.72f);
            TickVoidSetpiece(1f / 30);
            TickVoidBattle(0);
        }

        void StageCamera(Vector3 position, Vector3 target, float fov)
        {
            camera.Far = 2900;
            camera.Fov = fov;
            camera.Position = position;
            camera.LookAt(target);
        }

        void PoseFlyover(float progress)
        {
            var sector = VoidFlyover(track);
            if (Player.Flight is null)
            {
                Player.Distance = sector.Start;
                Player.Lane = 0;
                Require(LaunchFlight(Player, sector), "the capture launches the Singularity Flyover");
            }
            var flight = Player.Flight!;
            Vector3 point = FlyoverPoint(sector, progress);
            flight.Position = point;
            flight.Heading = (FlyoverPoint(sector, progress + .01f) - point).Normalized();
            flight.Age = progress * 10;
            Player.Draw(track, 1f / 30, true);
            Vector3 outward = (point with { Y = 0 }).Normalized();
            StageCamera(point + outward * 27 - flight.Heading * 8 + Vector3.Up * 9,
                point - outward * 9 + flight.Heading * 2, 65);
        }
    }

    private async Task CaptureVoidLayouts(string folder)
    {
        foreach (int id in new[] { VoidFootworkCourseId, VoidAerbaxCourseId })
        {
            StartVoidCheck(id);
            SetPhysicsProcess(false);
            raceUi.Hide();
            ClearSpeedBlur();
            countdown = 0;
            bool footwork = id == VoidFootworkCourseId;
            var boss = footwork ? Vector3.Zero : VoidAerbaxPosition;
            Vector3 courseFocus = footwork ? new Vector3(-18, 18, 81) : boss + new Vector3(-32, 4, 125);
            Player.Distance = track.Project(courseFocus, track.Length * .42f).Distance;
            Player.Draw(track, .12f, true);
            Vector3 spot = track.Point(Player.Distance);
            Vector3 outward = (spot - boss) with { Y = 0 };
            camera.Far = 2900;
            camera.Fov = footwork ? 67 : 64;
            camera.Position = spot - track.Forward(Player.Distance) * 26 + outward.Normalized() * 28 + Vector3.Up * 15;
            camera.LookAt(spot.Lerp(boss, .65f) + Vector3.Up * (footwork ? 32 : 28));
            elapsed = VoidFireAt - .16f;
            SetVoidGalaxyProgress(.65f);
            TickVoidSetpiece(1f / 30);
            TickVoidBattle(0);
            // Cached reflections need time to capture and filter after switching layouts.
            for (int frame = 0; frame < 24; frame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await SaveFrame(Path.Combine(folder, footwork ? "Bael-Footwork.png" : "Aerbax-Orbit.png"));
        }
        GD.Print("VOID LAYOUT CAPTURE PASS");
    }

    private void PreviewVoidFinale()
    {
        StartVoidSealing(elapsed);
        BeginVoidFinaleCamera();
    }

    private void CheckVoidGuardianBlades()
    {
        var scene = voidAsheron!;
        DrawVoidAsheron(0, 0, 0);
        float mageHeight = MeasureVoidActorBody(scene.Asheron).Size.Y * scene.Asheron.Scale.Y;
        var heights = scene.Guardians.Select(guardian => MeasureVoidActorBody(guardian).Size.Y * guardian.Scale.Y).ToArray();
        var resting = scene.Blades.Select(blade => blade.Sparks.Position).ToArray();
        DrawVoidAsheron(.91f, .25f, 2.25f);
        for (int i = 0; i < scene.Blades.Length; i++)
        {
            var blade = scene.Blades[i];
            int hand = blade.Guardian.Setup.Holdings[1].Part;
            var bladePose = blade.Guardian.Transform * blade.Guardian.BodyPartTransform(hand) *
                blade.Sword.Transform * blade.Sword.BodyPartTransform(0);
            float heightRatio = heights[i] / mageHeight;
            float swordLength = blade.Sword.BodyMeshes.First().GetAabb().Size.Y * bladePose.Basis.Y.Length();
            Require(heightRatio is >= 3 and <= 4 && swordLength / heights[i] is >= .46f and <= .6f,
                $"the guardian towers over Asheron ({heightRatio:F2}x) with a one-handed sword ({swordLength / heights[i]:P0} of its height)");
            Require(blade.Sword.Setup.Id == GuardianGreatsword && blade.Guardian.HeldAttachmentMatchesNative(1) &&
                    blade.Guardian.LastActionStyle == VoidGuardianSwordStyle && blade.Guardian.LastActionKind == "melee-sword",
                "both Tower Guardians swing the native sword one-handed");
            Require(blade.Flames.Emitting && blade.Sparks.Emitting && !blade.Sparks.LocalCoords &&
                    blade.Tip.IsEqualApprox(bladePose * (Vector3.Up * VoidBladePoint)) &&
                    blade.Sparks.Position.DistanceTo(resting[i]) > 2,
                "the blade particles and binding channel follow the animated sword");
        }
        DrawVoidAsheron(0, 0, 0);
        Require(scene.Blades.All(blade => !blade.Sparks.Emitting), "restarting the stage resets the charge bursts");
    }

    private async Task CaptureVoidGuardians(string folder, bool video)
    {
        var stage = voidAsheron!.Root;
        raceUi.Hide();
        ClearSpeedBlur();
        SetVoidGalaxyProgress(.75f);
        camera.Far = 2900;
        camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.Size = 166 * VoidSideStageScale;
        const int frames = 145;
        for (int frame = 0; frame < frames; frame++)
        {
            float age = .42f + frame / 30f;
            elapsed = VoidVolleyPeriod / 3 + age;
            TickVoidBattle(0);
            camera.Position = stage.ToGlobal(new Vector3(-9 + frame * .13f, 76 - frame * .037f, -40));
            camera.LookAt(stage.ToGlobal(new Vector3(0, 68, 18)));
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!video && frame == 75)
            {
                await SaveFrame(Path.Combine(folder, "Tower-Guardians.png"));
                break;
            }
        }
        GD.Print("TOWER GUARDIANS CAPTURE PASS");
    }
}
