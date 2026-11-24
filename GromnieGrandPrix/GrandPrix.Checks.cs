using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private void RunSmokeTest()
    {
        try
        {
            if (OS.GetCmdlineUserArgs().Contains("--hybrid-only"))
            {
                CheckHybridAndSnowboard();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--island-only"))
            {
                CheckIslandRegatta();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--dismount-only"))
            {
                CheckDismountSprint();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--kaleidoscope-only"))
            {
                CheckKaleidoscopeCourse();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--jester-only"))
            {
                CheckJesterDescent();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--arcade-speed-only"))
            {
                CheckArcadeSpeed();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--soundtrack-only"))
            {
                CheckSoundtrackSettings();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--flight-controls-only"))
            {
                CheckFlightControls();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--touch-controls-only"))
            {
                CheckTouchControls();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--rabbit-only"))
            {
                CheckWhiteRabbitNavigation();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--void-only"))
            {
                CheckVoidCourse();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--presentation-only"))
            {
                CheckRacePresentation();
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--course-catalog-only"))
            {
                CheckCourseRoadCatalog();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--mounts-only"))
            {
                Require(loaded, "DAT load");
                CheckMountRoster();
                GD.Print($"MOUNT ROSTER PASS | {RaceMount.All.Length} equal-pace mounts and {RaceMount.ColorsFor(RaceMountKind.Siraluun).Count} Siraluun plumages.");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--mount-colors-only"))
            {
                CheckMountColors();
                QuitSoundtrackCheck();
                return;
            }
            const string onlineCheck = "--online-check=";
            if (OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(onlineCheck, StringComparison.Ordinal)) is { } check)
            {
                CheckOnline(check[onlineCheck.Length..]);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--loadouts-only"))
            {
                CheckRaceLoadouts();
                GetTree().Quit();
                return;
            }
            const string aceCheck = "--ace-rider-check=";
            const string nativeAceCheck = "--ace-native-check=";
            if (OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(nativeAceCheck, StringComparison.Ordinal)) is { } native)
            {
                CheckNativeAceImport(native[nativeAceCheck.Length..]);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--rider-templates-only"))
            {
                CheckRiderTemplates();
                QuitSoundtrackCheck();
                return;
            }
            const string trackCheck = "--track-editor-check=";
            var editorCheck = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(trackCheck, StringComparison.Ordinal));
            if (editorCheck is not null)
            {
                CheckTrackEditor(editorCheck[trackCheck.Length..]);
                GetTree().Quit();
                return;
            }
            var bridgeCheck = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(aceCheck, StringComparison.Ordinal));
            if (bridgeCheck is not null)
            {
                CheckAceRiderImport(bridgeCheck[aceCheck.Length..]);
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--span-only"))
            {
                CheckSpanSprint();
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--encounters-only"))
            {
                const string option = "--encounter-course=";
                var name = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(option, StringComparison.Ordinal))?[option.Length..];
                StartEncounterCheckCourse(name);
                if (name is null) CheckRaceEncounters();
                else CheckOlthoiCrossingArt();
                GetTree().Quit();
                return;
            }
            string[] args = OS.GetCmdlineUserArgs();
            string? landmarkName = args.FirstOrDefault(arg => arg.StartsWith("--landmark=", StringComparison.Ordinal))?["--landmark=".Length..];
            if (args.Contains("--landmarks-only") || landmarkName is not null)
            {
                CheckLandmarkRaces(landmarkName);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--citadel-only"))
            {
                StartCitadelCheckRace();
                CheckCitadelBattle();
                GetTree().Quit();
                return;
            }
            Require(loaded, "DAT load");
            CheckDatUiFonts(false);
            Require(skins.Length == 8, "palette catalog");
            Require(townDestinations.Count == 50 && mediumRoutes.Count == 45, "all outdoor town courses have regional layouts");
            PopulateCoursePicker("");
            var picked = Enumerable.Range(0, courseList.ItemCount).Select(i => courseList.GetItemMetadata(i).AsInt32()).ToArray();
            Require(spanSprint is not null && picked.Length == 59 + VoidCourses.Count +
                (DismountCourseAvailable ? 1 : 0) + (IslandWaterCourseAvailable ? 1 : 0) &&
                picked.Contains(spanSprint.Id) && VoidCourses.Keys.All(picked.Contains) &&
                landmarkCircuits.Keys.All(picked.Contains) && !picked.Contains(0) && !picked.Contains(72),
                "the Span sprint and the full regional catalog appear in the picker");
                        Require(track.Length > 600 && track.Point(0).DistanceTo(track.Point(track.Length - .01f)) < .1f
                && track.TightestInsideRadius() >= BogCircuit.MinimumInsideRadius, "expanded Muckwater with sweeping bends");
            var scenery = track.Scenery ?? throw new InvalidOperationException("The game fell back to procedural scenery.");
            Require(scenery.CoursePropCount >= 200 && scenery.CourseModelCount >= 15, "native props and scenery variety");
            Require(scenery.MinimumCourseClearance >= track.HalfWidth + 2.05f, "clear driveable surface");
            Require(garage.HasNode("DAT stable scenery"), "native stable scenery");
            CheckGroundBlend();
            CheckWeather();
            CheckObsidianRaces();
            CheckLandmarkRaces();
            StartRace();
            OpenStable();
            Require(assets.FailedTextures == 0, "DAT texture decodes");
            GD.Print($"DAT scenery: {scenery.CoursePropCount} instances of {scenery.CourseModelCount} native models, {scenery.MinimumCourseClearance:F2} m minimum ground-prop clearance.");
            var fingerprints = new HashSet<string>();
            for (int i = 0; i < skins.Length; i++)
            {
                SelectSkin(i);
                var racer = showcase!;
                Require(racer.Mount.BodyMeshes.Count() == 21, "native mount geometry");
                Require(racer.Mount.AppearanceWarnings.Count == 0, $"native palette {skins[i].Name}");
                var signature = string.Join(",", Enumerable.Range(0, 21).Select(racer.Mount.PartTextureFingerprint));
                GD.Print($"Palette {skins[i].Name}: colors={racer.Mount.NativePaletteColorCount}, distinct={fingerprints.Add(signature)}, torso={racer.Mount.PartTextureFingerprint(0)}");
            }
            Require(fingerprints.Count == skins.Length, "visually distinct native palettes");
            foreach (var heritage in creation.Rows)
                foreach (var body in heritage.Genders)
                {
                    appearance = new CharacterAppearance
                    {
                        NativeHeritageId = heritage.Id, GenderId = body.Id,
                        HairStyle = Math.Max(0, body.HairStyles.Count - 1),
                        ShirtStyle = Math.Max(0, body.Shirts.Count - 1)
                    };
                    RebuildShowcase();
                    Require(showcase!.Rider.BodyMeshes.Any(), $"native rider {heritage.Name}/{body.Name}");
                }
            appearance = new CharacterAppearance();
            SelectSkin(0);
            FillStable();
            for (int i = 0; i < RaceWeapon.All.Length; i++)
            {
                weaponIndex = i;
                RebuildShowcase();
                Require(showcase!.Rider.HasVisibleHeldMesh && showcase.Rider.HeldSetup == RaceWeapon.All[i].Setup,
                    $"mounted weapon {RaceWeapon.All[i].Name}");
            }
            weaponIndex = 0;
            RebuildShowcase();
            CheckMountRoster();
            CheckSurfaceRelief();
            CheckRaceCamera();
            CheckRoadsideLife();
            StartRace();
            Require(racers.Skip(1).Take(3).Select(r => r.Distance).Distinct().Count() == 1 &&
                racers.Skip(1).Take(3).Select(r => r.Lane).Distinct().Count() == 3, "three-wide starting grid");
            SetPaused(true);
            Require(pauseUi.Visible, "pause");
            SetPaused(false);
            const float step = 1f / 60;
            for (int i = 0; i < 190; i++) TickRace(step, true, false, 0, false, false);
            var riderPosition = Player.Saddle.Position;
            bool sawDust = false;
            for (int i = 0; i < 65; i++)
            {
                TickRace(step, true, false, 0, false, false);
                sawDust |= dust.Count > 0;
            }
            Require(Player.Mount.CurrentCommand is 0x44000007 or 0x45000005 && Player.Mount.PoseChanges > 0, "native locomotion");
            Require(Player.Saddle.Position.DistanceTo(riderPosition) > .001f, "saddle tracks native torso pose");
            Require(sawDust, "running claws emit surface dust");
            Require(SpeedBlurStrength(0, false, false) == 0 && SpeedBlurStrength(27, true, false) is > .6f and < 1,
                "subtle speed blur ramps up with speed and boost");
            Player.Speed = 16;
            for (int i = 0; i < 48; i++) TickRace(step, true, false, .55f, true, false);
            TickRace(step, true, false, 0, false, false);
            Require(Player.Boost > .5f, "drift release boost");

            Require(pickupItems.Distinct().Count() == pickupItems.Length && pickupItems.All(i => ItemIcon(i) is not null)
                && RacePower.All.All(power => pickupItems.Contains(power.Item)), "all rotating DAT pickup icons load");
            var pickup = pickups[1];
            RaceItem oldItem = pickup.Displayed;
            UpdatePickupVisuals(elapsed + PickupCycleSeconds);
            Require(pickup.Displayed != oldItem && pickup.Icon.Texture == ItemIcon(pickup.Displayed), "pickup icon cycles");
            UpdatePickupVisuals(elapsed);
            RaceItem displayed = pickup.Displayed;
            Player.Distance = pickup.Distance - .1f;
            Player.Lane = pickup.Lane;
            TickRace(step, true, false, 0, false, false);
            Require(Player.Item == displayed && !pickup.Node.Visible, "collect the displayed item and hide the icon during cooldown");
            Player.Item = RaceItem.Pepper;
            TickRace(step, true, false, 0, false, true);
            Require(Player.Item == RaceItem.None && Player.Boost > 2, "pepper boost");
            Player.Item = RaceItem.MudPie;
            UseItem(Player);
            Require(mud.Any(hazard => hazard.Owner == Player), "mud hazard");
            racers[1].Distance = Player.Distance;
            racers[1].Lane = Player.Lane;
            Player.Item = RaceItem.Burp;
            UseItem(Player);
            Require(racers[1].Slowed > 0, "battle burp");

            CheckCombat();
            CheckRaceViews();

            CheckShortcuts();
            for (int i = 0; i < track.Length * 16 && !finished; i++)
            {
                float steer = Mathf.Clamp(-Player.Lane * .13f, -1, 1);
                TickRace(step, true, false, steer, false, false);
            }
            Require(finished && lap == 3 && resultsUi.Visible && Player.Finish > 0 && best > 0, "three laps and result");
            StartRace();
            Require(!finished && lap == 1 && mud.Count == 0 && countdown == 3, "retry reset");
            OpenStable();
            Require(!racing && stableUi.Visible && racers.Count == 0, "return to stable");
            int mediumId = mediumRoutes.Keys.First();
            ChooseCourse(mediumId);
            StartRace();
            Require(activeTown == mediumId && track.IsMedium && track.HalfWidth >= 11 && track.NativeBlockCount == 49 &&
                track.Length > 4250 && track.Laps == 1 && track.GateCount == 12 && pickups.Count >= 57 && pads.Count >= 14,
                "medium course loads 49 native blocks and a kilometer-scale racing lap");
            CheckRaceEncounters();
            CheckRivalLearning();
            const float sampleDistance = BogCircuit.Grid + 23;
            Player.Distance = sampleDistance;
            Player.Lane = track.HalfWidth * .52f;
            Player.Draw(track, 0, false);
            SetRaceCamera(1);
            for (int sample = 1; sample < 14; sample++)
            {
                Vector3 ray = camera.Position.Lerp(Player.Root.Position + Vector3.Up * .58f, sample / 14f);
                Require(ray.Y >= track.FreeGround(ray).Y + .295f, "chase camera clears native ridges");
            }
            ResetRace();
            for (int i = 0; i < track.Length * 6 && !finished; i++)
                TickRace(step, Player.SudsedUntil <= elapsed, Player.SudsedUntil > elapsed,
                    Mathf.Clamp(-Player.Lane * (Player.SudsedUntil > elapsed ? -.25f : .25f), -1, 1), false, false);
            Require(finished && lap == 1 && Player.GatesCleared == 13 && bestByCourse.ContainsKey(mediumId) && bestByCourse.ContainsKey(0),
                "the medium course finishes through all 12 checkpoints and records a best time");
            OpenStable();
            ChooseCourse(0);
            StartRace();
            Require(activeTown == 0 && track == muckTrack && pickups.Count == 9 && pads.Count == 3,
                "course change clears native race state");
            OpenStable();
            CheckXlRaces();
            StartRace();
            CheckWhiteRabbit();
            CheckFlightPickup(RaceItem.Gromnatross);
            if (creation.OlthoiSpitter is not null) CheckFlightPickup(RaceItem.OlthoiLift);
            CheckRacePowers();
            CheckUlgrimFolly();
            CheckRivalTactics();
            CheckSpanSprint();
            GD.Print($"GROMNIE GRAND PRIX PASS | {townDestinations.Count} native courses | {RaceMount.All.Length} equal-pace mounts | cycling pickups, foot dust, XL flyovers, flight pickups, White Rabbit, free steering and enforced gates | {track.Length:F1} m circuit | {fingerprints.Count} distinct native palettes | {creation.Rows.Count} rider heritages | 3 laps, items, pause, retry");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private async void CheckCourseRoadCatalog()
    {
        try
        {
            Require(loaded, "DAT load for the course catalog");
            int[] ids = mediumRoutes.Keys.Concat(xlRoutes.Keys).Concat(ridgeCircuits.Keys)
                .Concat(landmarkCircuits.Keys).Append(spanSprint?.Id ?? -1).ToArray();
            Require(ids.Length == 59 && spanSprint is not null, "the entire built-in course catalog is available");
            int passed = 0;
            foreach (int id in ids)
            {
                try
                {
                    ChooseCourse(id);
                    StartRace();
                    Require(racing && activeTown == id && track.HasGradedRoad, $"the dirt road loads ({courseProblem})");
                    var road = course.GetNode<Node3D>("Terrain-following road");
                    if (AndroidGraphics)
                    {
                        var roadMeshes = road.GetChildren().OfType<MeshInstance3D>().ToArray();
                        Require(roadMeshes.Length >= 5 && roadMeshes.All(mesh => mesh.Mesh.GetSurfaceCount() == 1),
                            "the road, shoulders and verges have valid cullable meshes");
                    }
                    else
                        Require(road.GetChildCount() == 5 && road.GetNode<MeshInstance3D>("Continuous dirt road").Mesh.GetSurfaceCount() == 1,
                            "the full road, shoulders and verges are rendered");
                    for (int i = 0; i <= 12; i++)
                    {
                        float at = track.Length * i / 12;
                        Vector3 point = track.Surface(at, track.WidthAt(at) * .61f);
                        Require(Math.Abs(track.GroundOrRoadHeight(point) - point.Y) < .12f,
                            $"rendering and racer contact agree at {at:F0} m");
                    }
                    passed++;
                }
                catch (Exception error)
                {
                    GD.PrintErr($"COURSE ROAD FAILED | {CourseName(id)} | {error.Message}");
                }
                OpenStable();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            GD.Print($"COURSE ROAD CATALOG | {passed}/{ids.Length} courses loaded and checked.");
            GetTree().Quit(passed == ids.Length ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private void CheckMountRoster()
    {
        Require(mountPicker.ItemCount == RaceMount.All.Length, "every mount is selectable in the stable");
        Vector3? referencePace = null;
        var boostSounds = new HashSet<AudioStream>();
        foreach (var rig in RaceMount.All)
        {
            SelectMount(rig.Kind);
            Require(showcase!.Rig == rig && mountPicker.GetSelectedId() == (int)rig.Kind &&
                swatches.Count(swatch => swatch.Visible) == RaceMount.ColorsFor(rig.Kind).Count,
                "stable preview and palette controls match the chosen mount");
            StartRace();
            Require(Player.Rig == rig && Player.Mount.Setup.Id == rig.ResolveLook(GromnieLook(skinIndex), SelectedMountColor).Setup &&
                Player.Mount.AppearanceWarnings.Count == 0 && racers.Select(racer => racer.Rig.Kind).Distinct().Count() == RacerCount,
                $"native {rig.Name} enters a mixed grid");
            Require(BoostSound(rig.Kind, 0) is { } sound && sound.GetLength() > 0 && boostSounds.Add(sound),
                $"{rig.Name} has its own playable boost clip");
            int played = raceAudio!.Played;
            GrantBoost(Player, 1);
            GrantBoost(Player, 1.2f);
            Require(raceAudio.Played == played + 1 && Player.Boost == 1.2f, "overlapping boosts extend time and debounce the sound");
            var storm = Player.Mount.GetNodeOrNull<CpuParticles3D>("Crimson Storm");
            if (rig.Kind == RaceMountKind.IsinDuleDisc)
            {
                Player.Draw(track, 1f / 60, true);
                Require(storm is { Emitting: true, LocalCoords: true, SpeedScale: > 1 } &&
                    storm.Mesh == assets.Mesh(0x01001cb9, particle: true) && storm.Position.Y < 0 &&
                    storm.EmissionNormals.All(direction => direction.Y < 0),
                    "the boosted disc emits its native Crimson Storm particles outward and down");
            }
            else Require(storm is null, "Crimson Storm belongs to the mage disc");
            Player.Boost = 0;

            Player.Distance = 34;
            Player.Lane = -.4f;
            int poses = Player.Mount.PoseChanges;
            var fur = Player.Mount.FindChildren("Tusker Fur *", nameof(MeshInstance3D), true, false)
                .OfType<MeshInstance3D>().ToArray();
            bool shouldHaveFur = rig.Kind is RaceMountKind.Tusker or RaceMountKind.SnowTusker;
            Require(shouldHaveFur ? fur.Length > 0 && fur.All(patch => patch.GetParent() is MeshInstance3D) : fur.Length == 0,
                $"{rig.Name} has the expected coat attached to native body meshes");
            Basis[] restingFur = fur.Select(patch => patch.Basis).ToArray();
            bool pawDust = false;
            bool separateStrides = false;
            float saddleUp = 1;
            float lowestHover = float.PositiveInfinity, highestHover = 0;
            for (int i = 0; i < 124; i++)
            {
                const float dt = 1f / 60;
                MovePlayer(dt, true, false, .16f, false);
                Player.Draw(track, dt, true);
                saddleUp = Math.Min(saddleUp, Player.Saddle.Basis.Y.Dot(Vector3.Up));
                dust.SampleFeet(Player, track, false);
                dust.Tick(dt);
                pawDust |= dust.Count > 0;
                if (rig.Kind == RaceMountKind.BaelZharon) CheckFrontCarry(Player);
                if (Player.CompanionSkate is { } mate)
                    separateStrides |= !mate.Part(7).Position.IsEqualApprox(Player.Mount.Part(7).Position);
                lowestHover = Math.Min(lowestHover, Player.Mount.Position.Y);
                highestHover = Math.Max(highestHover, Player.Mount.Position.Y);
            }
            var pace = new Vector3(Player.Distance, Player.Speed, Player.Lane);
            if (shouldHaveFur)
                Require(fur.All(patch => patch.Basis.IsFinite()) && fur.Where((patch, index) => patch.Basis != restingFur[index]).Any(),
                    $"{rig.Name} has a moving coat while sprinting");
            referencePace ??= pace;
            Require(pace.IsEqualApprox(referencePace.Value), $"{rig.Name} matches the other mounts' pace and handling");
            if (rig.Platform == MountPlatform.None)
                Require(Player.Mount.CurrentCommand == 0x44000007 && Player.Mount.PoseChanges > poses, $"{rig.Name} uses its own run cycle");
            else
                Require(lowestHover > .45f && highestHover - lowestHover > .09f && Player.Saddle.Position.IsZeroApprox(),
                    $"{rig.Name} hovers with the rider secured to the deck");
            Require((Player.DustContact.Length == 0 || pawDust) && Player.Saddle.GlobalPosition.IsFinite(),
                $"{rig.Name} has a stable rider anchor and dust at any ground contacts");
            Require(rig.SaddleFollow == 1 || saddleUp > .68f, $"{rig.Name} keeps the saddle upright while sprinting");
            if (Player.CompanionSkate is { } skate)
            {
                Require(skate.AppearanceWarnings.Count == 0 && skate.CurrentCommand == 0x44000007 &&
                    skate.PoseChanges > 0 && separateStrides && Player.DustContact.Length == 8,
                    "both Carenzi use independent native run cycles and paw contacts");
                CheckSkateBoots(Player, skate);
            }
            float scale = Player.Rider.Scale.Y;
            Require(LaunchFlight(Player), $"{rig.Name} can swap to a flying mount");
            Require(storm is null || !storm.IsVisibleInTree(), "the disc's plume hides with the disc in flight");
            Require(Player.CompanionSkate is null || !Player.CompanionSkate.Visible, "both skates hide during flight");
            TickFlight(Player, Player.Flight!, 1f / 60, 0);
            Require(Player.Rider.GetParent() == Player.Flight!.Flyer.RiderAnchor, "flying rider follows talon grip");
            RecallRacer(Player, "BACK AT THE CHECKPOINT");
            Require(Player.Flight is null && Player.Mount.Visible && Player.Rider.HasVisibleHeldMesh &&
                Mathf.IsEqualApprox(Player.Rider.Scale.Y, scale), $"{rig.Name} restores the correct mount and rider scale after recall");
            Require(storm is null || storm.IsVisibleInTree(), "the disc's plume returns with the disc");
            if (rig.Kind == RaceMountKind.BaelZharon) CheckFrontCarry(Player);
            if (rig.Platform != MountPlatform.None)
                Require(Player.Rider.CurrentCommand == (rig.Platform == MountPlatform.Carpet ? 0x4300013eu : 0x41000003u),
                    $"{rig.Name} restores its own rider pose after a flight pickup");
            if (Player.CompanionSkate is { } restored)
            {
                Require(restored.Visible, "both skates return from flight");
                Player.Eject(track, Player.Right(track));
                Player.Draw(track, .15f, false);
                Require(Player.Rider.TopLevel && restored.GlobalPosition.DistanceTo(Player.Root.GlobalPosition) < 2,
                    "knockoffs leave both skates on the track");
                RecallRacer(Player, "BACK AT THE CHECKPOINT");
                CheckSkateBoots(Player, restored);
            }
            OpenStable();
        }
        CheckSiraluunColors();
    }

    private void CheckSiraluunColors()
    {
        int gromnieSkin = skinIndex;
        var fingerprints = new HashSet<string>();
        SelectMount(RaceMountKind.Siraluun);
        for (int i = 0; i < RaceMount.SiraluunColors.Length; i++)
        {
            SelectSkin(i);
            var variant = RaceMount.SiraluunColors[i];
            var bird = showcase!.Mount;
            string signature = string.Join(",", Enumerable.Range(0, bird.Setup.Parts.Length).Select(bird.PartTextureFingerprint));
            Require(bird.NativeAppearanceId == variant.Look.Id && bird.AppearanceWarnings.Count == 0 && fingerprints.Add(signature) &&
                stableName.Text == variant.Name && swatches[i].Text == "✓", $"{variant.Name} has its own native plumage");
        }
        int last = SelectedMountColor;
        StartRace();
        Require(Player.Mount.NativeAppearanceId == RaceMount.SiraluunColors[last].Look.Id, "a selected Siraluun color follows the rider onto the course");
        OpenStable();
        SelectMount(RaceMountKind.Gromnie);
        Require(SelectedMountColor == gromnieSkin, "the Siraluun picker keeps the Gromnie color separate");
    }

    private static void CheckSkateBoots(MountedRacer racer, AcActor companion)
    {
        Node3D left = racer.Mount.Part(1).GetNode<Node3D>("Boot pad");
        Node3D right = companion.Part(1).GetNode<Node3D>("Boot pad");
        Vector3 leftBoot = (racer.Rider.Part(3).GlobalPosition + racer.Rider.Part(4).GlobalPosition) * .5f;
        Vector3 rightBoot = (racer.Rider.Part(7).GlobalPosition + racer.Rider.Part(8).GlobalPosition) * .5f;
        Require(left.GlobalPosition.DistanceTo(leftBoot) < .23f && right.GlobalPosition.DistanceTo(rightBoot) < .23f &&
            left.GlobalPosition.DistanceTo(right.GlobalPosition) > .5f, "the rider has one Carenzi under each boot");
    }

    private static void CheckFrontCarry(MountedRacer racer)
    {
        Vector3 hip = racer.Mount.ToLocal(racer.Rider.ToGlobal(racer.Rider.BodyPartTransform(0).Origin));
        Vector3 chest = racer.Mount.BodyPartTransform(9).Origin;
        Vector3 left = racer.Mount.BodyPartTransform(12).Origin, right = racer.Mount.BodyPartTransform(15).Origin;
        Require(hip.IsFinite() && hip.Z < chest.Z - .30f && left.X < hip.X && right.X > hip.X &&
            left.DistanceTo(hip) < .27f && right.DistanceTo(hip) < .27f, "Bael’Zharon holds the passenger in front with both hands");
    }

    private void CheckRoadsideLife()
    {
        Require(AsheronLines.All(line => raceAudio!.Dialogue(line)?.GetLength() > 1), "original Asheron boost banter loads");
        StartRace();
        Require(roadsideSites.Select(site => site.Kind).Distinct().Count() == 3 && roadHazards.Count > 0,
            "crowds, dancers, duels and native hazards staged on Muckwater");
        int crowdSounds = raceAudio!.Played;
        foreach (var kind in Enum.GetValues<Spectacle>())
        {
            var site = roadsideSites.First(row => row.Kind == kind);
            Player.Distance = site.Distance;
            nextCrowdScan = 0;
            var commands = new HashSet<uint>();
            for (int i = 0; i < 45; i++)
            {
                elapsed += .05f;
                TickRoadside(.05f);
                foreach (var performer in site.Cast) commands.Add(performer.Actor.CurrentCommand);
            }
            Require(site.Cast.Count >= 4 && kind switch
            {
                Spectacle.Cheer => commands.Contains(CheerEmote) || commands.Contains(ClapEmote),
                Spectacle.Dance => commands.Contains(YmcaEmote) && commands.Contains(DanceEmote),
                _ => site.Cast.Count(performer => performer.Emote == 0) == 2 && commands.Any(command => command is 0x10000062 or 0x10000063 or 0x10000064)
            }, $"native {kind} animation plays beside the track");
        }

        var hazard = roadHazards[0];
        hazard.ActiveUntil = 0;
        hazard.PulseAt = elapsed + HazardWarningTime;
        TickRoadHazards(hazard.Center);
        Require(hazard.Marker.MaterialOverride == hazardWarning && hazard.ActiveUntil <= elapsed, "hazard telegraphs before firing");
        elapsed += HazardWarningTime;
        int scripts = itemEffects!.PlayedScripts;
        TickRoadHazards(hazard.Center);
        Require(itemEffects.PlayedScripts == scripts + 1 && hazard.ActiveUntil > elapsed, "hazard triggers its original DAT effect");
        Player.Speed = 20;
        var span = Vector3.Right * (hazard.Radius + 1);
        CheckRoadHazards(Player, 0, hazard.Center - span, hazard.Center + span);
        float speed = Player.Speed;
        CheckRoadHazards(Player, 0, hazard.Center - span, hazard.Center + span);
        Require(speed < 20 && Player.Slowed > 0 && speed == Player.Speed, "swept hazard contact applies a single slowdown per pulse");
        Require(raceAudio.Played > crowdSounds, "roadside crowds react audibly as racers pass");
        OpenStable();
    }

    private void CheckRaceCamera()
    {
        StartRace();
        camera.Basis = camera.Basis.Scaled(Vector3.One * 1.001f);
        _Input(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        for (int frame = 0; frame < 1800; frame++)
            _PhysicsProcess(1d / 60);
        pressed.Clear();
        Require(camera.Basis.GetRotationQuaternion().IsNormalized() && camera.Basis.Scale.IsEqualApprox(Vector3.One),
            "race camera removes scale drift and stays normalized across physics frames");
        AimRaceCamera(camera.Position + Vector3.Up, 1);
        Require((-camera.Basis.Z).IsEqualApprox(Vector3.Up), "race camera can aim straight up");
        OpenStable();
    }

    private void CheckXlRaces()
    {
        Require(xlRoutes.Count == 5, "all five winding XL courses available");
        int first = YanshiXlCourseId;
        if (!xlRoutes.ContainsKey(first)) first = xlRoutes.Keys.First();
        foreach (int id in xlRoutes.Keys.Order())
        {
            ChooseCourse(id);
            StartRace();
            Require(activeTown == id && track.IsXL && track.NativeBlockCount == 64, "XL loads 64 native landblocks");
            Require(course.GetChildren().OfType<Node3D>().Count(node => node.Name.ToString().StartsWith("Landblock_")) == 64,
                "all 64 landblocks have scene meshes");
            Require(track.NativeBounds.Size.IsEqualApprox(new Vector2(1536, 1536)) && track.Length > 6400 &&
                track.Laps == 1 && track.GateCount == 16, "kilometer-scale XL route and checkpoint rules");
            Require(pickups.Count >= 45 && pads.Count >= 11, "pickups spread across the XL lap");
            if (flyover is { } route) CheckFlyover(route);
            bool outsideOriginalGrid = false;
            for (float at = 0; at < track.Length; at += 38)
            {
                var point = track.Point(at);
                if (Math.Abs(point.X) < 320 && Math.Abs(point.Z) < 320) continue;
                if (!track.TryFreeMove(point, track.Point(at + .2f), out _)) continue;
                outsideOriginalGrid = true;
                break;
            }
            Require(outsideOriginalGrid, "ground and movement resolved beyond the old 3×3 bounds");
            if (id == first)
            {
                for (int tick = 0; tick < track.Length * 6 && !finished; tick++)
                {
                    TickRace(1f / 60, true, false, Mathf.Clamp(-Player.Lane * .22f, -1, 1), false, false);
                    SetRaceCamera(1 - Mathf.Exp(-8.2f / 60));
                }
                Require(finished && Player.GatesCleared == track.GateCount + 1 && bestByCourse.ContainsKey(id), "completed an XL race through all gates");
            }
            OpenStable();
        }
        ChooseCourse(0);
        StartRace();
        Require(track == muckTrack && track.GateCount == 4 && pickups.Count == 9 && flyover is null, "old course works after the XL grids");
        OpenStable();
    }

    private void CheckFlyover(Flyover route)
    {
        int gate = FlightGate(route);
        Require(route.Start > track.Gate(gate - 1) && route.End < track.Gate(gate), "flyover rejoins before the next checkpoint");
        PutPlayerAtLaunch(route);
        TickRace(1f / 60, true, false, 0, false, false);
        var flight = Player.Flight;
        Require(flight is not null && Player.Rider.GetParent() == flight.Flyer.RiderAnchor && !Player.Mount.Visible,
            "the launch pad transfers the rider to the flyer");
        if (route.Direct && creation.OlthoiSpitter is { } rig)
        {
            var spitter = flight!.Flyer as OlthoiSpitter ?? throw new InvalidOperationException("The summit launch needs an Olthoi Spitter.");
            Require(spitter.Body.Setup.Id == rig.Setup && spitter.Body.AppearanceWarnings.Count == 0 && spitter.Body.BodyMeshes.Count() == 31,
                "the summit Spitter loads the complete native appearance");
            int[] wingParts = [3, 4, 11, 12];
            var wingPoses = wingParts.Select(spitter.Body.BodyPartTransform).ToArray();
            for (int frame = 0; frame < 10; frame++) TickRace(1f / 60, true, false, .35f, false, false);
            Require(wingParts.Select((part, i) => spitter.Body.BodyPartTransform(part) != wingPoses[i]).All(changed => changed),
                "all four of the native Spitter wings animate on the summit flight");
            CheckSpitterSeat(spitter);
        }
        else Require(flight!.Flyer is Gromnatross, "the feather launch uses the Gromnatross");
        float highest = 0;
        int frames = 0;
        while (Player.Flight is not null && frames++ < 1000)
        {
            TickRace(1f / 60, true, false, .35f, false, false);
            var p = Player.CoursePosition(track);
            highest = Math.Max(highest, p.Y - track.FreeGround(p).Y);
        }
        Require(highest > (route.Direct ? 80 : 45) && Player.Flight is null && Player.Distance >= route.End && Player.Mount.Visible && Player.GatesCleared == gate,
            "scenic flight climbs, banks and lands without skipping a gate");
        ResetRace();
    }

    private int FlightGate(Flyover route) => Enumerable.Range(1, track.GateCount).First(gate => track.Gate(gate) > route.End);

    private void PutPlayerAtLaunch(Flyover route)
    {
        countdown = 0;
        Player.Distance = route.Start - .15f;
        Player.GatesCleared = FlightGate(route);
        Player.Checkpoint = track.Gate(Player.GatesCleared - 1);
        Player.Lane = 0;
        Player.Speed = 23;
        Player.Draw(track, 0, true);
    }

    private void CheckObsidianRaces()
    {
        Require(ridgeCircuits.Count == 2, "two Obsidian Plains circuits available");
        foreach (var ridge in ridgeCircuits.Values)
        {
            ChooseCourse(ridge.Id);
            StartRace();
            Require(activeTown == ridge.Id && track.NativeBlockCount == 64 && flyover is { Direct: true }, "native Black Hills course and summit flight load");
            var route = flyover!.Value;
            CheckFlyover(route);
            int gate = FlightGate(route);
            PutPlayerAtLaunch(route);
            Player.Lane = track.HalfWidth * .62f;
            TickRace(1f / 60, true, false, 0, false, false);
            Require(Player.Distance > route.Start && Player.Flight is null, "ground lane can pass the summit launch");
            for (int i = 0; i < 7400 && Player.GatesCleared == gate; i++)
                TickRace(1f / 60, true, false, Mathf.Clamp((track.HalfWidth * .64f - Player.Lane) * .32f, -.70f, .70f), false, false);
            Require(Player.Flight is null && Player.GatesCleared > gate, "ground detour reaches the checkpoint beyond the landing");
            OpenStable();
        }
        ChooseCourse(0);
    }

    private void CheckFlightPickup(RaceItem item)
    {
        countdown = 0;
        Player.Distance = track.Gate(0) - 2;
        Player.Lane = 0;
        Player.Speed = 18;
        Player.Item = item;
        _Input(new InputEventKey { PhysicalKeycode = Key.F, Pressed = true });
        TickRace(1f / 60, true, false, 0, false, useItem);
        Require(Player.Flight is not null && Player.Item == RaceItem.None, "F consumes the flight pickup");
        Require(item == RaceItem.OlthoiLift ? Player.Flight!.Flyer is OlthoiSpitter : Player.Flight!.Flyer is Gromnatross,
            $"{ItemName(item)} summons the matching flight mount");
        if (Player.Flight!.Flyer is OlthoiSpitter spitter) CheckSpitterSeat(spitter);
        else Require(Player.Rider.CurrentCommand == 0x40000015, "Gromnatross Lift plays the native falling motion");
        for (int i = 0; i < 300 && Player.Flight is not null; i++)
            TickRace(1f / 60, true, false, .3f, false, false);
        Require(Player.Flight is null && Player.GatesCleared == 1 && Player.Mount.Visible && Player.Rider.HasVisibleHeldMesh,
            "pickup flight crosses the start gate and restores mounted gear on landing");
        pressed.Clear();
        useItem = false;
        ResetRace();
    }

    private void CheckSpitterSeat(OlthoiSpitter spitter)
    {
        Vector3 hip = Player.Rider.Transform * Player.Rider.BodyPartTransform(0).Origin;
        Vector3 saddle = spitter.Body.Transform.AffineInverse() * spitter.RiderAnchor.Position;
        Require(Player.Rider.GetParent() == spitter.RiderAnchor && Player.Rider.CurrentCommand is 0x4300013d or 0x41000013
            && hip.IsEqualApprox(new Vector3(0, .063f, .025f)) && saddle.Y is > 3.05f and < 3.27f,
            "the rider straddles the saddle at the Spitter's back surface");
    }

    private void CheckWhiteRabbit()
    {
        for (int index = 0; index < racers.Count; index++)
        {
            var racer = racers[index];
            racer.GatesCleared = 1;
            racer.Checkpoint = track.Gate(0);
            racer.Distance = index == 0 ? 60 : 66 + index * 9;
            racer.Lane = 0;
            racer.Draw(track, 0, false);
        }
        var second = racers[1];
        Player.Item = RaceItem.WhiteRabbit;
        UseItem(Player);
        var chase = rabbits.Single();
        Require(chase.Target == second && chase.Orb.BodyMeshes.Any() && !chase.Rabbit.Visible, "White Rabbit chooses the next position and summons the native orb");
        var drivingCamera = camera.Transform;
        var summonCamera = rabbitCamera.Position;
        Require(filmedRabbit == chase && rabbitCameraPanel.Visible && rabbitViewport.FindWorld3D() == GetViewport().FindWorld3D(),
            "the orb opens a live camera in the race world");
        SetPaused(true);
        Require(!rabbitCameraPanel.Visible && rabbitViewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
            "pause stops rendering the rabbit cam");
        SetPaused(false);
        Require(rabbitCameraPanel.Visible, "the rabbit cam returns on resume");
        for (int i = 0; i < 30; i++) TickItemEffects(1f / 60);
        Require(chase.Rabbit.Visible && chase.Rabbit.NativePaletteColorCount > 0 && chase.Rabbit.PoseChanges > 0 && chase.Distance > Player.Distance,
            "animated White Rabbit runs ahead");
        Require(summonCamera.DistanceTo(rabbitCamera.Position) > 1 && camera.Transform == drivingCamera,
            "the inset follows the rabbit independently from the driving view");
        for (int i = 0; i < 180 && rabbits.Count > 0; i++) TickItemEffects(1f / 60);
        Require(chase.Rabbit.LastActionKind == "creature-attack" && second.EjectTime > 0 && second.Stability == 0,
            "White Rabbit bites and Bunny Smite unseats the rival");
        var replay = rabbitReplay;
        Require(filmedRabbit is null && rabbitCameraPanel.Visible && replay is { LiveMeshes.Count: > 0 } &&
            (camera.CullMask & RabbitReplayLayer) == 0 && (rabbitCamera.CullMask & RaceOnlyLayer) == 0,
            "Bunny Smite opens a separate slow-motion rider in the inset");
        float liveTime = second.EjectTime;
        TickItemEffects(.50f);
        Require(Mathf.IsEqualApprox(replay!.Age, .15f) && replay.Rider.GlobalPosition.DistanceTo(replay.Motion.Launch.Origin) > .45f &&
            second.EjectTime == liveTime && Engine.TimeScale == 1, "only the replay runs in slow motion");
        TickItemEffects(1.75f);
        Require(rabbitReplay is null && !rabbitCameraPanel.Visible && rabbitViewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled &&
            replay.LiveMeshes.All(entry => entry.Mesh.Layers == entry.Layers), "replay closes and restores the rival's visual layers");

        Player.Distance = 114;
        var runnerUp = racers[^1];
        runnerUp.Ward = 12;
        Player.Item = RaceItem.WhiteRabbit;
        UseItem(Player);
        chase = rabbits.Single();
        Require(chase.Target == runnerUp, "leading rider's rabbit targets second place");
        for (int i = 0; i < 30; i++) TickItemEffects(1f / 60);
        Require(chase.Distance < Player.Distance, "leading rider's rabbit runs backward");
        for (int i = 0; i < 180 && rabbits.Count > 0; i++) TickItemEffects(1f / 60);
        Require(runnerUp.Ward == 0 && runnerUp.EjectTime <= 0 && rabbitReplay is null, "Portal Ward absorbs Bunny Smite without a replay");
        Player.Item = RaceItem.WhiteRabbit;
        UseItem(Player);
        for (int i = 0; i < 180 && rabbits.Count > 0; i++) TickItemEffects(1f / 60);
        Require(runnerUp.EjectTime > 0 && runnerUp.Rider.TopLevel, "backward Bunny Smite unseats second place");
        ResetRace();
        Require(rabbits.Count == 0 && !second.Rider.TopLevel && filmedRabbit is null && rabbitReplay is null && !rabbitCameraPanel.Visible,
            "retry clears rabbit chases and camera and remounts smitten racers");
    }
    private void CheckCombat()
    {
        pendingSwipe = 0;
        useItem = false;
        foreach (var racer in racers)
        {
            racer.Reattach();
            racer.FreeDriving = false;
            racer.Stability = 100;
            racer.EjectTime = racer.SwingTime = 0;
        }
        var right = racers[1];
        var left = racers[2];
        var ahead = racers[3];
        var far = racers[4];
        Player.Distance = 65;
        Player.Lane = 0;
        right.Distance = 65.7f;
        right.Lane = 1.8f;
        left.Distance = 65;
        left.Lane = -2.0f;
        ahead.Distance = 72;
        ahead.Lane = .2f;
        far.Distance = 93;
        far.Lane = 2;
        foreach (var racer in racers)
        {
            racer.Frozen = racer.Ward = racer.Slowed = racer.ResetHold = racer.ItemGrace = racer.SwipeCooldown = 0;
            racer.Checkpoint = BogCircuit.Grid;
            racer.Draw(track, 0, false);
        }
        Require(Player.Rider.HasVisibleHeldMesh && Player.Rider.HeldSetup == RaceWeapon.All[weaponIndex].Setup, "native held weapon");
        _Input(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = true });
        Require(pendingSwipe == -1, "Q queues a left swipe");
        TrySwipe(Player, pendingSwipe);
        TickRacerSwipe(Player, .14f);
        Require(left.Stability == 65 && right.Stability == 100 && left.Slowed == 0 && left.EjectTime == 0,
            "left hit costs balance without slowing or ejecting immediately");
        Player.SwipeCooldown = 0;
        _Input(new InputEventKey { PhysicalKeycode = Key.E, Pressed = true });
        Require(pendingSwipe == 1 && !useItem, "E queues a right swipe");
        TrySwipe(Player, pendingSwipe);
        TickRacerSwipe(Player, .14f);
        Require(right.Stability == 65 && right.Slowed == 0, "right hit costs balance");
        left.Speed = 20;
        for (int hit = 0; hit < 2; hit++)
        {
            left.ItemGrace = 0;
            Player.SwipeCooldown = 0;
            TrySwipe(Player, -1);
            TickRacerSwipe(Player, .14f);
            if (hit == 0) Require(left.Speed > 19 && !left.Rider.TopLevel, "second swipe barely slows");
        }
        Require(left.EjectTime > 0 && left.Rider.TopLevel && left.Stability == 0, "third Kaskara hit detaches rider");
        Vector3 launch = left.Rider.GlobalPosition;
        left.EjectTime -= .24f;
        left.Tumble(track);
        Require(left.Rider.GlobalPosition.DistanceTo(launch) > .8f && left.Rider.GlobalPosition.Y > launch.Y + .4f,
            "rider flies upwards and away before recall");
        left.EjectTime = .01f;
        TickRace(1f / 60, true, false, 0, false, false);
        Require(!left.Rider.TopLevel && left.Distance == left.Checkpoint && left.ResetHold > 0 && left.Stability == 100,
            "unseated rival resets at checkpoint with full balance");
        left.ResetHold = 0;
        left.Distance = 65;
        left.Lane = -2.0f;
        right.Lane = 4.0f;
        Vector3 heading = Player.Heading(track);
        Vector3 sidestep = Player.Right(track);
        ahead.FreeDriving = true;
        ahead.FreePosition = track.FreeGround(Player.CoursePosition(track) + heading * 15 + sidestep * 5);
        Player.Item = RaceItem.Frostbolt;
        _Input(new InputEventKey { PhysicalKeycode = Key.F, Pressed = true });
        Require(useItem, "F queues a pickup");
        UseItem(Player);
        var bolt = shots.Single();
        Require(bolt.Kind == RaceItem.Frostbolt && bolt.Target == ahead && bolt.Heading == heading,
            "Frostbolt locks onto a rival off the launch line");
        for (int i = 0; i < 120 && shots.Count > 0; i++)
        {
            ahead.FreePosition = track.FreeGround(ahead.FreePosition + heading * .24f + sidestep * .09f);
            TickItemEffects(1f / 60);
            if (i == 8) Require(bolt.Heading.AngleTo(heading) > .1f && ahead.Frozen <= 0 && bolt.IceTrail?.SampleCount > 5,
                "Frostbolt bends mid-flight and leaves a sampled trail before contact");
        }
        Require(ahead.Frozen > 0 && SpeedLimit(ahead, 20) < 13, "native frostbolt slows on impact");
        Require(itemEffects?.PlayedScripts > 0, "native spell effect script");
        Require(frostboltTrails.Count == 1 && !frostboltTrails[0].Flying, "icy trail lingers after contact");
        TickItemEffects(.60f);
        Require(frostboltTrails.Count == 0, "old Frostbolt trail fades away");
        ahead.Frozen = 0;
        ahead.FreeDriving = false;
        Player.Item = RaceItem.TuskerPus;
        left.ItemGrace = 0;
        UseItem(Player);
        var shot = shots.Single();
        Require(shot.Target == left, "Tusker Pus locks the nearest rival");
        TickItemEffects(2.2f);
        Require(left.Distance == left.Checkpoint && left.ResetHold > 0 && left.ItemGrace > 0, "Tusker Pus resets rival to checkpoint");
        Player.Item = RaceItem.PortalWard;
        UseItem(Player);
        Require(!AcceptHit(Player) && Player.Ward == 0, "portal ward absorbs one hit");
        pendingSwipe = 0;
        useItem = false;
        pressed.Clear();
    }
    private void CheckShortcuts()
    {
        Require(FindDryShortcut() is not null, "a dry off-track line can beat guided pace between consecutive checkpoints");
        var first = track.Point(track.Gate(0));
        var heading = track.Forward(track.Gate(0));
        var wide = track.Right(track.Gate(0)) * (track.GateHalfWidth + 3);
        Require(track.TryCrossGate(0, first - heading, first + heading, out _), "gate forward crossing");
        Require(!track.TryCrossGate(0, first + heading, first - heading, out _), "reverse gate not counted");
        Require(!track.TryCrossGate(0, first - heading + wide, first + heading + wide, out _), "gate cannot be bypassed on shoulder");

        float before = Player.Distance, lane = Player.Lane, speed = Player.Speed, velocity = lateral;
        int gates = Player.GatesCleared;
        Player.Distance = track.Gate(0) + 7;
        Player.Speed = 16;
        Player.Lane = track.HalfWidth + .24f;
        lateral = 3;
        MovePlayer(1f / 60, true, false, 1, false);
        Require(Player.FreeDriving, "steering past the marker starts free driving");
        Vector3 exit = Player.FreePosition;
        float yaw = Player.FreeYaw;
        MovePlayer(1f / 60, true, false, 1, false);
        Require(Player.FreePosition.DistanceTo(exit) > .18f && Math.Abs(Player.FreeYaw - yaw) > .015f,
            "off-track motion steers independently of spline");
        Player.FreePosition = track.FreeGround(track.Surface(Player.Distance, track.HalfWidth - .65f));
        Player.FreeYaw = BogCircuit.Yaw(track.Forward(Player.Distance));
        MovePlayer(1f / 60, true, false, 0, false);
        Require(!Player.FreeDriving && Player.GatesCleared == gates, "rejoining preserves ordered checkpoint progress");

        float next = track.Gate(gates);
        float nextYaw = BogCircuit.Yaw(track.Forward(next));
        Vector3 gate = track.Point(next);
        Player.FreeDriving = true;
        Player.FreePosition = track.FreeGround(gate - track.Forward(next));
        Player.FreeYaw = nextYaw;
        Vector3 prior = Player.FreePosition;
        Player.FreePosition = track.FreeGround(gate + track.Forward(next));
        RecordGate(Player, prior, Player.FreePosition, 1f / 60);
        Require(Player.GatesCleared == gates + 1, "free driving also counts a physical gate crossing");

        Player.FreeDriving = false;
        Player.Distance = before;
        Player.Lane = lane;
        Player.Speed = speed;
        Player.GatesCleared = gates;
        Player.Checkpoint = gates == 0 ? BogCircuit.Grid - 10 : track.Gate(gates - 1);
        lateral = velocity;
    }
    private (Vector3 From, Vector3 To, float Along, float FromDistance, int NextGate)? FindDryShortcut()
    {
        for (int sector = 0; sector < 4; sector++)
        {
            for (float start = track.Gate(sector) + 3; start < track.Gate(sector + 1) - 24; start += 2.5f)
            {
                for (float end = track.Gate(sector + 1) - 3; end > start + 24; end -= 2.5f)
                {
                    Vector3 a = track.Point(start), b = track.Point(end);
                    float xzLength = new Vector2(b.X - a.X, b.Z - a.Z).Length();
                    if (xzLength / OffRoadPace >= (end - start) / RoadPace - .10f) continue;
                    int steps = Mathf.CeilToInt(xzLength * 1.5f);
                    Vector3 previous = track.FreeGround(a);
                    bool offRoad = false, clear = true;
                    for (int n = 1; n <= steps; n++)
                    {
                        float t = (float)n / steps;
                        if (!track.TryFreeMove(previous, a.Lerp(b, t), out previous))
                        {
                            clear = false;
                            break;
                        }
                        if (!offRoad && n % 5 == 0)
                            offRoad = Math.Abs(track.Project(previous, Mathf.Lerp(start, end, t)).Lane) > track.HalfWidth + .5f;
                    }
                    if (clear && offRoad) return (a, b, end - start, start, sector + 1);
                }
            }
        }
        return null;
    }
    private void CheckGroundBlend()
    {
        var material = (ShaderMaterial)course.GetNode<MeshInstance3D>("DAT course scenery/Blended DAT terrain").MaterialOverride;
        var texture = material.GetShaderParameter("blend_map").AsGodotObject() as Texture2D;
        Require(texture is not null, "terrain blend map bound to shader");
        using var mask = texture!.GetImage();
        Color Sample(Vector3 point)
        {
            Vector2 uv = (MuckwaterLayout.Flat(point) - MuckwaterLayout.Origin) / MuckwaterLayout.Extent;
            return mask.GetPixel((int)(uv.X * mask.GetWidth()), (int)(uv.Y * mask.GetHeight()));
        }
        Color road = Sample(track.Point(BogCircuit.Grid + 9));
        Color shoulder = Sample(track.Surface(BogCircuit.Grid + 9, track.HalfWidth + 1.2f));
        Color grass = Sample(new Vector3(135, 0, 62));
        Require(road.R > .97f && shoulder.R < .10f && shoulder.G > .35f && grass.R < .02f && grass.G < .02f,
            "race line blends into its shoulder");
        byte[] data = mask.GetData();
        int transitions = 0, deepWater = 0;
        for (int i = 0; i < mask.GetWidth() * mask.GetHeight() * 4; i += 4)
        {
            if (data[i + 2] is > 25 and < 230) transitions++;
            if (data[i + 3] > 150) deepWater++;
        }
        Require(transitions > 1200 && deepWater > 800, "native grass/marsh transitions and wet basins");
    }

    private void CheckWeather()
    {
        Require(firstMoon is not null && secondMoon is not null && firstMoon != secondMoon, "both retail DAT moon textures load");
        int previous = -1;
        var seen = new HashSet<int>();
        for (int roll = 0; roll < 16; roll++)
        {
            int current = RollRaceWeather();
            Require(current != previous, "race weather does not immediately repeat");
            seen.Add(current);
            previous = current;
        }
        Require(seen.Count > 1, "weather rolls choose varied presets");
        foreach (var weather in WeatherPresets)
        {
            ApplyWeather(weather);
            Require(rainOverlay.Visible == (weather.Rain > 0) && weatherLabel.Text == weather.Name &&
                weatherEnvironment.FogDensity == weather.FogDensity && weatherSun.LightEnergy == weather.SunEnergy,
                $"{weather.Name} updates the sky, race light and precipitation together");
        }
        ApplyWeather(WeatherPresets[3]);
        TickTrackWeather(8);
        Require(trackWater > .5f && rainOverlay.Visible, "rain collects on the track");
        for (int second = 8; second < 39; second++) TickTrackWeather(1);
        Require(trackWater > 0 && !rainOverlay.Visible, "puddles remain after the shower passes");
        TickTrackWeather(60);
        Require(trackWater == 0, "standing water drains");
        ApplyWeather(WeatherPresets[0]);
    }

    private void CheckRaceViews()
    {
        SetRaceCamera(1);
        var original = camera.Transform;
        foreach (var expected in new[] { RaceView.FirstPerson, RaceView.Isometric, RaceView.TiltShift, RaceView.WideChase, RaceView.Chase })
        {
            _Input(new InputEventKey { PhysicalKeycode = Key.V, Pressed = true });
            Require(raceView == expected && camera.Position.IsFinite() && camera.Basis.IsFinite(), $"V selects {expected}");
            Require((camera.Projection == Camera3D.ProjectionType.Orthogonal) == (expected == RaceView.Isometric) &&
                tiltShiftOverlay.Visible == (expected == RaceView.TiltShift), "view projection and blur match the selected camera");
            if (expected == RaceView.FirstPerson)
                Require(camera.Position.DistanceTo(Player.CoursePosition(track)) < 5 &&
                    (camera.Position - Player.CoursePosition(track)).Dot(Player.Heading(track)) > .3f, "first-person camera sits above the mount's head");
        }
        Require(camera.Transform == original || camera.Position.DistanceTo(original.Origin) < .01f, "the camera cycle restores chase framing");
        pressed.Remove(Key.V);
    }

    private void CheckSurfaceRelief()
    {
        var body = showcase!.Mount.BodyMeshes.SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount())
            .Select(index => (Mesh: mesh.Mesh, Index: index, Material: mesh.Mesh.SurfaceGetMaterial(index) as StandardMaterial3D)))
            .FirstOrDefault(surface => surface.Material is { NormalEnabled: true, NormalTexture: not null });
        Require(body.Material is { MetallicSpecular: 0, SpecularMode: BaseMaterial3D.SpecularModeEnum.Disabled } &&
            body.Mesh.SurfaceGetArrays(body.Index)[(int)Mesh.ArrayType.Tangent].AsFloat32Array().Length > 0,
            "matte native mount material uses a tangent-space normal map");
        using var normal = body.Material!.NormalTexture.GetImage();
        Require(normal.HasMipmaps() && normal.GetData().Distinct().Count() > 3, "native normal maps contain filtered surface detail");
    }

    private static void Require(bool success, string operation)
    {
        if (!success) throw new InvalidOperationException($"Gromnie test failed on {operation}.");
    }


    private async void CaptureFrames(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            if (OS.GetCmdlineUserArgs().Contains("--hybrid-only"))
            {
                await CaptureHybridAndSnowboard(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--island-only"))
            {
                await CaptureIslandRegatta(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--kaleidoscope-raster-mirrors"))
            {
                await CaptureKaleidoscopeRasterMirrors(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--dismount-only"))
            {
                await CaptureDismountSprint(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--arcade-speed-only"))
            {
                await CaptureArcadeSpeed(folder, OS.GetCmdlineUserArgs().Contains("--speed-video"));
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--soundtrack-only"))
            {
                await CaptureSoundtrackSettings(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--void-benchmark"))
            {
                await BenchmarkVoidCourse();
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--flight-controls-only"))
            {
                await CaptureFlightControls(folder, OS.GetCmdlineUserArgs().Contains("--flight-video"));
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--rabbit-only"))
            {
                StartVoidCheck();
                var upperDeck = track.Project(new Vector3(-298, 162, 133), 825);
                SetRabbitCheckGrid(upperDeck.Distance);
                await CaptureWhiteRabbit(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--kaleidoscope-only"))
            {
                await CaptureKaleidoscopeCourse(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--jester-only"))
            {
                await CaptureJesterDescent(folder);
                StopJesterReflection();
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--void-only"))
            {
                await CaptureVoidCourse(folder, OS.GetCmdlineUserArgs().Contains("--void-video"));
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--presentation-only"))
            {
                await CaptureRacePresentation(folder, OS.GetCmdlineUserArgs().Contains("--presentation-video"));
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--rider-templates-only"))
            {
                await CaptureRiderTemplates(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--mount-colors-only"))
            {
                await CaptureMountColors(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--siraluun-only"))
            {
                SelectMount(RaceMountKind.Siraluun);
                for (int i = 0; i < RaceMount.SiraluunColors.Length; i++)
                {
                    SelectSkin(i);
                    await SaveFrame(Path.Combine(folder, $"Siraluun-{RaceMount.SiraluunColors[i].Name}.png"));
                }
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--skates-only"))
            {
                SelectMount(RaceMountKind.CarenziSkates);
                await SaveFrame(Path.Combine(folder, "Carenzi-Skates-Stable.png"));
                await CaptureMountOnTrack(folder, RaceMountKind.CarenziSkates, "Carenzi-Skates.png");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--platform-mounts"))
            {
                await CaptureMountOnTrack(folder, RaceMountKind.FestivusCarpet, "Festivus-Carpet.png");
                await CaptureMountOnTrack(folder, RaceMountKind.IsinDuleDisc, "Isin-Dule-Mage-Disc.png");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--mounts-only"))
            {
                SelectMount(RaceMountKind.BaelZharon);
                CheckFrontCarry(showcase!);
                await SaveFrame(Path.Combine(folder, "BaelZharon-Front-Carry.png"));
                ChooseCourse(0);
                StartRace();
                Require(racing, "Bael’Zharon capture race loads");
                paused = true;
                countdown = 0;
                Player.Distance = 78;
                Player.Lane = -.5f;
                Player.Speed = 20;
                Player.Draw(track, .13f, true);
                CheckFrontCarry(Player);
                camera.Fov = 54;
                camera.Position = Player.Root.ToGlobal(new Vector3(3.2f, 3.05f, -7.7f));
                camera.LookAt(Player.Root.ToGlobal(new Vector3(0, 2.38f, -.25f)));
                UpdateRaceUi();
                await SaveFrame(Path.Combine(folder, "BaelZharon-Race.png"));
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--new-mounts-only"))
            {
                await CaptureNewMounts(folder);
                QuitSoundtrackCheck();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--ui-fonts-only"))
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                CheckDatUiFonts();
                await SaveFrame(Path.Combine(folder, "Dereth-Font-Stable.png"));
                OpenOnlineMenu();
                await SaveFrame(Path.Combine(folder, "Dereth-Font-Online.png"));
                onlineUi.Hide();
                StartRace();
                Require(racing, "the DAT font HUD capture race loads");
                for (int i = 0; i < 290; i++) TickRace(1f / 60, true, false, 0, false, false);
                UpdateRaceUi();
                await SaveFrame(Path.Combine(folder, "Dereth-Font-HUD.png"));
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--loadouts-only"))
            {
                await CaptureRaceLoadouts(folder);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--track-editor-only"))
            {
                await CaptureTrackEditor(folder);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--span-only"))
            {
                await CaptureSpanSprint(folder);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--encounters-only"))
            {
                await CaptureRaceEncounters(folder);
                GetTree().Quit();
                return;
            }
            string[] args = OS.GetCmdlineUserArgs();
            string? landmarkName = args.FirstOrDefault(arg => arg.StartsWith("--landmark=", StringComparison.Ordinal))?["--landmark=".Length..];
            if (args.Contains("--landmarks-only") || landmarkName is not null)
            {
                await CaptureLandmarkRaces(folder, landmarkName);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--citadel-only"))
            {
                StartCitadelCheckRace();
                paused = true;
                await CaptureCitadelBattle(folder);
                GetTree().Quit();
                return;
            }
            await SaveFrame(Path.Combine(folder, "Gromnie-Stable.png"));
            await CaptureMountRoster(folder);
            await CaptureRacePower(folder);
            await CaptureUlgrimFolly(folder);
            OpenCoursePicker();
            await SaveFrame(Path.Combine(folder, "Dereth-Courses.png"));
            courseUi.Hide();
            if (xlRoutes.ContainsKey(XlCourseOffset + 72)) ChooseCourse(XlCourseOffset + 72);
            StartRace();
            for (int i = 0; i < 285; i++) TickRace(1f / 60, true, false, -.04f, false, false);
            SetRaceCamera(1);
            UpdateRaceUi();
            await SaveFrame(Path.Combine(folder, "Holtburg-Race.png"));

            ClearItemVisuals();
            Player.Lane = 0;
            var target = racers[1];
            for (int i = 2; i < racers.Count; i++)
            {
                racers[i].Distance = Player.Distance + i * 4;
                racers[i].Lane = i % 2 == 0 ? -2.55f : 2.35f;
                racers[i].Draw(track, 0, true);
            }
            Vector3 heading = Player.Heading(track);
            target.Distance = Player.Distance + 20;
            target.FreeDriving = true;
            target.FreePosition = track.FreeGround(Player.CoursePosition(track) + heading * 21 + Player.Right(track) * 7);
            foreach (var other in racers.Skip(2))
            {
                other.Distance += 26;
                other.Draw(track, 0, true);
            }
            target.ItemGrace = target.Ward = target.ResetHold = 0;
            target.Draw(track, 0, true);
            Player.Draw(track, 0, true);
            SetRaceCamera(1);
            Player.Item = RaceItem.Frostbolt;
            UseItem(Player);
            for (int i = 0; i < 16; i++) TickItemEffects(1f / 60);
            UpdateRaceUi();
            await SaveFrame(Path.Combine(folder, "Dereth-Frostbolt.png"));
            target.FreeDriving = false;
            await CaptureKnockoff(folder);
            await CaptureOffTrack(folder);
            await CaptureRoadside(folder);
            await CaptureXl(folder);
            await CaptureMedium(folder);
            await CaptureSpanSprint(folder);
            foreach (var ridge in ridgeCircuits.Values)
            {
                OpenStable();
                ChooseCourse(ridge.Id);
                StartRace();
                Require(activeTown == ridge.Id && racing, "Obsidian flight capture course loads");
                await CaptureFlyover(folder, $"Obsidian-Plains-{ridge.Id}.png");
            }
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }

    private void CheckDatUiFonts(bool checkControls = true)
    {
        var body = ThemeForUi().DefaultFont;
        var title = titleLabels[0].GetThemeFont("font");
        Require(body is FontFile && title is FontFile && body != title, "native body and title fonts");
        if (checkControls)
            Require(mountPicker.GetThemeFont("font") == body && timerText.GetThemeFont("font") == body &&
                onlineInvite.GetThemeFont("font") == body, "native fonts in the stable, online menu and HUD");
        Require(body.GetStringSize("Join — 8A2G", fontSize: 16).X > 35 &&
            title.GetStringSize("GROMNIE", fontSize: 64).X is > 120 and < 420,
            "DAT font spacing and title width");
    }

    private async Task CaptureMountOnTrack(string folder, RaceMountKind kind, string name)
    {
        OpenStable();
        SelectMount(kind);
        ChooseCourse(0);
        StartRace();
        Require(racing, "the mount capture race loads");
        paused = true;
        countdown = 0;
        Player.Distance = 78;
        Player.Lane = -.5f;
        Player.Speed = 20;
        Player.Lean = .22f;
        Player.Draw(track, .42f, true, -.25f);
        camera.Fov = 44;
        camera.Position = Player.Root.ToGlobal(new Vector3(3.8f, 3.45f, -5.5f));
        camera.LookAt(Player.Root.ToGlobal(new Vector3(0, 1.22f, -.22f)));
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, name));
    }

    private async Task CaptureRoadside(string folder)
    {
        OpenStable();
        ChooseCourse(0);
        StartRace();
        paused = true;
        countdown = 0;
        var site = roadsideSites.First(row => row.Kind == Spectacle.Dance);
        Player.Distance = site.Distance - 7.2f;
        Player.Lane = site.Side * (track.HalfWidth - 3.4f);
        Player.Speed = 19;
        Player.Draw(track, .25f, true);
        for (int i = 0; i < 64; i++)
        {
            elapsed += 1f / 60;
            TickRoadside(1f / 60);
            TickItemEffects(1f / 60);
        }
        Vector3 inward = -track.Right(site.Distance) * site.Side;
        Vector3 heading = track.Forward(site.Distance);
        camera.Position = site.Center + inward * 13 - heading * 5.3f + Vector3.Up * 3.7f;
        camera.Fov = 52;
        camera.LookAt(site.Center - heading * .9f + Vector3.Up * .95f);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Dereth-Roadside-Crowd.png"));

        RoadHazard hazard = roadHazards[0];
        hazard.PulseAt = elapsed;
        TickRoadHazards(hazard.Center);
        for (int i = 0; i < 13; i++) TickItemEffects(1f / 60);
        Vector3 toward = (hazard.Center - Player.Root.Position) with { Y = 0 };
        toward = toward.Normalized();
        camera.Position = hazard.Center - toward * 12 + toward.Cross(Vector3.Up) * 3 + Vector3.Up * 3.8f;
        camera.LookAt(hazard.Center + Vector3.Up * .9f);
        await SaveFrame(Path.Combine(folder, "Dereth-ObjFX-Hazard.png"));
    }

    private async Task CaptureMountRoster(string folder)
    {
        foreach (var mount in RaceMount.All.Skip(1))
        {
            SelectMount(mount.Kind);
            string filename = mount.Kind == RaceMountKind.Asheron ? "Asheron-Piggyback.png" : $"Mount-{mount.Kind}.png";
            await SaveFrame(Path.Combine(folder, filename));
        }

        ChooseCourse(0);
        StartRace();
        paused = true;
        countdown = 0;
        const float at = 71;
        float[] lanes = [0, -5.4f, 4.2f, 8.8f, -9.2f];
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.Distance = at + (i % 2 == 0 ? 0 : 1.3f);
            racer.Lane = lanes[i];
            racer.Speed = 20;
            racer.Draw(track, .29f + i * .045f, true);
        }
        Vector3 center = track.Point(at);
        camera.Position = center + track.Forward(at) * 19 + track.Right(at) * 6.1f + Vector3.Up * 5.55f;
        camera.Fov = 55;
        camera.LookAt(center + Vector3.Up * 1.3f);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Dereth-Mount-Roster.png"));
        OpenStable();
        SelectMount(RaceMountKind.Gromnie);
    }

    private async Task CaptureKnockoff(string folder)
    {
        StartRace();
        countdown = 0;
        elapsed = 3.8f;
        Player.Distance = track.Gate(0) + 16;
        Player.GatesCleared = 1;
        Player.Checkpoint = track.Gate(0);
        Player.Speed = 18;
        Player.Lane = -.5f;
        Player.Draw(track, 0, true);

        var victim = racers[4];
        foreach (var other in racers.Skip(1))
        {
            other.Distance = Player.Distance + (other == victim ? 1.1f : 9 + racers.IndexOf(other) * 3);
            other.GatesCleared = 1;
            other.Lane = other == victim ? 1.42f : 0;
            other.Speed = 17;
            other.Draw(track, 0, true);
        }
        int hits = Mathf.CeilToInt(100f / Player.Weapon.BalanceLoss);
        for (int attack = 0; attack < hits; attack++)
        {
            victim.ItemGrace = Player.SwipeCooldown = 0;
            TrySwipe(Player, 1);
            TickRacerSwipe(Player, .16f);
        }
        Require(victim.EjectTime > 0, "knockoff capture");
        victim.EjectTime -= .34f;
        victim.Tumble(track);
        SetRaceCamera(1);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Gromnie-Knockoff.png"));
    }

    private async Task CaptureOffTrack(string folder)
    {
        ChooseCourse(0);
        StartRace();
        var shortcut = FindDryShortcut();
        if (shortcut is null) return;
        var (from, to, _, distance, nextGate) = shortcut.Value;
        countdown = 0;
        elapsed = 5.4f;
        Player.GatesCleared = nextGate;
        Player.Checkpoint = track.Gate(nextGate - 1);
        Player.Speed = OffRoadPace;
        Player.Distance = distance;
        Player.FreeDriving = true;
        Vector3 direction = to - from;
        Player.FreeYaw = BogCircuit.Yaw(direction);
        for (int i = 1; i < 20; i++)
        {
            Player.FreePosition = track.FreeGround(from.Lerp(to, i / 20f));
            UpdateRoadProjection(Player);
            if (Math.Abs(Player.Lane) > track.HalfWidth + 2) break;
        }
        Player.Draw(track, 0, true);
        for (int i = 1; i < racers.Count; i++)
        {
            var other = racers[i];
            other.GatesCleared = nextGate;
            other.Distance = Player.Distance + (i - 2) * 4;
            other.Speed = 17;
            other.Draw(track, 0, true);
        }
        HighlightCheckpoint();
        SetRaceCamera(1);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Gromnie-Shortcut.png"));
    }
    private async Task CaptureXl(string folder)
    {
        if (xlRoutes.Count == 0) return;
        int preferred = YanshiXlCourseId;
        ChooseCourse(xlRoutes.ContainsKey(preferred) ? preferred : xlRoutes.Keys.First());
        StartRace();
        Require(track.IsXL && track.NativeBlockCount == 64, "8×8 preview");
        // Run from the real starting grid so the image shows the assembled XL world.
        Player.Lane = 5.7f;
        for (int frame = 0; frame < 485; frame++)
            TickRace(1f / 60, true, false, Mathf.Clamp((5.7f - Player.Lane) * .19f, -1, 1), false, false);
        UpdateSpeedBlur(1f / 60, immediate: true);
        paused = true;
        SetRaceCamera(1);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Dereth-XL-Race.png"));
        ClearSpeedBlur();
        await CaptureWeather(folder);
        await CaptureRaceViews(folder);
        await CaptureWhiteRabbit(folder);
        await CaptureFlyover(folder);
        await CaptureRegionalOverview(folder, "Dereth-XL-Overview.png");
    }

    private async Task CaptureMedium(string folder)
    {
        OpenStable();
        int medium = mediumRoutes.Keys.First();
        ChooseCourse(medium);
        StartRace();
        Require(activeTown == medium && track.IsMedium && track.NativeBlockCount == 49, "7×7 preview");
        for (int frame = 0; frame < 520; frame++)
            TickRace(1f / 60, true, false, Mathf.Clamp(-Player.Lane * .22f, -1, 1), false, false);
        paused = true;
        SetRaceCamera(1);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Dereth-Medium-Race.png"));
        await CaptureRegionalOverview(folder, "Dereth-Medium-Overview.png");
    }

    private async Task CaptureWeather(string folder)
    {
        var original = camera.Transform;
        float originalFov = camera.Fov;
        Vector3 heading = track.Forward(Player.Distance);
        Vector3 right = heading.Cross(Vector3.Up);
        camera.Position = Player.Root.Position - heading * 12.1f + right * 2.9f + Vector3.Up * 3.35f;
        camera.Fov = 62;
        camera.LookAt(Player.Root.Position + heading * 24 + Vector3.Up * 3.9f);
        var weatherView = camera.Transform;
        foreach (int index in new[] { 1, 2, 3, 4, 5 })
        {
            ApplyWeather(WeatherPresets[index]);
            camera.Transform = weatherView;
            if (WeatherPresets[index].Rain > 0) TickTrackWeather(12);
            if (WeatherPresets[index].Night >= .9f)
            {
                Vector3 skyHeading = heading.Lerp(track.Forward(BogCircuit.Grid + 75), .5f).Normalized();
                camera.LookAt(camera.Position + (skyHeading + Vector3.Up * .15f) * 48);
            }
            string name = WeatherPresets[index].Name.Replace(' ', '-');
            await SaveFrame(Path.Combine(folder, $"Dereth-{name}.png"));
        }
        ApplyWeather(WeatherPresets[0]);
        camera.Transform = original;
        camera.Fov = originalFov;
    }

    private async Task CaptureRaceViews(string folder)
    {
        SetPhysicsProcess(false);
        paused = false;
        foreach (string frame in new[] { "First-Person", "Isometric", "Tilt-Shift", "Wide-Chase" })
        {
            CycleRaceView();
            UpdateRaceUi();
            await SaveFrame(Path.Combine(folder, $"Dereth-{frame}.png"));
        }
        CycleRaceView();
        paused = true;
        ClearSpeedBlur();
        SetPhysicsProcess(true);
    }

    private async Task CaptureWhiteRabbit(string folder)
    {
        ClearItemVisuals();
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.Land();
            racer.Reattach();
            racer.Finish = -1;
            racer.ResetHold = racer.ItemGrace = racer.Ward = 0;
            racer.Distance = Player.Distance + (i == 0 ? 0 : i == 1 ? 16 : 24 + i * 4);
            racer.Lane = i == 0 ? -.2f : i == 1 ? -2.1f : (i % 2 == 0 ? 3.2f : -3.2f);
            racer.Draw(track, 0, true);
        }
        SetRaceCamera(1);
        SetPhysicsProcess(false);
        paused = false;
        Player.Item = RaceItem.WhiteRabbit;
        UseItem(Player);
        for (int i = 0; i < 38; i++) TickItemEffects(1f / 60);
        var chase = rabbits.Single();
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "White-Rabbit-Cam.png"));
        Vector3 rabbitAt = chase.Root.Position;
        camera.Position = rabbitAt + track.Right(Player.Distance) * 4.2f - track.Forward(Player.Distance) * 2.8f + Vector3.Up * 2.0f;
        camera.LookAt(rabbitAt + track.Forward(Player.Distance) * 1.1f + Vector3.Up * .55f);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "White-Rabbit.png"));
        var target = racers[1];
        for (int i = 0; i < 90 && rabbits.Count > 0; i++) TickItemEffects(1f / 60);
        target.EjectTime = Math.Max(0, target.EjectTime - .18f);
        if (target.EjectTime > 0) target.Tumble(track);
        for (int i = 0; i < 40; i++) TickItemEffects(1f / 60);
        SetRaceCamera(1);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Bunny-Smite.png"));
        paused = true;
        SetPhysicsProcess(true);
    }

    private async Task CaptureFlyover(string folder, string filename = "Gromnatross-Flyover.png")
    {
        if (flyover is not { } route) return;
        ResetRace();
        PutPlayerAtLaunch(route);
        TickRace(1f / 60, true, false, 0, false, false);
        Require(Player.Flight is not null, "capture uses the real flyover launch");
        while (Player.Distance < Mathf.Lerp(route.Start, route.End, .50f) && Player.Flight is not null)
            TickRace(1f / 60, true, false, .35f, false, false);
        UpdateSpeedBlur(1f / 60, immediate: true);
        paused = true;
        SetRaceCamera(1);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, filename));
        ClearSpeedBlur();
    }
    private async Task CaptureRegionalOverview(string folder, string filename)
    {
        paused = true;
        raceUi.Hide();
        var bounds = track.NativeBounds;
        Vector2 center = bounds.GetCenter();
        var stageEnvironment = GetChildren().OfType<WorldEnvironment>().First().Environment;
        stageEnvironment.FogEnabled = false;
        camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.Size = bounds.Size.Y + 135;
        camera.Far = 5000;
        camera.Position = new Vector3(center.X, 2300, center.Y);
        camera.LookAt(new Vector3(center.X, 0, center.Y), Vector3.Forward);
        Vector2 Plot(float x, float z) => camera.UnprojectPosition(new Vector3(x, 0, z));

        var overlay = new CanvasLayer { Layer = 22 };
        AddChild(overlay);
        float cellSize = bounds.Size.X / track.GridSide;
        for (int i = 0; i <= track.GridSide; i++)
        {
            float x = bounds.Position.X + i * cellSize;
            float z = bounds.Position.Y + i * cellSize;
            DrawLine([Plot(x, bounds.Position.Y), Plot(x, bounds.End.Y)], new("d1d1b777"), 1.1f);
            DrawLine([Plot(bounds.Position.X, z), Plot(bounds.End.X, z)], new("d1d1b777"), 1.1f);
        }
        var route = Enumerable.Range(0, 481).Select(i =>
        {
            Vector3 p = track.Point(track.Length * i / 480);
            return Plot(p.X, p.Z);
        }).ToArray();
        DrawLine(route, new Color("24332b"), 7);
        DrawLine(route, new Color("ffcf72"), 3.1f);

        var card = Layer(overlay);
        Plate(card, 26, 32, 262, 130, "172821ef", "829763");
        Text(card, track.Name.ToUpperInvariant(), 40, 48, 236, 21, Gold);
        Text(card, $"{track.Length / 1000:F2} km lap", 41, 83, 190, 23);
        Text(card, $"{track.NativeBlockCount} native landblocks", 41, 122, 220, 14, Soft);
        await SaveFrame(Path.Combine(folder, filename));
        overlay.QueueFree();
        stageEnvironment.FogEnabled = true;

        void DrawLine(Vector2[] points, Color color, float width) => overlay.AddChild(new Line2D
        {
            Points = points, Width = width, DefaultColor = color, Antialiased = true
        });
    }
    private async Task SaveFrame(string path)
    {
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Require(image.SavePng(path) == Error.Ok, "capture " + path);
    }
}














