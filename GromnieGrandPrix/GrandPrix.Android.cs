using Godot;

public partial class GrandPrix
{
    private Control androidDatUi = null!;
    private Label androidDatStatus = null!;
    private ProgressBar androidDatProgress = null!;
    private Button androidDatRetry = null!;
    private FileDialog androidDatPicker = null!;
    private bool androidDatBusy;

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
