using System.Net;
using System.Text.Json;
using DerethSurvivors;
using GromnieOnline;
using Godot;

public partial class GrandPrix
{
    private const int GasBit = 1, BrakeBit = 2, DriftBit = 4, FreeFlightBit = 8;
    private const int ItemBit = 1, LeftBit = 2, RightBit = 4, GuardBit = 8, RollBit = 16;
    private const int OnlineCourseId = 19_999;
    private readonly Dictionary<int, HumanPilot> remotePilots = [];
    private readonly HashSet<int> onlineLoaded = [];
    private readonly List<OnlineWave> onlineWaves = [];
    private WebSocketPeer? raceSocket;
    private RelayRoster? lobby;
    private Wire? onlineHello;
    private OnlinePlan? onlinePlan;
    private OnlineState? pendingOnlineState;
    private string connectedRelay = "";
    private bool onlineBegun, onlineChoosingCourse, onlineStartPending;
    private int onlineEpoch, onlineFrame, onlineInputSequence, localRacerIndex;
    private int onlineReturnCourse;
    private float onlineSendTimer, onlineIdle, onlineLoadDeadline;
    private bool OnlineHost => lobby?.You == 1;
    private bool OnlineRace => onlinePlan is not null;
    private bool OnlineGuest => OnlineRace && !OnlineHost;

    private sealed class HumanPilot
    {
        internal int Serial, Held, Actions, Steer;
        internal ulong LastInput;
        internal float Lateral, Charge;
        internal bool Sliding, StartBoost, PreviousGas;
    }

    public override void _Process(double delta)
    {
        if (paceAndroidFrames) androidRacePacing.ScheduleFrame();
        if (voidFinaleCamera) TickVoidFinale((float)delta);
        else if (AndroidGraphics && loaded && racing && !OnlineRace && !paused && !finished)
            UpdateRacePresentation((float)delta);
        if (raceSocket is not { } socket) return;
        float dt = (float)delta;
        socket.Poll();
        var state = socket.GetReadyState();
        if (state == WebSocketPeer.State.Closed)
        {
            ExitOnline("Connection closed. Check the relay and try again.");
            return;
        }
        onlineIdle += dt;
        if (onlineIdle > (lobby is null ? 12 : 80))
        {
            ExitOnline("The relay stopped responding.");
            return;
        }
        if (state != WebSocketPeer.State.Open) return;
        if (onlineHello is { } hello)
        {
            onlineHello = null;
            SendOnline(hello);
        }
        for (int i = 0; i < 128 && socket.GetAvailablePacketCount() > 0; i++)
        {
            byte[] bytes = socket.GetPacket();
            if (bytes.Length > Wire.MaxBytes) { ExitOnline("Oversized relay message."); return; }
            try
            {
                onlineIdle = 0;
                if (JsonSerializer.Deserialize<Wire>(bytes) is { } packet) ReceiveOnline(packet);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                ExitOnline("The room sent incompatible race data.");
            }
            if (raceSocket != socket) return;
        }
        if (!onlineBegun && OnlineRace && OnlineHost)
        {
            if (lobby!.Members.All(peer => onlineLoaded.Contains(peer.Id)))
            {
                onlineBegun = true;
                SendOnline(Wire.Packet("go", onlinePlan!.Epoch));
            }
            else if (worldTime > onlineLoadDeadline)
                ReturnOnlineLobby("A racer couldn’t load the course in time.");
        }
        onlineSendTimer += dt;
        if (onlineSendTimer >= 15)
        {
            SendOnline(new Wire("ping"));
            onlineSendTimer = 0;
        }
        if (onlineStartPending && worldTime > onlineLoadDeadline)
        {
            onlineStartPending = false;
            RefreshOnlineUi("The relay didn't start the race.");
        }
    }

    private void SendOnline(Wire frame)
    {
        if (raceSocket is not { } socket || socket.GetReadyState() != WebSocketPeer.State.Open) return;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        if (bytes.Length <= Wire.MaxBytes && socket.Send(bytes, WebSocketPeer.WriteMode.Text) == Error.Ok) return;
        ExitOnline("The relay connection fell behind.");
    }

    private void ConnectOnline(bool host)
    {
        if (raceSocket is not null) return;
        string endpoint = onlineRelayUrl.Text.Trim();
        string code = "";
        if (!host)
        {
            string invite = onlineInvite.Text.Trim();
            int separator = invite.IndexOf('@');
            if (separator >= 0)
            {
                endpoint = invite[(separator + 1)..].Trim();
                invite = invite[..separator];
            }
            code = Wire.CleanCode(invite);
            if (!Wire.IsCode(code)) { RefreshOnlineUi("Enter the host’s eight-character code or paste the full invite."); return; }
        }
        if (!TryRelayEndpoint(endpoint, out Uri? address))
        {
            RefreshOnlineUi("Set a wss:// relay URL, or paste a full invite from your host.");
            return;
        }
        string name = onlineName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 24 || name.Any(char.IsControl))
        { RefreshOnlineUi("Enter a racer name up to 24 characters."); return; }
        var profile = new OnlineLoadout(name, (int)selectedMount, SelectedMountColor, weaponIndex, shieldIndex, appearance.Copy(), aceRider);
        var admission = new Admit(Wire.Protocol, JsonSerializer.SerializeToElement(profile), host ? CurrentRaceListing() : null);
        if (admission.Profile.GetRawText().Length > 10_000)
        {
            RefreshOnlineUi("This imported skin is too large. Choose the custom rider for this room.");
            return;
        }
        connectedRelay = address!.AbsoluteUri;
        raceSocket = new WebSocketPeer { InboundBufferSize = 512 * 1024, OutboundBufferSize = 512 * 1024 };
        onlineHello = Wire.Packet(host ? "host" : "join", admission) with { Code = code };
        onlineIdle = onlineSendTimer = 0;
        if (raceSocket.ConnectToUrl(connectedRelay) != Error.Ok)
        {
            ExitOnline("Couldn’t connect to the relay.");
            return;
        }
        RefreshOnlineUi("Connecting to relay…");
    }

    private static bool TryRelayEndpoint(string input, out Uri? endpoint)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var parsed) && parsed.UserInfo.Length == 0 &&
            parsed.Query.Length == 0 && parsed.Fragment.Length == 0 && parsed.Host.Length > 0 &&
            (parsed.Scheme == "wss" || parsed.Scheme == "ws" && IPAddress.TryParse(parsed.DnsSafeHost, out var address) &&
             IPAddress.IsLoopback(address)))
        {
            endpoint = parsed.AbsolutePath == "/" ? new Uri(parsed, "/race") : parsed;
            return true;
        }
        endpoint = null;
        return false;
    }

    private void ReceiveOnline(Wire wire)
    {
        switch (wire.Type)
        {
            case "room" when wire.Read<RelayRoster>() is { } roster && Wire.IsCode(roster.Code ?? "") &&
                roster.Members is { Length: > 0 and <= Wire.Capacity } && roster.Members.All(peer => peer is not null) &&
                roster.Members.Any(peer => peer.Id == roster.You):
                lobby = roster;
                RefreshOnlineUi(onlineBegun ? "" : roster.Locked ? "Loading the race…" : "Waiting in the lobby.");
                break;
            case "chat" when lobby is { ChatEnabled: true, Locked: false } && !OnlineRace:
                ReceiveLobbyChat(wire);
                break;
            case "error":
                string message = wire.Read<string>() ?? "The relay rejected the request.";
                if (lobby is null) ExitOnline(message);
                else { onlineStartPending = false; RefreshOnlineUi(message); }
                break;
            case "closed":
                ExitOnline(wire.Read<string>() ?? "The host closed the room.");
                break;
            case "prepare" when wire.From == 1 && lobby is not null && wire.Read<OnlinePlan>() is { } plan:
                onlineStartPending = false;
                PrepareOnlineRace(plan);
                break;
            case "loaded" when OnlineHost && OnlineRace && lobby!.Members.Any(peer => peer.Id == wire.From) &&
                wire.Read<OnlineLoaded>() is { } loaded && loaded.Epoch == onlinePlan!.Epoch:
                if (loaded.Error is not null) ReturnOnlineLobby(loaded.Error);
                else onlineLoaded.Add(wire.From);
                break;
            case "go" when wire.From == 1 && OnlineGuest && wire.Read<int>() == onlinePlan!.Epoch:
                onlineBegun = true;
                break;
            case "state" when wire.From == 1 && OnlineGuest && onlineBegun && wire.Read<OnlineState>() is { } state:
                if (state.Epoch == onlinePlan!.Epoch && state.Frame > onlineFrame &&
                    state.Frame > (pendingOnlineState?.Frame ?? 0) && state.Racers is { Length: RacerCount } &&
                    state.HiddenPickups is not null && state.Waves is not null && state.Props is not null)
                    pendingOnlineState = state;
                break;
            case "input" when OnlineHost && onlineBegun && OnlineRace && remotePilots.TryGetValue(wire.From, out var pilot) &&
                wire.Read<OnlineInput>() is { } controls && controls.Epoch == onlinePlan!.Epoch && controls.Serial > pilot.Serial:
                pilot.Serial = controls.Serial;
                pilot.Held = controls.Held & (GasBit | BrakeBit | DriftBit | FreeFlightBit);
                pilot.Steer = Math.Clamp(controls.Steer, -1, 1);
                pilot.Actions |= controls.Actions & (ItemBit | LeftBit | RightBit | GuardBit | RollBit);
                pilot.LastInput = Time.GetTicksMsec();
                break;
            case "lobby" when wire.From == 1:
                BackToOnlineLobby();
                if (wire.Read<string>() is { } note) RefreshOnlineUi(note);
                break;
        }
    }

    private void HostOnlineRace()
    {
        if (!OnlineHost || lobby is not { Locked: false } room || OnlineRace || onlineStartPending) return;
        var slots = room.Members.Select(peer => new OnlineSlot(peer.Id, DecodeLoadout(peer.Profile))).ToList();
        var bots = new[] { "", "Sir Chomps", "Puddle Pete", "Mudonna", "The Honk" };
        int[] paints = [0, 1, 2, 3, 7];
        while (slots.Count < RacerCount)
        {
            int i = slots.Count;
            int mount = (i + selectedTown % RaceMount.All.Length + 3) % RaceMount.All.Length;
            slots.Add(new OnlineSlot(0, new OnlineLoadout(bots[i], mount, paints[i], i switch { 1 => 1, 2 => 2, 3 => 3, _ => 6 },
                (i + 1) % RaceShield.All.Length, RivalLook(i))));
        }
        var route = CustomCourse(selectedTown);
        var plan = new OnlinePlan(++onlineEpoch, route is null ? selectedTown : OnlineCourseId, testMode ? 0 : RollRaceWeather(),
            Random.Shared.Next(1, 1_000_000), slots.ToArray(), route);
        onlineReturnCourse = selectedTown;
        onlineLoadDeadline = worldTime + 12;
        onlineStartPending = true;
        SendOnline(Wire.Packet("prepare", plan, room.Revision));
        RefreshOnlineUi("Starting the race…");
    }

    private OnlineLoadout DecodeLoadout(JsonElement json)
    {
        try { return json.Deserialize<OnlineLoadout>() ?? throw new JsonException(); }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        { return new OnlineLoadout("Racer", 0, 0, 0, 0, new CharacterAppearance()); }
    }

    private void PrepareOnlineRace(OnlinePlan plan)
    {
        if (OnlineRace || plan.Slots is not { Length: RacerCount } || plan.Epoch <= 0 ||
            plan.Slots.Any(slot => slot?.Loadout is null) || plan.Slots[0].Peer != 1 ||
            plan.Slots.Where(slot => slot.Peer > 0).Select(slot => slot.Peer).Distinct().Count() != plan.Slots.Count(slot => slot.Peer > 0))
        { ExitOnline("Invalid race roster."); return; }
        localRacerIndex = Array.FindIndex(plan.Slots, slot => slot.Peer == lobby!.You);
        if (localRacerIndex < 0) { ExitOnline("You missed the start. Rejoin the lobby."); return; }
        if (!OnlineHost) onlineReturnCourse = selectedTown;
        onlinePlan = plan;
        onlineBegun = false;
        onlineFrame = onlineInputSequence = 0;
        onlineFrameTimer = onlineInputTimer = 0;
        onlineLoadDeadline = worldTime + 55;
        onlineLoaded.Clear();
        remotePilots.Clear();
        foreach (var slot in plan.Slots)
            if (slot.Peer > 1) remotePilots.Add(slot.Peer, new HumanPilot());
        selectedTown = plan.Course;
        string? problem = null;
        bool usable = plan.Course == OnlineCourseId ? plan.Route?.IsValid == true
            : plan.Route is null && (HasCourse(plan.Course) || (testMode && plan.Course == 0));
        if (usable && plan.Weather >= 0 && plan.Weather < WeatherPresets.Length)
        {
            StartRace();
            if (!racing) problem = "A racer couldn’t load this course or its DAT files.";
        }
        else problem = "A racer is missing this course. Check game and DAT versions.";
        if (problem is null)
        {
            onlineUi.Hide();
            onlineWaiting.Show();
        }
        if (OnlineHost)
        {
            if (problem is null) onlineLoaded.Add(1);
            else ReturnOnlineLobby(problem);
        }
        else SendOnline(Wire.Packet("loaded", new OnlineLoaded(plan.Epoch, problem)));
    }

    private void ReturnOnlineLobby(string? warning = null)
    {
        if (!OnlineHost) return;
        SendOnline(Wire.Packet("lobby", warning));
    }

    private void BackToOnlineLobby()
    {
        if (OnlineRace)
        {
            ClearOnlineRace(onlineReturnCourse);
            OpenStable();
        }
        onlineUi.Show();
        RefreshOnlineUi("Waiting in the lobby.");
    }

    private void ClearOnlineRace(int returnCourse)
    {
        onlinePlan = null;
        onlineBegun = onlineStartPending = false;
        onlineLoaded.Clear();
        remotePilots.Clear();
        onlineWaves.Clear();
        pendingOnlineState = null;
        ClearOnlineProps();
        localRacerIndex = 0;
        onlineWaiting.Hide();
        selectedTown = returnCourse;
        best = bestByCourse.GetValueOrDefault(returnCourse, -1);
    }

    private void ExitOnline(string? message = null)
    {
        ClosePublicRaceBrowser(showOnline: false);
        onlineRoomPublic = false;
        var old = raceSocket;
        raceSocket = null;
        onlineHello = null;
        lobby = null;
        ClearLobbyChat();
        if (old is not null) { old.Close(); old.Dispose(); }
        bool raced = OnlineRace;
        if (onlineChoosingCourse) courseUi.Hide();
        onlineChoosingCourse = false;
        ClearOnlineRace(raced ? onlineReturnCourse : selectedTown);
        if (raced) OpenStable();
        if (message is not null)
        {
            onlineUi.Show();
            RefreshOnlineUi(message);
        }
        else onlineUi.Hide();
    }

    private void OnlineRaceButton()
    {
        if (!OnlineRace) StartRace();
        else if (OnlineHost) ReturnOnlineLobby();
    }

    private void StableButton()
    {
        bool raced = OnlineRace;
        if (raceSocket is not null) ExitOnline();
        if (!raced) OpenStable();
    }
}
