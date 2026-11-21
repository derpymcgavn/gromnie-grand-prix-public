using System.Text.Json;
using Godot;
using GromnieOnline;

public partial class GrandPrix
{
    private Control onlinePublicBrowseUi = null!;
    private Tree publicRaceList = null!;
    private LineEdit publicRaceFilter = null!;
    private Button publicRaceRefresh = null!, publicRaceJoin = null!;
    private Label publicRaceStatus = null!;
    private HttpRequest? publicRaceRequest;
    private Uri? publicRaceRelay;
    private PublicRoom[] onlinePublicRaces = [];
    private bool publicRaceListLoaded, onlineRoomPublic;

    private void MakePublicRaceBrowser(CanvasLayer canvas)
    {
        onlinePublicBrowseUi = Layer(canvas);
        var shade = new ColorRect { Color = new("101c19ae"), MouseFilter = Control.MouseFilterEnum.Stop };
        onlinePublicBrowseUi.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Plate(onlinePublicBrowseUi, 316, 125, 648, 532, "182922", "a4a66c");
        Text(onlinePublicBrowseUi, "PUBLIC RACES", 346, 152, 588, 24, Mint, display: true);

        publicRaceFilter = new LineEdit
        {
            Position = new(346, 198), Size = new(450, 42), MaxLength = 80,
            PlaceholderText = "Filter by circuit or room name", Theme = ThemeForUi()
        };
        onlinePublicBrowseUi.AddChild(publicRaceFilter);
        publicRaceFilter.TextChanged += _ => RenderPublicRaceList();
        publicRaceRefresh = Action(onlinePublicBrowseUi, "REFRESH", 810, 198, 124, 42, RefreshPublicRaceBrowser);

        publicRaceList = new Tree
        {
            Position = new(346, 255), Size = new(588, 281), Columns = 3,
            ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, Theme = ThemeForUi()
        };
        publicRaceList.SetColumnTitle(0, "ROOM");
        publicRaceList.SetColumnTitle(1, "CIRCUIT");
        publicRaceList.SetColumnTitle(2, "SEATS");
        publicRaceList.SetColumnTitleAlignment(0, HorizontalAlignment.Left);
        publicRaceList.SetColumnTitleAlignment(1, HorizontalAlignment.Left);
        publicRaceList.SetColumnTitleAlignment(2, HorizontalAlignment.Center);
        publicRaceList.SetColumnCustomMinimumWidth(0, 193);
        publicRaceList.SetColumnCustomMinimumWidth(1, 252);
        publicRaceList.SetColumnCustomMinimumWidth(2, 70);
        publicRaceList.SetColumnExpand(2, false);
        publicRaceList.AddThemeStyleboxOverride("panel", Surface("20352b", "687f56", 8));
        publicRaceList.AddThemeStyleboxOverride("selected", Surface("365747", "789566", 3));
        publicRaceList.AddThemeStyleboxOverride("selected_focus", Surface("42634b", "c1de9b", 3));
        publicRaceList.AddThemeStyleboxOverride("title_button_normal", Surface("263d31", "536851", 4));
        publicRaceList.AddThemeColorOverride("font_color", Cream);
        publicRaceList.AddThemeColorOverride("title_button_color", Mint);
        publicRaceList.AddThemeConstantOverride("v_separation", 11);
        onlinePublicBrowseUi.AddChild(publicRaceList);
        publicRaceList.ItemSelected += RefreshPublicJoinButton;
        publicRaceList.NothingSelected += RefreshPublicJoinButton;
        publicRaceList.ItemActivated += JoinPublicRace;

        publicRaceStatus = Text(onlinePublicBrowseUi, "", 346, 550, 588, 15, Soft);
        publicRaceStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Action(onlinePublicBrowseUi, "BACK", 346, 601, 136, 39, () => ClosePublicRaceBrowser());
        publicRaceJoin = Action(onlinePublicBrowseUi, "JOIN RACE", 755, 595, 179, 45, JoinPublicRace, true);
        onlinePublicBrowseUi.Hide();
    }

    private void OpenPublicRaceBrowser()
    {
        if (!loaded || racing || raceSocket is not null) return;
        onlineUi.Hide();
        onlinePublicBrowseUi.Show();
        publicRaceList.GrabFocus();
        RefreshPublicRaceBrowser();
    }

    private void ClosePublicRaceBrowser(bool showOnline = true)
    {
        CancelPublicRaceRequest();
        onlinePublicBrowseUi.Hide();
        publicRaceRelay = null;
        onlinePublicRaces = [];
        publicRaceListLoaded = false;
        publicRaceList.Clear();
        publicRaceFilter.Clear();
        if (!showOnline) return;
        onlineUi.Show();
        RefreshOnlineUi("Choose a race or enter a friend’s invite code.");
    }

    private void RefreshPublicRaceBrowser()
    {
        if (!onlinePublicBrowseUi.Visible || publicRaceRequest is not null) return;
        onlinePublicRaces = [];
        publicRaceListLoaded = false;
        publicRaceList.Clear();
        RefreshPublicJoinButton();
        if (!TryRelayEndpoint(onlineRelayUrl.Text.Trim(), out publicRaceRelay))
        {
            publicRaceStatus.Text = "Set a valid relay in Relay Settings to find public races.";
            return;
        }

        var endpoint = new UriBuilder(new Uri(publicRaceRelay!, "rooms"))
        {
            Scheme = publicRaceRelay!.Scheme == "wss" ? "https" : "http"
        };
        var request = new HttpRequest { Timeout = 10, MaxRedirects = 0, BodySizeLimit = Wire.MaxBytes };
        AddChild(request);
        publicRaceRequest = request;
        request.RequestCompleted += (result, status, _, body) => ReceivePublicRaces(request, result, status, body);
        publicRaceRefresh.Disabled = true;
        publicRaceStatus.Text = "Looking for public races…";
        if (request.Request(endpoint.Uri.AbsoluteUri, ["Accept: application/json"]) != Error.Ok)
        {
            CancelPublicRaceRequest();
            publicRaceStatus.Text = "Could not reach the relay. Check Relay Settings and retry.";
        }
    }

    private void CancelPublicRaceRequest()
    {
        if (publicRaceRequest is { } request)
        {
            publicRaceRequest = null;
            request.CancelRequest();
            request.QueueFree();
        }
        publicRaceRefresh.Disabled = false;
    }

    private void ReceivePublicRaces(HttpRequest request, long result, long status, byte[] body)
    {
        if (request != publicRaceRequest) return;
        publicRaceRequest = null;
        request.QueueFree();
        publicRaceRefresh.Disabled = false;
        if (!onlinePublicBrowseUi.Visible) return;
        if (result != (long)HttpRequest.Result.Success || status != 200)
        {
            publicRaceStatus.Text = status == 404
                ? "This relay does not have a public race list. You can join it by invite."
                : "Could not reach the relay. Check Relay Settings and retry.";
            return;
        }

        try
        {
            var response = JsonSerializer.Deserialize<PublicRoomList>(body);
            if (response?.Protocol != Wire.Protocol || response.Rooms is null)
            {
                publicRaceStatus.Text = "The public race list uses an incompatible relay version.";
                return;
            }
            onlinePublicRaces = response.Rooms.Where(room =>
                room is { Code.Length: 8, Name.Length: > 0 and <= 80, Players: > 0, Capacity: > 1 and <= Wire.Capacity } &&
                Wire.IsCode(room.Code) && room.Players < room.Capacity && !room.Name.Any(char.IsControl))
                .DistinctBy(room => room.Code).Take(48).ToArray();
        }
        catch (JsonException)
        {
            publicRaceStatus.Text = "The relay returned an unreadable race list.";
            return;
        }
        publicRaceListLoaded = true;
        RenderPublicRaceList();
    }

    private string PublicRaceCourse(int course) =>
        course == OnlineCourseId ? "Custom circuit" : HasCourse(course) ? CourseName(course) : "Unknown circuit";

    private void RenderPublicRaceList()
    {
        if (!publicRaceListLoaded) return;
        string filter = publicRaceFilter.Text.Trim();
        string? selectedCode = publicRaceList.GetSelected()?.GetMetadata(0).AsString();
        publicRaceList.Clear();
        var root = publicRaceList.CreateItem();
        TreeItem? selection = null;
        int shown = 0;
        foreach (var room in onlinePublicRaces)
        {
            string course = PublicRaceCourse(room.Course);
            if (!room.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !course.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var row = publicRaceList.CreateItem(root);
            row.SetText(0, room.Name);
            row.SetMetadata(0, room.Code);
            row.SetText(1, course);
            row.SetTooltipText(1, course);
            row.SetText(2, $"{room.Players}/{room.Capacity}");
            row.SetTextAlignment(2, HorizontalAlignment.Center);
            if (selection is null || room.Code == selectedCode) selection = row;
            shown++;
        }
        selection?.Select(0);
        publicRaceStatus.Text = shown > 0 ? $"{shown} {(shown == 1 ? "open race" : "open races")}" :
            onlinePublicRaces.Length > 0 ? "No races match that filter." : "No public races are open. You can host one or enter an invite code.";
        RefreshPublicJoinButton();
    }

    private void RefreshPublicJoinButton() =>
        publicRaceJoin.Disabled = publicRaceRequest is not null || publicRaceList.GetSelected() is null;

    private void JoinPublicRace()
    {
        if (!onlinePublicBrowseUi.Visible || publicRaceJoin.Disabled || publicRaceRelay is null ||
            publicRaceList.GetSelected() is not { } selected) return;
        string code = selected.GetMetadata(0).AsString();
        if (!Wire.IsCode(code)) return;
        string invite = FormatRaceInvite(code, publicRaceRelay.AbsoluteUri);
        ClosePublicRaceBrowser();
        onlineInvite.Text = invite;
        ConnectOnline(false);
    }

    private RaceListing CurrentRaceListing() => new(onlineRoomPublic, CustomCourse(selectedTown) is null ? selectedTown : OnlineCourseId);

    private void ToggleRaceVisibility()
    {
        if (lobby is { } room && (!OnlineHost || room.Locked || onlineStartPending)) return;
        onlineRoomPublic = !onlineRoomPublic;
        RefreshRaceVisibilityLabel();
        PublishRaceListing();
    }

    private void PublishRaceListing()
    {
        if (OnlineHost && lobby is { Locked: false })
            SendOnline(Wire.Packet("listing", CurrentRaceListing()));
    }

    private void RefreshRaceVisibilityLabel()
    {
        string label = onlineRoomPublic ? "ROOM · PUBLIC" : "ROOM · PRIVATE";
        onlineVisibilitySetting.Text = onlineLobbyVisibility.Text = label;
    }
}
