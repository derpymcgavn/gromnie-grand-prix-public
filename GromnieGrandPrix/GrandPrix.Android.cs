using Godot;

public partial class GrandPrix
{
    private readonly HashSet<Key> touchPressed = [];
    private Control? raceTouchUi;
    private Label? touchGo, touchBrake;
    private bool touchFlightMode;
    private Control androidDatUi = null!;
    private Label androidDatStatus = null!;
    private ProgressBar androidDatProgress = null!;
    private Button androidDatRetry = null!;

    private void MakeRaceTouchUi()
    {
        var controls = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        raceTouchUi = controls;
        raceUi.AddChild(controls);
        controls.VisibilityChanged += () =>
        {
            if (!controls.IsVisibleInTree()) touchPressed.Clear();
        };

        AddTouchControl(controls, "LEFT", Key.A, 28, 696, 106, 80, held: true);
        AddTouchControl(controls, "RIGHT", Key.D, 146, 696, 106, 80, held: true);
        AddTouchControl(controls, "DRIFT", Key.Space, 28, 600, 106, 80, held: true);
        touchBrake = AddTouchControl(controls, "BRAKE", Key.S, 1008, 696, 110, 80, held: true);
        touchGo = AddTouchControl(controls, "GO", Key.W, 1130, 696, 122, 80, held: true);
        AddTouchControl(controls, "ITEM", Key.F, 1008, 600, 110, 80);
        AddTouchControl(controls, "AIR ROLL", Key.R, 1130, 600, 122, 80);
        AddTouchControl(controls, "HIT LEFT", Key.Q, 146, 600, 106, 80);
        AddTouchControl(controls, "HIT RIGHT", Key.E, 264, 600, 106, 80);
        AddTouchControl(controls, "GUARD", Key.C, 264, 696, 106, 80);
        AddTouchControl(controls, "CAMERA", Key.V, 827, 27, 104, 60);
        AddTouchControl(controls, "PAUSE", Key.Escape, 943, 27, 104, 60);
    }

    private void UpdateRaceTouchFlightLabels()
    {
        if (touchGo is null || touchBrake is null) return;
        bool flying = Player.Flight is not null && FreeFlightControls;
        if (flying == touchFlightMode) return;
        touchFlightMode = flying;
        touchGo.Text = flying ? "DIVE" : "GO";
        touchBrake.Text = flying ? "CLIMB" : "BRAKE";
    }

    private static readonly Color TouchActive = new("bedfa6");

    private Label AddTouchControl(Control parent, string title, Key key, int x, int y, int width, int height, bool held = false)
    {
        var size = new Vector2(width, height);
        var panel = new Panel
        {
            Position = new(x, y), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddThemeStyleboxOverride("panel", Surface("172821d9", "a4a66c", 15, 2));
        parent.AddChild(panel);
        var label = new Label
        {
            Size = size, Text = title, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddChild(label);
        var touch = new TouchScreenButton
        {
            Position = size * .5f,
            Shape = new RectangleShape2D { Size = size },
            ShapeCentered = false
        };
        panel.AddChild(touch);
        touch.Pressed += () =>
        {
            panel.Modulate = TouchActive;
            if (held) touchPressed.Add(key);
            else HandleRaceControl(key);
        };
        touch.Released += () =>
        {
            panel.Modulate = Colors.White;
            if (held) touchPressed.Remove(key);
        };
        return label;
    }

    private void MakeAndroidDatUi(CanvasLayer canvas)
    {
        androidDatUi = Layer(canvas);
        MakeModal(androidDatUi, "PREPARING RACE DATA", 260, 263);
        androidDatStatus = Text(androidDatUi, "", 420, 327, 440, 19, Cream, HorizontalAlignment.Center);
        androidDatStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        androidDatProgress = new ProgressBar { Position = new(429, 420), Size = new(422, 30) };
        androidDatUi.AddChild(androidDatProgress);
        androidDatRetry = Action(androidDatUi, "TRY AGAIN", 544, 431, 192, 52, StartAndroidGame, true);
        androidDatUi.Hide();
    }

    private async void StartAndroidGame()
    {
        stableUi.Hide();
        androidDatUi.Show();
        androidDatRetry.Hide();
        androidDatProgress.Show();
        androidDatProgress.Value = 0;
        androidDatStatus.Text = "Checking bundled race data…";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string folder = ProjectSettings.GlobalizePath("user://bundled-dats");
        var progress = new Progress<(long Bytes, long Total)>(state =>
        {
            androidDatStatus.Text = $"Unpacking race data · {state.Bytes / 1048576:N0} of {state.Total / 1048576:N0} MiB";
            androidDatProgress.Value = (double)state.Bytes * 100 / state.Total;
        });
        try
        {
            long copied = await Task.Run(() => AndroidDatInstall.Install(folder, progress));
            GD.Print($"Android DAT cache ready in {stopwatch.Elapsed.TotalSeconds:F2} s ({copied:N0} bytes unpacked).");
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            ShowAndroidDatError(error is IOException
                ? "Could not unpack the race data. Free some internal storage and try again."
                : "The bundled race data is missing or damaged.");
            return;
        }
        androidDatStatus.Text = "Opening the stable…";
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        androidDatUi.Hide();
        LoadGame(folder);
        if (loaded) GD.Print($"Android stable ready in {stopwatch.Elapsed.TotalSeconds:F2} s.");
    }

    private void ShowAndroidDatError(string message)
    {
        stableUi.Hide();
        androidDatUi.Show();
        androidDatStatus.Text = message;
        androidDatProgress.Hide();
        androidDatRetry.Show();
    }
}
