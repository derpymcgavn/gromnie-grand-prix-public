using System.Net;
using Godot;

public partial class GrandPrix
{
    private async void CheckNativeAceImport(string address)
    {
        try
        {
            Require(TryAceServer(address, out string host, out _) && IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip),
                "the native rider fixture uses a loopback ACE server");
            const string profileArg = "--ace-native-profile=";
            string profile = OS.GetCmdlineUserArgs().Single(arg => arg.StartsWith(profileArg, StringComparison.Ordinal))[profileArg.Length..];
            string store = ProjectSettings.GlobalizePath(RiderTemplateFile);
            Require(!Path.IsPathRooted(Path.GetRelativePath(profile, store)) &&
                    !Path.GetRelativePath(profile, store).StartsWith("..", StringComparison.Ordinal) && !File.Exists(store),
                "the native rider fixture has a fresh isolated profile");
            Require(loaded, "DATs are loaded for the native rider fixture");

            const string character = "Fixture Gromnie Rider";
            const string password = "fixture";
            OpenAceRider();
            nativeAceAddress.Text = address;
            nativeAceAccount.Text = "fixture";
            nativeAcePassword.Text = password;
            ConnectNativeAce();
            await WaitFor(() => !nativeAceBusy, "the native ACE login response", 28);
            int row = Enumerable.Range(0, nativeAceCharacters.ItemCount)
                .FirstOrDefault(index => nativeAceCharacters.GetItemText(index) == character, -1);
            Require(row >= 0 && nativeAcePassword.Text.Length == 0 && nativeAcePassword.Secret,
                "native sign-in lists the fixture's character and clears the masked login field (" + nativeAceStatus.Text + ")");
            nativeAceCharacters.Select(row);
            testMode = false;
            ImportNativeAceCharacter();
            await WaitFor(() => !nativeAceBusy, "the character’s native appearance and logoff", 28);
            testMode = true;
            Require(aceRider?.Name == character && showcase?.Name == character && showcase.Rider.Setup.Id == 0x02000001 &&
                    nativeAceSession is null && File.Exists(store), "the selected native appearance applies, saves a template and signs out (" + nativeAceStatus.Text + ")");
            string saved = File.ReadAllText(store);
            Require(!saved.Contains($"\"{password}\"", StringComparison.Ordinal) && ParseRiderTemplates(saved).Single().Imported?.Name == character,
                "the offline template contains the appearance without the login password");
            UseCustomRider();
            OpenAceRider();
            riderTemplates = ParseRiderTemplates(saved);
            ShowAceImportPane(templatePane);
            UseRiderTemplate();
            ChooseCourse(0);
            StartRace();
            Require(racing && Player.Name == character && Player.Rider.Setup.Id == 0x02000001,
                "the saved template races offline without another connection");
            GD.Print("ACE NATIVE IMPORT PASS | stock UDP sign-in, roster, selected appearance, logout, saved template, offline race.");
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            QuitSoundtrackCheck();
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(1);
        }
    }
}
