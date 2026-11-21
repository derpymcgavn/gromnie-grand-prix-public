using System.Text.Json;
using Godot;
using DerethSurvivors;
using Disk = System.IO.File;

public partial class GrandPrix
{
    private AceRiderSnapshot RiderTemplateFixture(string name)
    {
        var look = creation.Resolve(RivalLook(1));
        return new(1, name, look.Setup, look.Motion, look.Sound, look.Scale, look.BasePalette,
            look.Description.Palettes.ToArray(), look.Description.Textures.ToArray(), look.Description.Parts.ToArray());
    }

    private void CheckRiderTemplates()
    {
        Require(loaded, "DATs load for rider templates");
        var legacy = RiderTemplateFixture("Legacy Rider");
        aceRider = legacy;
        OpenAceRider();
        Require(riderTemplates.Count == 1 && riderTemplates[0].Imported == legacy,
            "the previously saved appearance migrates to a reusable template");
        Require(nativeAcePassword.Secret && nativeAceImportButton.Disabled, "native sign-in masks passwords and waits for a roster");
        SetNativeAceCharacters(["First Rider", "Second Rider"]);
        nativeAceCharacters.Select(1);
        Require(SelectedNativeAceCharacterIndex() == 1 && !nativeAceImportButton.Disabled,
            "the native character picker passes the chosen row to the server client");
        nativeAcePassword.Text = "temporary-password-fixture";
        ShowAceImportPane(templatePane);
        Require(nativeAcePassword.Text.Length == 0 && nativeAceCharacters.ItemCount == 0,
            "leaving the server tab drops its local credentials and roster");

        var imported = RiderTemplateFixture("Imported Racer");
        Require(TryApplyImportedRider(imported, out _) && TryApplyImportedRider(imported, out _) &&
                riderTemplates.Count == 2 && showcase!.Name == imported.Name,
            "import applies the character and creates one reusable saved look");
        riderTemplateName.Text = "Race Outfit";
        SaveCurrentRiderTemplate();
        UseCustomRider();
        OpenAceRider();
        ShowAceImportPane(templatePane);
        riderTemplateName.Text = "Stable Original";
        SaveCurrentRiderTemplate();
        string stored = JsonSerializer.Serialize(riderTemplates);
        riderTemplates = ParseRiderTemplates(stored);
        Require(riderTemplates.Count == 4 && !stored.Contains("temporary-password-fixture", StringComparison.Ordinal) &&
                riderTemplates[2].Imported?.Palettes.SequenceEqual(imported.Palettes) == true &&
                riderTemplates[3].Custom is not null, "only character appearance data survives the local save round-trip");
        RefreshRiderTemplates("Race Outfit");
        UseRiderTemplate();
        ChooseCourse(0);
        StartRace();
        Require(racing && Player.Name == imported.Name && Player.Rider.Setup.Id == imported.Setup,
            "a named appearance template can be equipped offline and raced");
        OpenStable();
        OpenAceRider();
        ShowAceImportPane(templatePane);
        RefreshRiderTemplates("Stable Original");
        UseRiderTemplate();
        Require(aceRider is null && showcase!.Name == "You", "a built-in rider appearance can also be saved and restored");
        GD.Print("RIDER TEMPLATES PASS | legacy migration, character picker, credential cleanup, offline restore and race.");
    }

    private async Task CaptureRiderTemplates(string folder)
    {
        Require(loaded, "DATs load for rider import capture");
        OpenAceRider();
        nativeAceAddress.Text = "shard.example.net:9000";
        SetNativeAceCharacters(["Training Rider", "Circuit Rider", "Stable Rider"]);
        nativeAceCharacters.Select(1);
        SetNativeAceBusy(false, "Choose the character you want to race with.");
        await SaveFrame(Path.Combine(folder, "Rider-Import.png"));
        TryApplyImportedRider(RiderTemplateFixture("Circuit Rider"), out _);
        ShowAceImportPane(templatePane);
        riderTemplateName.Text = "Race outfit";
        SaveCurrentRiderTemplate();
        await SaveFrame(Path.Combine(folder, "Rider-Templates.png"));
        CloseAceRider();
        GD.Print("RIDER IMPORT CAPTURE PASS");
    }

    private async void CheckAceRiderImport(string folder)
    {
        try
        {
            Require(loaded, "DATs are loaded for the ACE rider check");
            var look = creation.Resolve(RivalLook(1));
            const string riderName = "Race Import Fixture";
            var snapshot = new AceRiderSnapshot(1, riderName, look.Setup, look.Motion, look.Sound, look.Scale, look.BasePalette,
                look.Description.Palettes.ToArray(), look.Description.Textures.ToArray(), look.Description.Parts.ToArray());
            Require(snapshot.Palettes.Length > 0 && snapshot.Parts.Length > 0 && snapshot.Textures.Length > 0,
                "appearance fixture covers native dyes, geometry and texture substitutions");
            string file = Path.Combine(folder, "snapshot.json");
            Disk.WriteAllBytes(file + ".tmp", JsonSerializer.SerializeToUtf8Bytes(snapshot));
            Disk.Move(file + ".tmp", file, overwrite: true);

            string connection = Path.Combine(folder, "connection.txt");
            for (int i = 0; i < 100 && !Disk.Exists(connection); i++)
                await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            var lines = Disk.ReadAllLines(connection);
            OpenAceRider();
            ShowAceImportPane(acePairingPane);
            aceAddress.Text = lines[0];
            aceCode.Text = lines[1];
            FetchAceRider();
            for (int i = 0; i < 120 && aceConnect.Disabled; i++)
                await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            Require(aceRider?.Name == riderName && showcase!.Rider.Setup.Id == look.Setup &&
                showcase.Rider.NativeModelSwapCount > 0 && showcase.Rider.NativeTextureSwapCount > 0,
                "the stable imports the native model and its clothing over HTTP (" + aceStatus.Text + ")");
            Require(aceRider!.Palettes.SequenceEqual(snapshot.Palettes), "the native dye ranges survive transport");
            CloseAceRider();

            string relativeSave = Path.GetRelativePath(folder, ProjectSettings.GlobalizePath(SaveFile));
            Require(relativeSave.StartsWith("appdata" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "the import check uses an isolated stable save");
            testMode = false;
            try { SaveGarage(); }
            finally { testMode = true; }
            aceRider = null;
            aceEndpoint = "";
            RestoreGarage();
            Require(aceRider?.Name == riderName && aceEndpoint == lines[0] &&
                !Disk.ReadAllText(ProjectSettings.GlobalizePath(SaveFile)).Contains(lines[1], StringComparison.Ordinal),
                "the saved rider reloads offline without storing the code");

            ChooseCourse(0);
            StartRace();
            Require(Player.Name == riderName && Player.Rider.Setup.Id == look.Setup &&
                Player.Rider.NativeModelSwapCount == showcase!.Rider.NativeModelSwapCount,
                "the imported stable rider and outfit carry over into the race");
            OpenStable();
            UseCustomRider();
            Require(aceRider is null && showcase!.Name == "You" && showcase.Rider.Setup.Id == creation.Resolve(appearance).Setup,
                "switching back restores the custom rider");
            Disk.WriteAllText(Path.Combine(folder, "result.txt"), "PASS");
            GD.Print("ACE RIDER IMPORT PASS | HTTP pairing, native attire, offline save, racing and custom rider restore.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }
}
