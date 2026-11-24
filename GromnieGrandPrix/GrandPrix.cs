using Godot;
using DerethSurvivors;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

public partial class GrandPrix : Node3D
{
    private readonly Node3D muckCourse = new() { Name = "Muckwater Circuit" };
    private Node3D course = null!;
    private readonly Node3D garage = new() { Position = new(275, 0, 270) };
    private readonly Camera3D camera = new() { Current = true, Fov = 57, Far = 220 };
    private readonly HashSet<Key> pressed = [];
    private DatArchive? archive;
    private AcAssets assets = null!;
    private CharacterCreationCatalog creation = null!;
    private RareCatalog.Creature[] skins = [];
    private MountedRacer? showcase;
    private CharacterAppearance appearance = new();
    private int skinIndex;
    private readonly Dictionary<RaceMountKind, int> mountColors = [];
    private int SelectedMountColor => MountColorFor(selectedMount);
    private RaceMountKind selectedMount;
    private float best = -1, worldTime, previewYaw = -2.23f;
    private bool testMode, useItem, loaded;
    private string datFolder = "";
    private const string SaveFile = "user://stable.json";
    private const int CourseLayoutVersion = 1;
    private const int XlLayoutVersion = 1;
    private const int MediumLayoutVersion = 1;
    private readonly Dictionary<int, float> legacyCourseBests = [];

    public override void _Ready()
    {
        course = muckCourse;
        track = muckTrack;
        AddChild(course);
        AddChild(garage);
        AddChild(camera);
        var args = OS.GetCmdlineUserArgs();
        var browserOutput = args.FirstOrDefault(arg => arg.StartsWith("--export-yanshi-web=", StringComparison.Ordinal));
        if (browserOutput is not null)
        {
            string browserDatFolder = args.FirstOrDefault(arg => arg.StartsWith("--dat=", StringComparison.Ordinal))?[6..]
                ?? System.Environment.GetEnvironmentVariable("ASHERONS_CALL_DAT") ?? "C:/Turbine/Asheron's Call";
            ExportYanshiForBrowser(browserDatFolder, browserOutput["--export-yanshi-web=".Length..]);
            return;
        }
        var mediumOutput = args.FirstOrDefault(arg => arg.StartsWith("--plan-medium-courses=", StringComparison.Ordinal))?[22..];
        if (mediumOutput is not null)
        {
            string mediumFolder = args.FirstOrDefault(arg => arg.StartsWith("--dat=", StringComparison.Ordinal))?[6..]
                ?? "C:/Turbine/Asheron's Call";
            PlanMediumCourses(mediumFolder, mediumOutput, args.Contains("--verify-medium-courses"), args.Contains("--resume-medium-courses"));
            return;
        }
        var planOutput = args.FirstOrDefault(arg => arg.StartsWith("--plan-town-routes=", StringComparison.Ordinal))?[19..];
        if (planOutput is not null)
        {
            string planFolder = args.FirstOrDefault(arg => arg.StartsWith("--dat=", StringComparison.Ordinal))?[6..]
                ?? "C:/Turbine/Asheron's Call";
            int? preferredTour = int.TryParse(args.FirstOrDefault(arg => arg.StartsWith("--gentle-town=", StringComparison.Ordinal))?[14..],
                out int townId) ? townId : null;
            PlanTownRoutes(planFolder, planOutput, args.Contains("--resume-town-plan"), args.Contains("--verify-town-plan"), preferredTour);
            return;
        }
        testMode = args.Contains("--smoke-test") || args.Any(arg => arg.StartsWith("--capture=", StringComparison.Ordinal));
        BuildSpeedBlur();
        BuildRaceViews();
        BuildSceneGrade();
        BuildWeather();
        InitializeSoundtrack();
        MakeUi();
        PlayMusic(MainThemeMusic);

        string? folder = args.FirstOrDefault(arg => arg.StartsWith("--dat=", StringComparison.Ordinal))?[6..];
        if (!testMode) RestoreGarage();
        if (OS.HasFeature("android"))
        {
            StartAndroidGame();
            return;
        }
        folder ??= System.Environment.GetEnvironmentVariable("ASHERONS_CALL_DAT");
        folder ??= datFolder.Length > 0 ? datFolder : "C:/Turbine/Asheron's Call";
        LoadGame(folder);
        if (args.Contains("--smoke-test")) RunSmokeTest();
        var capture = args.FirstOrDefault(arg => arg.StartsWith("--capture=", StringComparison.Ordinal))?[10..];
        if (capture is not null && loaded) CaptureFrames(capture);
    }

    private void LoadGame(string folder)
    {
        folder = folder.Trim().Trim('"');
        string datName = Path.GetFileName(folder);
        if (datName.Equals("client_portal.dat", StringComparison.OrdinalIgnoreCase) ||
            datName.Equals("client_cell_1.dat", StringComparison.OrdinalIgnoreCase))
            folder = Path.GetDirectoryName(folder) ?? "";
        try
        {
            var candidate = new DatArchive(Path.Combine(folder, "client_portal.dat"));
            try { creation = CharacterCreationCatalog.Load(candidate); }
            catch { candidate.Dispose(); throw; }
            archive = candidate;
            if (raceAudio is not null) { raceAudio.Clear(); raceAudio.QueueFree(); }
            raceAudio = new RaceAudio(candidate);
            AddChild(raceAudio);
            assets = new AcAssets(candidate, folder);
            LoadCombatIcons();
            LoadDerethMoons();
            skins = JsonSerializer.Deserialize<RareCatalog.Creature[]>(FileAccess.GetFileAsString("res://Data/mounts.json"))
                ?? throw new InvalidDataException("The Gromnie palette catalog is missing.");
            skinIndex = ClampMountColor(RaceMountKind.Gromnie, skinIndex);
            if (!creation.Validate(appearance, out _)) appearance = new CharacterAppearance();

            LoadTownCourses(folder);
            // Construct the first mounted pair before enabling the race entry point.
            try { RebuildShowcase(); }
            catch (Exception error) when (aceRider is not null && error is (InvalidDataException or IOException or ArgumentException or KeyNotFoundException))
            {
                aceRider = null;
                RebuildShowcase();
                GD.PushWarning("Could not restore the imported ACE rider. " + error.Message);
            }
            if (testMode) track.Build(course, assets);
            BuildGarage();
            if (testMode) BuildCourseItems();
            datFolder = folder;
            loaded = true;
            ApplyUiFonts(candidate);
            FillStable();
            OpenStable();
        }
        catch (Exception error)
        {
            GD.PushWarning(error.Message);
            assets?.Dispose();
            archive?.Dispose();
            archive = null;
            if (OS.HasFeature("android")) ShowAndroidDatError("The game could not read its installed race data.");
            else ShowDatPrompt(folder, "Choose the folder with client_portal.dat and client_cell_1.dat.");
            if (testMode) GD.PrintErr(error);
        }
    }

    private void BuildGarage()
    {
        var decor = new Node3D { Name = "DAT stable scenery" };
        garage.AddChild(decor);
        try
        {
            (track.Scenery ?? new MuckwaterScenery(assets, muckTrack)).BuildStable(decor);
            return;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or KeyNotFoundException or NotSupportedException)
        {
            garage.RemoveChild(decor);
            decor.QueueFree();
            GD.PushWarning("Native stable scenery unavailable. " + error.Message);
        }
        BogCircuit.Box(garage, new(0, -.56f, 0), new(75, 1, 60), BogCircuit.Paint("637844"));
        var podium = new CylinderMesh { TopRadius = 3.25f, BottomRadius = 3.55f, Height = .4f, RadialSegments = 32 };
        BogCircuit.MeshAt(garage, podium, new(0, -.22f, 0), Vector3.One, BogCircuit.Paint("ad8259"));
        var metal = BogCircuit.Paint("dcb378");
        var inner = new CylinderMesh { TopRadius = 2.9f, BottomRadius = 2.9f, Height = .15f, RadialSegments = 32 };
        BogCircuit.MeshAt(garage, inner, new(0, -.05f, 0), Vector3.One, metal);
        BogCircuit.MeshAt(garage, inner, new(0, -.032f, 0), new(.93f, 1, .93f), BogCircuit.Paint("90744e"));
        var rock = new SphereMesh { RadialSegments = 7, Rings = 3 };
        var dirt = BogCircuit.Paint("787454");
        var green = BogCircuit.Paint("647b43");
        for (int i = 0; i < 19; i++)
        {
            float theta = i * Mathf.Tau / 19;
            float radius = 7 + i % 5 * 2.3f;
            var p = new Vector3(Mathf.Cos(theta) * radius, 0, Mathf.Sin(theta) * radius);
            BogCircuit.MeshAt(garage, rock, p, new(1.8f + i % 3, .7f + i % 4 * .44f, 1.8f), i % 2 == 0 ? dirt : green);
            if (i % 3 == 0)
            {
                BogCircuit.Box(garage, p + Vector3.Up * 1.8f, new(.31f, 3.6f, .31f), BogCircuit.Paint("574a34"));
                BogCircuit.MeshAt(garage, rock, p + Vector3.Up * 4.4f, new(4.4f, 3.6f, 3.7f), green);
            }
        }
    }

    private void RebuildShowcase() => RebuildShowcase(PlayerLook(), aceRider?.Name ?? "You");

    private CharacterLookDefinition PlayerLook() => aceRider?.Resolve(assets) ?? creation.Resolve(appearance);

    private void RebuildShowcase(CharacterLookDefinition look, string name)
    {
        var replacement = new MountedRacer(assets, RaceMount.Get(selectedMount), GromnieLook(skinIndex), look, name,
            RaceWeapon.All[weaponIndex], RaceShield.All[shieldIndex], SelectedMountColor);
        if (showcase is not null) { garage.RemoveChild(showcase.Root); showcase.Root.QueueFree(); }
        showcase = replacement;
        garage.AddChild(replacement.Root);
        replacement.DrawGarage(0, previewYaw);
    }

    private void StartRace()
    {
        if (!PrepareRaceCourse()) return;
        if (track.IsJester) ApplyJesterAtmosphere();
        else if (track.IsKaleidoscope) ApplyKaleidoscopeAtmosphere();
        else if (track.IsVoid) ApplyVoidAtmosphere();
        else ApplyWeather(WeatherPresets[onlinePlan?.Weather ?? (testMode ? 0 : RollRaceWeather())]);
        courseUi.Hide();
        UpdateRaceCourseLabels();
        ClearRacers();
        var names = new[] { "You", "Sir Chomps", "Puddle Pete", "Mudonna", "The Honk" };
        int[] palette = [skinIndex, 1, 2, 3, 7];
        RaceMountKind[] order = [RaceMountKind.Grievver, RaceMountKind.Asheron, RaceMountKind.Mukkir,
            RaceMountKind.Cow, RaceMountKind.Shreth, RaceMountKind.Chicken, RaceMountKind.Mattekar,
            RaceMountKind.Rabbit, RaceMountKind.Reedshark, RaceMountKind.Auroch, RaceMountKind.Gromnie, RaceMountKind.BaelZharon,
            RaceMountKind.FestivusCarpet, RaceMountKind.IsinDuleDisc, RaceMountKind.CarenziSkates, RaceMountKind.Siraluun,
            RaceMountKind.Turkey, RaceMountKind.Tusker, RaceMountKind.SnowTusker];
        var rivals = order.Where(mount => mount != selectedMount).ToArray();
        for (int i = 0; i < RacerCount; i++)
        {
            var look = i == 0 ? PlayerLook() : creation.Resolve(RivalLook(i));
            var mount = RaceMount.Get(i == 0 ? selectedMount : rivals[(Math.Max(0, activeTown) % rivals.Length + i - 1) % rivals.Length]);
            int weapon = i == 0 ? weaponIndex : i switch { 1 => 1, 2 => 2, 3 => 3, _ => 6 };
            int shield = i == 0 ? shieldIndex : (i + 1) % RaceShield.All.Length;
            var racer = onlinePlan is { } plan ? CreateOnlineRacer(plan.Slots[i].Loadout) :
                new MountedRacer(assets, mount, GromnieLook(palette[i]), look,
                    i == 0 ? aceRider?.Name ?? names[i] : names[i], RaceWeapon.All[weapon], RaceShield.All[shield],
                    i == 0 ? SelectedMountColor : track.IsWaterCourse ? i : palette[i] % RaceMount.ColorsFor(mount.Kind).Count,
                    jetSki: track.IsWaterCourse, supportsJetSki: track.HasWaterSections);
            course.AddChild(racer.Root);
            if (racer.JetSkiVisual is { } ski) AddIslandReflectionLayers(ski);
            if (track.IsSnowboardCourse) racer.EnableSnowboard(assets, i);
            if (track.IsSprint) AddRiverReflectionLayer(racer.Root);
            racers.Add(racer);
        }
        if (track.IsVoid) AddVoidRiderLight(Player.Root);
        garage.Hide();
        course.Show();
        stableUi.Hide();
        resultsUi.Hide();
        pauseUi.Hide();
        raceUi.Show();
        UpdateOnlineRaceButtons();
        ResetRace();
        if (AndroidGraphics) ApplyRaceUpscaling(GetViewport());
        SetRaceCamera(1);
        UpdateSpanReflection();
        if (track.IsVoidBattle) WarmVoidRaceGraphics();
        if (AndroidGraphics) GD.Print($"Android race ready: {track.Name}.");
        if (YanshiPerformanceProfile)
            GD.Print($"Yanshi Android profile: scale {GetViewport().Scaling3DScale:P0}, cap {Engine.MaxFps} FPS.");
    }

    private CharacterAppearance RivalLook(int index)
    {
        var heritage = creation.Rows[(index * 2) % creation.Rows.Count];
        var body = heritage.Genders[index % heritage.Genders.Count];
        return new CharacterAppearance
        {
            NativeHeritageId = heritage.Id, GenderId = body.Id, SkinShade = .2f * index,
            HairStyle = index % Math.Max(1, body.HairStyles.Count),
            HeadgearStyle = index % Math.Max(1, body.Headgears.Count),
            ShirtStyle = index % Math.Max(1, body.Shirts.Count),
            ShirtColor = (index * 3) % Math.Max(1, body.ClothingColors.Count),
            TrousersStyle = index % Math.Max(1, body.Pants.Count),
            FootwearStyle = index % Math.Max(1, body.Footwear.Count)
        };
    }
    private void ClearRacers()
    {
        ClearUlgrimEffects();
        foreach (var racer in racers) { course.RemoveChild(racer.Root); racer.Root.QueueFree(); }
        racers.Clear();
    }

    private void OpenStable()
    {
        CloseTouchLayout(save: false);
        bool returnToEditor = editorPreview is not null;
        if (returnToEditor) ReturnFromTrackTest();
        if (loaded && rivalLines.Dirty) SaveGarage();
        racing = paused = finished = false;
        if (AndroidGraphics) ApplyRaceUpscaling(GetViewport());
        ResetVoidProgress();
        StopSpanReflection();
        StopIslandWaterReflection();
        ResetRegattaFluid();
        StopJesterReflection();
        ApplyWeather(WeatherPresets[0]);
        raceAudio?.Clear();
        ClearSpeedBlur();
        ClearRabbitCamera();
        ClearRacers();
        course.Hide();
        garage.Show();
        raceUi.Hide();
        pauseUi.Hide();
        resultsUi.Hide();
        datUi.Hide();
        stableUi.Show();
        PlayMusic(MainThemeMusic);
        UpdateStableLabels();
        AimGarage();
        if (returnToEditor) ShowTrackEditor();
    }

    private void AimGarage()
    {
        camera.Projection = Camera3D.ProjectionType.Perspective;
        camera.Near = .1f;
        tiltShiftOverlay.Hide();
        float lift = selectedMount switch
        {
            RaceMountKind.Asheron => .43f,
            RaceMountKind.BaelZharon => .59f,
            RaceMountKind.Tusker or RaceMountKind.SnowTusker => .34f,
            RaceMountKind.Siraluun => .22f,
            _ => 0
        };
        var target = garage.Position + new Vector3(-1.77f, 1.43f + lift, -.12f);
        camera.Position = garage.Position + new Vector3(0, 2.87f + lift, 8.05f + lift);
        camera.Fov = 48;
        camera.LookAt(target);
    }

    private void SetRaceCamera(float weight)
    {
        if (jesterCameraSnap)
        {
            weight = 1;
            jesterCameraSnap = false;
        }
        PositionRaceCamera(weight);
        if (track.HasWaterSections) UpdateIslandWaterReflection();
        UpdateJesterMirrors();
        UpdateRaceViewUi();
        UpdateCheckpointArchVisibility();
    }

    private void PositionRaceCamera(float weight)
    {
        float viewDistance = track.IsKaleidoscope ? 550 : track.IsVoid ? 2900 : track.IsWaterCourse ? 1850
            : track.IsCustom ? Mathf.Clamp(track.NativeBounds.Size.X * 1.25f, 470, 2600)
            : track.IsXL ? 2200 : track.IsSprint ? 1500 : track.IsMedium ? 1850 : 470;
        if (YanshiPerformanceProfile)
            viewDistance = Math.Min(viewDistance, Player.Flight is not null ? 550 : 400);
        else if (AndroidGraphics)
            viewDistance = Math.Min(viewDistance, track.IsVoid || Player.Flight is not null ? 1250 : 850);
        if (camera.Far != viewDistance) camera.Far = viewDistance;
        camera.Projection = raceView == RaceView.Isometric ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective;
        camera.Near = raceView == RaceView.FirstPerson ? .12f : .1f;
        bool wide = raceView == RaceView.WideChase;
        bool airborne = Player.Flight is not null;
        float pace = VisualPace(Player.Speed, airborne, Player.UsesJetSki);
        float highSpeed = airborne ? 0 : RoadSpeedCue(Player.Speed, Player.UsesJetSki);
        if (highSpeed > 0 && weight < 1)
            weight = Mathf.Lerp(weight, 1 - MathF.Pow(1 - weight, 2.35f), highSpeed);
        if (raceView is not (RaceView.Chase or RaceView.WideChase))
        {
            SetAlternateRaceCamera(weight);
            return;
        }
        if (Player.EjectTime > 0 && (OnlineGuest ? Player.Rider.GlobalPosition.Y : Player.EjectMotion.Launch.Origin.Y) -
            track.Point(Player.Distance).Y > 9)
        {
            Vector3 focus = Player.Rider.GlobalPosition;
            Vector3 away = (new Vector3(camera.Position.X, focus.Y, camera.Position.Z) - focus).Normalized();
            camera.Projection = Camera3D.ProjectionType.Perspective;
            camera.Position = KeepCameraAboveRoad(camera.Position.Lerp(focus + away * 13 + Vector3.Up * 4.8f, weight), 1.35f);
            AimRaceCamera(focus, weight);
            return;
        }
        if (Player.Flight is { } flight)
        {
            var right = flight.Heading.Cross(Vector3.Up);
            var desiredAir = flight.Position - flight.Heading * (wide ? 22 : 18) + right * 2.6f + Vector3.Up * (wide ? 10 : 7.5f);
            desiredAir = KeepCameraAboveRoad(desiredAir, 3.65f);
            camera.Position = KeepCameraAboveRoad(camera.Position.Lerp(desiredAir, weight), 3.65f);
            camera.Fov = Mathf.Lerp(camera.Fov, 63 + pace * 6, weight);
            var look = flight.Position + flight.Heading * 8 - Vector3.Up * 1.6f;
            AimRaceCamera(look, weight);
            return;
        }
        Vector3 heading = Player.Heading(track);
        chaseYaw = Mathf.LerpAngle(chaseYaw, BogCircuit.Yaw(heading), weight);
        Vector3 ahead = Vector3.Forward.Rotated(Vector3.Up, chaseYaw);
        Vector3 focusPoint = Player.Root.Position;
        if (Player.Falling)
            focusPoint.Y = Math.Max(focusPoint.Y, Math.Max(track.GroundOrRoadHeight(focusPoint), track.Point(Player.Distance).Y - 2.5f));
        float lookDistance = (wide ? 5.5f : 4) + pace * 1.65f + highSpeed * 10;
        Vector3 lead = ahead * lookDistance;
        if (!Player.FreeDriving)
        {
            Vector3 road = track.Point(Player.Distance);
            Vector3 lookDownRoad = track.Point(Player.Distance + Mathf.Lerp(4, lookDistance, highSpeed)) - road;
            lead = lead.Lerp(lookDownRoad with { Y = 0 }, highSpeed * .60f);
            lead.Y = Mathf.Clamp(lookDownRoad.Y, -1.45f, 1.55f);
        }
        Vector3 target = focusPoint + lead + Vector3.Up * 1.35f;
        float summitReveal = 0;
        if (track.IsVoidBattle)
        {
            Vector3 inward = new Vector3(-focusPoint.X, 0, -focusPoint.Z).Normalized();
            summitReveal = Mathf.SmoothStep(.28f, .65f, ahead.Dot(inward));
            Vector3 towardBael = ahead.Lerp(inward, .30f).Normalized();
            target += (towardBael * 11 + Vector3.Up * 6.5f) * summitReveal;
        }
        float chaseHeight = track.IsSpan(Player.Distance) ? 3.25f : 4.65f;
        Vector3 desired = focusPoint - ahead * ((wide ? 12.6f : 8.1f) + track.WidthAt(Player.Distance) * .14f + pace * .8f + highSpeed * 2.8f)
            + Vector3.Up * (chaseHeight + (wide ? 2.45f : 0) - pace * (track.IsSpan(Player.Distance) ? .24f : .52f) + highSpeed * .8f);
        if (Player.Rig.Kind == RaceMountKind.BaelZharon)
            desired += Vector3.Up * 1.35f + ahead.Cross(Vector3.Up) * .85f;
        Vector3 riderFocus = focusPoint + Vector3.Up * .58f;
        desired = FitChaseCamera(desired, riderFocus);
        camera.Position = weight >= 1 ? desired : FitChaseCamera(camera.Position.Lerp(desired, weight), riderFocus);
        camera.Fov = Mathf.Lerp(camera.Fov, (wide ? 63 : 58) + pace * 6.5f + highSpeed * 5.6f +
            (Player.Boost > 0 ? 3.8f : 0) + summitReveal * 5f, weight);
        AimRaceCamera(target, weight);
    }

    private void AimRaceCamera(Vector3 target, float weight)
    {
        Vector3 direction = target - camera.Position;
        if (!direction.IsFinite() || direction.LengthSquared() <= .01f) return;

        Vector3 forward = direction.Normalized();
        Vector3 up = Math.Abs(forward.Y) > .999f ? Vector3.Forward : Vector3.Up;
        Quaternion desired = Basis.LookingAt(forward, up).GetRotationQuaternion().Normalized();
        if (weight < 1)
        {
            // Smooth only rotation so accumulated basis scale cannot enter the next frame.
            Quaternion current = camera.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
            desired = current.Slerp(desired, weight).Normalized();
        }
        camera.Basis = new Basis(desired);
    }

    private Vector3 ClearCameraSight(Vector3 cameraPoint, Vector3 riderFocus)
    {
        // A native ridge can stand between two individually clear positions.
        // Lift the chase camera above the entire sight line to the saddle.
        cameraPoint = KeepCameraAboveRoad(cameraPoint, .75f);
        if (new Vector2(cameraPoint.X - riderFocus.X, cameraPoint.Z - riderFocus.Z).LengthSquared() > 26 * 26) return cameraPoint;
        riderFocus = KeepCameraAboveRoad(riderFocus, .35f);
        float rise = 0;
        int segments = Mathf.Clamp(Mathf.CeilToInt(cameraPoint.DistanceTo(riderFocus) / .70f), 14, 40);
        for (int i = 1; i < segments; i++)
        {
            float t = i / (float)segments;
            Vector3 sample = cameraPoint.Lerp(riderFocus, t);
            float floor = track.GroundOrRoadHeight(sample) + .30f;
            rise = Math.Max(rise, (floor - sample.Y) / (1 - t));
        }
        return cameraPoint + Vector3.Up * rise;
    }
    public override void _PhysicsProcess(double delta)
    {
        if (!loaded) return;
        float dt = (float)delta;
        worldTime += dt;
        if (!racing)
        {
            showcase?.DrawGarage(dt, previewYaw);
            return;
        }
        if (OnlineRace)
        {
            if (!onlineBegun) return;
            if (onlineWaiting.Visible) onlineWaiting.Hide();
            if (OnlineGuest) { TickOnlineGuest(dt); return; }
        }
        if (finished || (paused && !OnlineRace)) return;
        float steer = paused ? 0 : LocalSteering;
        TickRace(dt, !paused && Has(Key.W, Key.Up), paused || Has(Key.S, Key.Down), steer,
            !paused && IsHeld(Key.Space), useItem);
        useItem = false;
        if (OnlineRace) SendOnlineState(dt);
        if (!racing || voidFinaleCamera || (AndroidGraphics && !OnlineRace)) return;
        UpdateRacePresentation(dt);
    }

    private void UpdateRacePresentation(float dt)
    {
        SetRaceCamera(1 - Mathf.Exp(-8.2f * dt));
        UpdateSpanReflection();
        UpdateSpeedBlur(dt);
        UpdateRaceUi();
    }

    public override void _Input(InputEvent input)
    {
        if (HandleRaceTouchInput(input))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (input is InputEventKey key && !key.Echo)
        {
            Key code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
            if (key.Pressed) pressed.Add(code);
            else pressed.Remove(code);
            if (!key.Pressed) return;
            if (trackEditorUi.Visible)
            {
                if (HandleTrackEditorKey(code, key)) GetViewport().SetInputAsHandled();
                return;
            }
            if (aceRiderUi.Visible && code == Key.Escape)
            {
                CloseAceRider();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (courseUi.Visible && code == Key.Escape)
            {
                CloseCoursePicker();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (onlinePublicBrowseUi.Visible && code == Key.Escape)
            {
                ClosePublicRaceBrowser();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (onlineUi.Visible && code == Key.Escape)
            {
                ExitOnline();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (HandleRaceControl(code))
                GetViewport().SetInputAsHandled();
        }
    }

    private bool HandleRaceControl(Key code)
    {
        if (!racing || finished) return false;
        if (code == Key.Escape)
        {
            if (paused && EditingTouchLayout) CloseTouchLayout(save: false, focusMenu: true);
            else if (paused && raceSettingsMenu.IsVisibleInTree()) CloseRaceSettings(focusMenu: true);
            else SetPaused(!paused);
            return true;
        }
        if (paused) return false;
        if (Player.Finish >= 0 && code != Key.V) return false;
        if (code == Key.F) useItem = true;
        if (code == Key.R && Player.Flight is not null) requestRoll = true;
        if (code == Key.C && countdown <= 0) requestGuard = true;
        if (code == Key.V) CycleRaceView();
        if (code is Key.Q or Key.E) pendingSwipe = code == Key.Q ? -1 : 1;
        return code is Key.Space or Key.Up or Key.Down or Key.Left or Key.Right or Key.Q or Key.E or Key.C or Key.F or Key.R or Key.V;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            FlushRaceSettings();
            StopJesterReflection();
            StopSoundtrack();
        }
        if (what != NotificationApplicationFocusOut || testMode) return;
        pressed.Clear();
        ClearTouchInput();
        if (racing && !paused && !finished) SetPaused(true);
    }

    private bool IsHeld(Key key) => pressed.Contains(key) || IsTouchHeld(key);
    private bool Has(Key a, Key b) => IsHeld(a) || IsHeld(b);

    private void SetPaused(bool value)
    {
        paused = value;
        raceAudio?.Pause(value);
        if (paused) ClearSpeedBlur();
        useItem = false;
        pendingSwipe = 0;
        requestRoll = false;
        requestGuard = false;
        CloseTouchLayout(save: false);
        CloseRaceSettings();
        pauseUi.Visible = value;
        if (value) resumeRace.GrabFocus();
        if (raceTouchUi is not null) raceTouchUi.Visible = !value;
        UpdateRaceViewUi();
        UpdateRabbitCameraVisibility();
    }

    private sealed record GarageSave(int Skin, CharacterAppearance Appearance, float Best, string DatFolder,
        int? Course = null, Dictionary<int, float>? CourseBests = null, int Weapon = 0,
        int CourseLayout = 0, Dictionary<int, float>? LegacyCourseBests = null, int XlLayout = 0,
        RaceMountKind Mount = RaceMountKind.Gromnie, int MediumLayout = 0,
        Dictionary<int, Dictionary<int, RivalLineMemory.Line>>? RivalLines = null,
        AceRiderSnapshot? AceRider = null, string? AceEndpoint = null, int Shield = 0, int SiraluunSkin = 0,
        Dictionary<RaceMountKind, int>? MountColors = null, int SnowboardLayout = 0);

    private void RestoreGarage()
    {
        try
        {
            if (!FileAccess.FileExists(SaveFile)) return;
            var save = JsonSerializer.Deserialize<GarageSave>(FileAccess.GetFileAsString(SaveFile));
            if (save is null) return;
            RestoreMountColors(save);
            if (Enum.IsDefined(save.Mount)) selectedMount = save.Mount;
            previewYaw = MountPreviewYaw(selectedMount);
            appearance = save.Appearance ?? new CharacterAppearance();
            CopyBestTimes(save.LegacyCourseBests, legacyCourseBests);
            if (save.CourseBests is not null)
                foreach (var (course, time) in save.CourseBests)
                    if (course >= 0 && float.IsFinite(time) && time > 0)
                    {
                        bool sameLayout = save.CourseLayout == CourseLayoutVersion && course > 0 && !IsPreviousVoidCourse(course) &&
                            (!SnowboardCourses.ContainsKey(course) || save.SnowboardLayout == SnowboardLayoutVersion) &&
                            (course >= XlCourseOffset ? save.XlLayout == XlLayoutVersion : save.MediumLayout == MediumLayoutVersion);
                        var destination = sameLayout ? bestByCourse : legacyCourseBests;
                        destination[course] = time;
                    }
            if (float.IsFinite(save.Best) && save.Best > 0) legacyCourseBests.TryAdd(0, save.Best);
            if (save.CourseLayout == CourseLayoutVersion)
                rivalLines.Restore(save.RivalLines, id => !IsPreviousVoidCourse(id) &&
                    (!SnowboardCourses.ContainsKey(id) || save.SnowboardLayout == SnowboardLayoutVersion) &&
                    (id >= XlCourseOffset ? save.XlLayout == XlLayoutVersion : save.MediumLayout == MediumLayoutVersion));
            selectedTown = save.Course is { } savedCourse && IsPreviousVoidCourse(savedCourse) ? VoidCourseId : save.Course ?? selectedTown;
            weaponIndex = Mathf.Clamp(save.Weapon, 0, RaceWeapon.All.Length - 1);
            shieldIndex = Mathf.Clamp(save.Shield, 0, RaceShield.All.Length - 1);
            datFolder = save.DatFolder ?? "";
            aceRider = save.AceRider;
            aceEndpoint = save.AceEndpoint is { } address && AceRiderSnapshot.TryEndpoint(address, out _) ? address : "";
        }
        catch (JsonException) { }
    }

    private static void CopyBestTimes(Dictionary<int, float>? source, Dictionary<int, float> destination)
    {
        if (source is null) return;
        foreach (var (course, time) in source)
            if (course >= 0 && float.IsFinite(time) && time > 0) destination[course] = time;
    }
    private void SaveGarage()
    {
        if (testMode) return;
        string temp = SaveFile + ".tmp";
        try
        {
            using (var file = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
            {
                if (file is null) return;
                var save = new GarageSave(skinIndex, appearance, bestByCourse.GetValueOrDefault(0, -1), datFolder,
                    editorPreview is null ? selectedTown : editorReturnCourse, bestByCourse, weaponIndex, CourseLayoutVersion, legacyCourseBests, XlLayoutVersion, selectedMount,
                    MediumLayoutVersion, rivalLines.SavedLines, aceRider, aceEndpoint, shieldIndex,
                    MountColorFor(RaceMountKind.Siraluun), mountColors, SnowboardLayoutVersion);
                file.StoreString(JsonSerializer.Serialize(save));
            }
            System.IO.File.Move(ProjectSettings.GlobalizePath(temp), ProjectSettings.GlobalizePath(SaveFile), overwrite: true);
            rivalLines.MarkSaved();
        }
        catch (IOException error) { GD.PushWarning(error.Message); }
    }

    public override void _ExitTree()
    {
        CancelNativeAce();
        StopSoundtrack();
        if (loaded && rivalLines.Dirty) SaveGarage();
        raceSocket?.Close();
        raceSocket?.Dispose();
        if (track is { } active && (active.IsCustom || active.IsDismountSprint || active.IsWaterCourse || active.IsSnowboardCourse))
            active.Curve.Dispose();
        assets?.Dispose();
        archive?.Dispose();
        townCells?.Dispose();
    }
}


















