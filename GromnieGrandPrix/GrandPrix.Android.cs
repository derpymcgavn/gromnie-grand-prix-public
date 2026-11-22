using Godot;

public partial class GrandPrix
{
    private readonly HashSet<Key> touchPressed = [];
    private Control? raceTouchUi;
    private Label? touchGo, touchBrake;
    private Panel touchDrift = null!, touchAirRoll = null!, touchItem = null!;
    private Control touchSwipes = null!;
    private bool touchAirborne, touchFlightMode;
    private Control androidDatUi = null!;
    private Label androidDatStatus = null!;
    private ProgressBar androidDatProgress = null!;
    private Button androidDatRetry = null!;
    private FileDialog androidDatPicker = null!;
    private bool androidDatBusy;

    private void MakeRaceTouchUi()
    {
        var controls = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        raceTouchUi = controls;
        raceUi.AddChild(controls);
        controls.VisibilityChanged += () =>
        {
            if (!controls.IsVisibleInTree()) touchPressed.Clear();
        };

        AddTouchControl(controls, "LEFT", Key.A, 28, 658, 146, 118, held: true, fontSize: 26);
        AddTouchControl(controls, "RIGHT", Key.D, 186, 658, 146, 118, held: true, fontSize: 26);
        AddTouchControl(controls, "GUARD", Key.C, 132, 574, 96, 70);
        touchSwipes = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        controls.AddChild(touchSwipes);
        AddTouchControl(touchSwipes, "HIT LEFT", Key.Q, 28, 574, 96, 70);
        AddTouchControl(touchSwipes, "HIT RIGHT", Key.E, 236, 574, 96, 70);

        touchBrake = AddTouchControl(controls, "BRAKE", Key.S, 990, 658, 103, 118, held: true, fontSize: 21);
        touchGo = AddTouchControl(controls, "THROTTLE", Key.W, 1105, 658, 147, 118, held: true, primary: true, fontSize: 23);
        touchItem = AddTouchControl(controls, "ITEM", Key.F, 990, 574, 103, 70).GetParent<Panel>();
        touchDrift = AddTouchControl(controls, "DRIFT", Key.Space, 1105, 574, 147, 70, held: true).GetParent<Panel>();
        touchAirRoll = AddTouchControl(controls, "AIR ROLL", Key.R, 1105, 574, 147, 70).GetParent<Panel>();
        touchAirRoll.Hide();

        AddTouchControl(controls, "CAMERA", Key.V, 827, 27, 104, 60);
        AddTouchControl(controls, "PAUSE", Key.Escape, 943, 27, 104, 60);
    }

    private void UpdateRaceTouchFlightLabels()
    {
        if (touchGo is null || touchBrake is null) return;
        bool airborne = Player.Flight is not null;
        if (airborne != touchAirborne)
        {
            touchAirborne = airborne;
            touchDrift.Visible = !airborne;
            touchAirRoll.Visible = airborne;
            touchSwipes.Visible = !airborne;
            touchItem.Visible = !airborne;
            if (airborne) touchPressed.Remove(Key.Space);
        }
        bool flying = airborne && FreeFlightControls;
        if (flying == touchFlightMode) return;
        touchFlightMode = flying;
        touchGo.Text = flying ? "DIVE" : "THROTTLE";
        touchBrake.Text = flying ? "CLIMB" : "BRAKE";
    }

    private bool IsTouchHeld(Key key) => touchPressed.Contains(key) ||
        key == Key.W && !touchAirborne && touchPressed.Contains(Key.Space);

    private Label AddTouchControl(Control parent, string title, Key key, int x, int y, int width, int height,
        bool held = false, bool primary = false, int fontSize = 18)
    {
        var size = new Vector2(width, height);
        var normal = primary ? Surface("efd196eb", "fff3c8", 21, 2) : Surface("172821dc", "a4a66c", 18, 2);
        var active = Surface("c9e8ac", "f3ffdf", 18, 2);
        var ink = new Color("172921");
        var panel = new Panel
        {
            Name = $"Touch_{key}", Position = new(x, y), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddThemeStyleboxOverride("panel", normal);
        parent.AddChild(panel);
        var label = new Label
        {
            Size = size, Text = title, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", primary ? ink : Cream);
        panel.AddChild(label);
        var touch = new TouchScreenButton
        {
            Position = size * .5f,
            Shape = new RectangleShape2D { Size = size },
            ShapeCentered = false,
            PassbyPress = held
        };
        panel.AddChild(touch);
        touch.Pressed += () =>
        {
            panel.AddThemeStyleboxOverride("panel", active);
            label.AddThemeColorOverride("font_color", ink);
            if (held) touchPressed.Add(key);
            else HandleRaceControl(key);
        };
        touch.Released += () =>
        {
            panel.AddThemeStyleboxOverride("panel", normal);
            label.AddThemeColorOverride("font_color", primary ? ink : Cream);
            if (held) touchPressed.Remove(key);
        };
        return label;
    }

    private void MakeAndroidDatUi(CanvasLayer canvas)
    {
        androidDatUi = Layer(canvas);
        MakeModal(androidDatUi, "RACE DATA", 260, 263);
        androidDatStatus = Text(androidDatUi, "", 420, 327, 440, 19, Cream, HorizontalAlignment.Center);
        androidDatStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        androidDatProgress = new ProgressBar { Position = new(429, 420), Size = new(422, 30) };
        androidDatUi.AddChild(androidDatProgress);
        androidDatRetry = Action(androidDatUi, "CHOOSE FOLDER", 510, 431, 260, 52, ChooseAndroidData, true);
        androidDatPicker = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenDir,
            UseNativeDialog = true
        };
        AddChild(androidDatPicker);
        androidDatPicker.DirSelected += ImportAndroidData;
        androidDatUi.Hide();
    }

    private static string AndroidDataFolder => ProjectSettings.GlobalizePath(
        AndroidDatInstall.HasBundle ? "user://bundled-dats" : "user://imported-dats");

    private void ChooseAndroidData()
    {
        if (androidDatBusy) return;
        if (AndroidDatInstall.HasBundle) StartAndroidGame();
        else androidDatPicker.Popup();
    }

    private void StartAndroidGame()
    {
        if (AndroidDatInstall.HasBundle || AndroidDatInstall.HasImport(AndroidDataFolder))
            PrepareAndroidData();
        else
            ShowAndroidDatError("Choose the folder with client_portal.dat and client_cell_1.dat from your Asheron's Call installation.");
    }

    private void ImportAndroidData(string uri) => PrepareAndroidData(uri);

    private async void PrepareAndroidData(string? importUri = null)
    {
        if (androidDatBusy) return;
        androidDatBusy = true;
        stableUi.Hide();
        androidDatUi.Show();
        androidDatRetry.Hide();
        androidDatProgress.Show();
        androidDatProgress.Value = 0;
        androidDatStatus.Text = importUri is null ? "Checking race data…" : "Importing race data…";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string folder = AndroidDataFolder;
        var progress = new Progress<(long Bytes, long Total)>(state =>
        {
            androidDatStatus.Text = $"Copying race data · {state.Bytes / 1048576:N0} of {state.Total / 1048576:N0} MiB";
            androidDatProgress.Value = (double)state.Bytes * 100 / state.Total;
        });
        try
        {
            if (importUri is not null)
                await Task.Run(() => AndroidDatInstall.Import(importUri, folder, progress));
            else if (AndroidDatInstall.HasBundle)
            {
                long copied = await Task.Run(() => AndroidDatInstall.Install(folder, progress));
                GD.Print($"Android DAT cache ready in {stopwatch.Elapsed.TotalSeconds:F2} s ({copied:N0} bytes unpacked).");
            }
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            androidDatBusy = false;
            ShowAndroidDatError(error is FileNotFoundException ? error.Message
                : error is IOException
                    ? "Could not copy the race data. Check the files and free internal storage."
                    : "The race data could not be read. Select a folder with a valid pair of Asheron's Call DAT files.");
            return;
        }
        androidDatStatus.Text = "Opening the stable…";
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        androidDatUi.Hide();
        LoadGame(folder);
        androidDatBusy = false;
        if (loaded) GD.Print($"Android stable ready in {stopwatch.Elapsed.TotalSeconds:F2} s.");
    }

    private void ShowAndroidDatError(string message)
    {
        stableUi.Hide();
        androidDatUi.Show();
        androidDatStatus.Text = message;
        androidDatProgress.Hide();
        androidDatRetry.Text = AndroidDatInstall.HasBundle ? "TRY AGAIN" : "CHOOSE FOLDER";
        androidDatRetry.Show();
    }
}
