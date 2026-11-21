using Godot;
using GromnieOnline;
using FileAccess = Godot.FileAccess;

public partial class GrandPrix
{
    private const string DefaultRaceRelay = "wss://ggprix.derptide.net/race";
    private Control onlineUi = null!, onlineSettings = null!, onlineRoom = null!, onlineWaiting = null!;
    private LineEdit onlineName = null!, onlineInvite = null!, onlineRelayUrl = null!;
    private Button onlineJoin = null!, onlineHost = null!, onlineStart = null!, onlineCourse = null!, onlineSettingsButton = null!;
    private Button onlineBrowseButton = null!, onlineVisibilitySetting = null!, onlineLobbyVisibility = null!;
    private Button pauseRestart = null!, resultsAgain = null!;
    private Label onlineStatus = null!, onlineCode = null!, onlineRoomCourse = null!;
    private ItemList onlineRoster = null!;

    private void MakeOnlineUi(CanvasLayer canvas)
    {
        Action(stableUi, "PLAY ONLINE", 1038, 168, 214, 41, OpenOnlineMenu);
        onlineUi = Layer(canvas);
        MakeModal(onlineUi, "ONLINE RACING", 118, 543);
        onlineSettings = new Control();
        onlineRoom = new Control();
        onlineUi.AddChild(onlineSettings);
        onlineUi.AddChild(onlineRoom);
        Text(onlineSettings, "Racer name", 420, 185, 434, 16);
        onlineName = OnlineField(onlineSettings, 212, "Racer", 24);
        Text(onlineSettings, "Invite code", 420, 265, 434, 16);
        onlineInvite = OnlineField(onlineSettings, 292, "Enter a code or paste an invite", 512);
        onlineInvite.TextSubmitted += _ => ConnectOnline(false);
        onlineHost = Action(onlineSettings, "HOST RACE", 420, 355, 205, 47, () => ConnectOnline(true));
        onlineJoin = Action(onlineSettings, "JOIN RACE", 643, 355, 217, 47, () => ConnectOnline(false), true);
        onlineBrowseButton = Action(onlineSettings, "FIND PUBLIC RACES", 420, 415, 440, 42, OpenPublicRaceBrowser);
        onlineVisibilitySetting = Action(onlineSettings, "ROOM · PRIVATE", 420, 468, 215, 32, ToggleRaceVisibility);
        onlineVisibilitySetting.TooltipText = "Private races are joined using an invite code. Public races appear in Find Public Races.";
        onlineSettingsButton = Action(onlineSettings, "RELAY SETTINGS", 649, 468, 211, 32, () => onlineRelayUrl.Visible = !onlineRelayUrl.Visible);
        onlineRelayUrl = OnlineField(onlineSettings, 505, DefaultRaceRelay, 512);
        string? relayOverride = System.Environment.GetEnvironmentVariable("GROMNIE_RELAY");
        if (string.IsNullOrWhiteSpace(relayOverride) && FileAccess.FileExists("res://RelayUrl.txt"))
            relayOverride = FileAccess.GetFileAsString("res://RelayUrl.txt");
        onlineRelayUrl.Text = string.IsNullOrWhiteSpace(relayOverride) ? DefaultRaceRelay : relayOverride.Trim();
        onlineRelayUrl.Hide();

        Plate(onlineRoom, 248, 118, 784, 543, "182922", "a4a66c");
        Text(onlineRoom, "ONLINE LOBBY", 280, 145, 720, 21, Mint, HorizontalAlignment.Center);
        onlineCode = Text(onlineRoom, "", 282, 193, 162, 27, Gold);
        Action(onlineRoom, "COPY INVITE", 449, 190, 153, 37, CopyOnlineInvite);
        onlineRoster = new ItemList { Position = new(282, 236), Size = new(320, 185),
            MouseFilter = Control.MouseFilterEnum.Ignore, Theme = ThemeForUi() };
        onlineRoster.AddThemeStyleboxOverride("panel", Surface("20352b", "687f56", 8));
        onlineRoster.AddThemeConstantOverride("v_separation", 8);
        onlineRoom.AddChild(onlineRoster);
        onlineRoomCourse = Text(onlineRoom, "", 282, 437, 320, 16, Soft);
        onlineRoomCourse.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        onlineLobbyVisibility = Action(onlineRoom, "ROOM · PRIVATE", 282, 465, 156, 33, ToggleRaceVisibility);
        onlineLobbyVisibility.TooltipText = onlineVisibilitySetting.TooltipText;
        onlineCourse = Action(onlineRoom, "CHANGE COURSE", 445, 465, 157, 33, ChooseOnlineCourse);
        onlineStart = Action(onlineRoom, "START RACE", 282, 506, 320, 39, HostOnlineRace, true);
        MakeOnlineChat();
        onlineStatus = Text(onlineUi, "", 420, 550, 440, 16, Soft);
        onlineStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Action(onlineUi, "BACK / LEAVE", 420, 600, 440, 39, () => ExitOnline());
        onlineUi.Hide();

        onlineWaiting = Layer(canvas);
        Plate(onlineWaiting, 424, 332, 432, 90, "172821f6", "7b8d5b");
        Text(onlineWaiting, "WAITING FOR RACERS", 438, 353, 404, 22, Mint, HorizontalAlignment.Center);
        Text(onlineWaiting, "The countdown starts when everyone loads.", 438, 385, 404, 15, Soft, HorizontalAlignment.Center);
        onlineWaiting.Hide();
        MakePublicRaceBrowser(canvas);
    }

    private LineEdit OnlineField(Control parent, float y, string placeholder, int limit)
    {
        var field = new LineEdit { Position = new(420, y), Size = new(440, 39), PlaceholderText = placeholder,
            MaxLength = limit, Theme = ThemeForUi() };
        parent.AddChild(field);
        return field;
    }

    private void OpenOnlineMenu()
    {
        if (!loaded || racing) return;
        if (onlineName.Text.Length == 0) onlineName.Text = aceRider?.Name ?? "Racer";
        onlineUi.Show();
        RefreshOnlineUi("Share the invite with friends. Your rider’s visible look travels with you.");
    }

    private void RefreshOnlineUi(string message)
    {
        onlineStatus.Text = message;
        onlineSettings.Visible = lobby is null;
        onlineRoom.Visible = lobby is not null;
        onlineJoin.Disabled = onlineHost.Disabled = onlineBrowseButton.Disabled = onlineVisibilitySetting.Disabled = raceSocket is not null;
        RefreshRaceVisibilityLabel();
        if (lobby is not { } room) return;
        onlineCode.Text = room.Code[..4] + "-" + room.Code[4..];
        onlineRoster.Clear();
        foreach (var member in room.Members)
        {
            string name = OnlineRiderName(DecodeLoadout(member.Profile));
            string role = member.Id == 1 ? "  ·  HOST" : "";
            onlineRoster.AddItem(name + role + (member.Id == room.You ? "  ·  YOU" : ""));
            onlineRoster.SetItemSelectable(onlineRoster.ItemCount - 1, false);
        }
        onlineRoomCourse.Text = OnlineHost ? CourseName(selectedTown) : "The host chooses the course.";
        onlineStart.Visible = onlineCourse.Visible = onlineLobbyVisibility.Visible = OnlineHost;
        onlineCourse.Disabled = onlineStart.Disabled = onlineLobbyVisibility.Disabled = room.Locked || onlineStartPending;
        RefreshOnlineChat(room);
    }

    private static string OnlineRiderName(OnlineLoadout look) =>
        look.Name is { Length: > 0 and <= 24 } name && !name.Any(char.IsControl) ? name : "Racer";

    private void CopyOnlineInvite()
    {
        if (lobby is not { } room) return;
        string shareRelay = connectedRelay;
        if (OnlineHost && new Uri(connectedRelay).IsLoopback &&
            TryRelayEndpoint(System.Environment.GetEnvironmentVariable("GROMNIE_INVITE_RELAY") ?? "", out var publicRelay) &&
            publicRelay!.Scheme == "wss") shareRelay = publicRelay.AbsoluteUri;
        string invite = FormatRaceInvite(room.Code, shareRelay);
        DisplayServer.ClipboardSet(invite);
        RefreshOnlineUi(shareRelay == DefaultRaceRelay
            ? "Code copied. Friends can enter it under Play Online and choose Join Race."
            : "Invite copied. Friends can paste it into Join Race.");
    }

    private static string FormatRaceInvite(string code, string relay)
    {
        string display = code[..4] + "-" + code[4..];
        return relay == DefaultRaceRelay ? display : display + "@" + relay;
    }

    private void ChooseOnlineCourse()
    {
        if (!OnlineHost || OnlineRace) return;
        onlineChoosingCourse = true;
        onlineUi.Hide();
        OpenCoursePicker();
    }

    private void CloseCoursePicker()
    {
        courseUi.Hide();
        if (!onlineChoosingCourse) return;
        onlineChoosingCourse = false;
        onlineUi.Show();
        PublishRaceListing();
        RefreshOnlineUi("Waiting in the lobby.");
    }

    private void UpdateOnlineRaceButtons()
    {
        pauseRestart.Text = OnlineRace ? "RETURN TO LOBBY" : "RESTART";
        resultsAgain.Text = OnlineRace ? "RETURN TO LOBBY" : "RACE AGAIN";
        pauseRestart.Disabled = resultsAgain.Disabled = OnlineGuest;
    }
}
