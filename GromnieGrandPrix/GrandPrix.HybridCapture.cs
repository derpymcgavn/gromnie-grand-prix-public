using Godot;

public partial class GrandPrix
{
    private async Task CaptureHybridAndSnowboard(string folder)
    {
        var layout = HybridTestRoute();
        customTracks[layout.Id] = layout;
        LoadTrackDraft(layout);
        ShowTrackEditor();
        await SaveFrame(Path.Combine(folder, "Hybrid-Track-Editor.png"));
        HideTrackEditor();
        ChooseCourse(layout.Id);
        StartRace();
        Require(racing && track.HasHybridTravel, "load hybrid capture course " + courseProblem);
        SetPhysicsProcess(false);
        countdown = 0;
        raceUi.Hide();

        var water = track.TravelSections.First(section => section.Mode == TrackTravelMode.JetSki);
        Player.Land();
        Player.Distance = water.Start + 16;
        Player.Speed = 27;
        SyncHybridTravel(Player, quiet: true);
        for (int i = 0; i < 42; i++) TickRace(1f / 60, true, false, .22f, false, false);
        Require(Player.UsesJetSki && Player.Flight is null, "the hybrid jet ski is active for capture");
        FrameRacer(3.7f, 1.9f);
        await SaveFrame(Path.Combine(folder, "Hybrid-Jet-Ski.png"));

        var air = track.TravelSections.First(section => section.Mode == TrackTravelMode.Flight);
        Player.Distance = (air.Start + air.End) * .5f;
        SyncHybridTravel(Player, quiet: true);
        Require(Player.Flight is not null, "the hybrid flyer is active for capture");
        TickRace(1f / 60, true, false, .35f, false, false);
        FrameRacer(6.4f, 3.3f);
        await SaveFrame(Path.Combine(folder, "Hybrid-Flight.png"));

        foreach (var (id, name) in SnowboardCourses)
        {
            if (!SnowboardCourseAvailable(id)) continue;
            OpenStable();
            ChooseCourse(id);
            StartRace();
            Require(racing && track.IsSnowboardCourse && Player.UsesSnowboard, "load snowboard capture course " + courseProblem);
            SetPhysicsProcess(false);
            countdown = 0;
            raceUi.Hide();
            Player.Distance = 88;
            Player.Lane = 0;
            Player.Speed = BoardPace;
            for (int i = 0; i < 75; i++)
                TickRace(1f / 60, true, false, i < 35 ? -.48f : .34f, true, false);
            FrameRacer(3.6f, 1.65f);
            await SaveFrame(Path.Combine(folder, name.Replace(' ', '-') + ".png"));
            if (id != MountEsperDownhillId) continue;

            ResetRace();
            countdown = 0;
            for (int frame = 0; frame < 540; frame++)
            {
                TickHybridCheck();
                Vector3 contact = Player.CoursePosition(track);
                if (Player.EjectTime > 0 || !Player.SnowboardAirborne ||
                    contact.Y - track.FreeGround(contact).Y < .65f) continue;
                FrameRacer(4.9f, 1.35f);
                await SaveFrame(Path.Combine(folder, "Mount-Esper-Crest.png"));
                break;
            }
        }
        GD.Print(SnowboardingEnabled ? "HYBRID AND SNOWBOARD CAPTURE PASS" : "HYBRID CAPTURE PASS");

        void FrameRacer(float side, float height)
        {
            Vector3 forward = Player.Heading(track);
            Vector3 focus = Player.Root.Position + Vector3.Up * 1.1f;
            Vector3 point = focus - forward * 5.2f + forward.Cross(Vector3.Up) * side + Vector3.Up * height;
            camera.GlobalPosition = KeepCameraAboveRoad(point, .72f);
            camera.Fov = 58;
            camera.LookAt(focus + forward * 1.1f);
            if (track.HasWaterSections) UpdateIslandWaterReflection();
        }
    }
}
