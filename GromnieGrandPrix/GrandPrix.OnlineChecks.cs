using Godot;
using Disk = System.IO.File;

public partial class GrandPrix
{
    private async void CheckOnline(string role)
    {
        try
        {
            string Option(string key) => OS.GetCmdlineUserArgs().Single(arg => arg.StartsWith(key, StringComparison.Ordinal))[key.Length..];
            string folder = Option("--online-check-dir=");
            bool host = role == "host";
            bool shortInvite = OS.GetCmdlineUserArgs().Contains("--online-check-short");
            bool browsePublic = OS.GetCmdlineUserArgs().Contains("--online-check-public");
            bool voidProgress = OS.GetCmdlineUserArgs().Contains("--online-check-void");
            bool checkChat = OS.GetCmdlineUserArgs().Contains("--online-check-chat");
            Require(loaded && (host || role == "guest"), "the multiplayer client loaded");
            Require(FormatRaceInvite("ABCD2345", DefaultRaceRelay) == "ABCD-2345" &&
                FormatRaceInvite("ABCD2345", "ws://127.0.0.1:8040/race") == "ABCD-2345@ws://127.0.0.1:8040/race",
                "the shared relay uses code-only invites and custom relays keep their address");
            onlineRelayUrl.Text = Option("--online-check-url=");
            onlineName.Text = host ? "Host Fixture" : "Guest Fixture";
            shieldIndex = host ? 0 : 2;
            int guestColor = RaceMount.SiraluunColors.Length - 1;
            if (!host)
            {
                SelectMount(RaceMountKind.Siraluun);
                SelectSkin(guestColor);
            }
            OpenOnlineMenu();
            if (host && browsePublic)
            {
                ChooseCourse(VoidCourseId);
                onlineRoomPublic = true;
            }
            string invite = Path.Combine(folder, "invite.txt");
            if (!host)
            {
                await WaitFor(() => Disk.Exists(invite), "host invite");
                if (browsePublic)
                {
                    OpenPublicRaceBrowser();
                    await WaitFor(() => onlinePublicRaces.Length == 1, "the host appeared in the public browser");
                    Require(onlinePublicRaces[0] is { Players: 1, Capacity: 5, Course: VoidCourseId }, "the browser displays the selected circuit and occupancy");
                    publicRaceFilter.Text = "no matching circuit";
                    RenderPublicRaceList();
                    Require(publicRaceList.GetRoot()?.GetFirstChild() is null && publicRaceJoin.Disabled,
                        "unmatched search terms hide rooms and disable joining");
                    publicRaceFilter.Text = "Rynthid";
                    RenderPublicRaceList();
                    const string publicShot = "--online-public-shot=";
                    if (OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(publicShot, StringComparison.Ordinal)) is { } shot)
                        await SaveFrame(shot[publicShot.Length..]);
                    var row = publicRaceList.GetRoot()?.GetFirstChild();
                    Require(row is not null, "the browser renders the public room");
                    row!.Select(0);
                    JoinPublicRace();
                }
                else
                {
                    onlineInvite.Text = Disk.ReadAllText(invite);
                    if (!shortInvite) onlineRelayUrl.Clear();
                }
            }
            if (host || !browsePublic) ConnectOnline(host);
            await WaitFor(() => lobby is not null, "connected to invite lobby");
            if (host)
            {
                string shareRelay = shortInvite ? DefaultRaceRelay : connectedRelay;
                Disk.WriteAllText(invite + ".tmp", FormatRaceInvite(lobby!.Code, shareRelay));
                Disk.Move(invite + ".tmp", invite, true);
                await WaitFor(() => lobby?.Members.Length == 2, "two participants joined");
                if (checkChat) await CheckLobbyChat(host: true);
                if (voidProgress) ChooseCourse(VoidCourseId);
                else
                {
                    var course = new CustomTrack(CustomTrack.FirstId, "Online Foothills", 0xb271ffff, 3, 24,
                        ExampleTrackPoints.Select(point => new[] { point.X, point.Y }).ToArray());
                    customTracks[course.Id] = course;
                    ChooseOnlineCourse();
                    ChooseCourse(course.Id);
                }
                const string screenshot = "--online-check-shot=";
                if (OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(screenshot, StringComparison.Ordinal)) is { } shot)
                    await SaveFrame(shot[screenshot.Length..]);
                HostOnlineRace();
            }
            else if (checkChat) await CheckLobbyChat(host: false);
            await WaitFor(() => onlineBegun && racing, "host start and guest load acknowledgment", 50);
            Require((voidProgress ? track.IsVoid : track.IsCustom && track.Name == "Online Foothills") && Player.Name == onlineName.Text,
                "both clients race the same course and use their own view");
            Require(racers[1].Rig.Kind == RaceMountKind.Siraluun &&
                racers[1].Mount.NativeAppearanceId == RaceMount.SiraluunColors[guestColor].Look.Id,
                "both clients see the guest's selected Siraluun plumage");
            pressed.Add(Key.W);
            await WaitFor(() => racing && countdown <= 0, "the synced countdown");
            if (voidProgress) await CheckOnlineVoidProgress(host, folder);
            else if (host)
            {
                await WaitFor(() => racers[1].Distance > BogCircuit.Grid + 8 && racers[1].Speed > 5, "guest inputs drive on the host");
                await WaitFor(() => racers[1].GroundDrifting && dust.DriftSmokeCount > 0, "guest held inputs start a smoky drift on the host");
                await WaitFor(() => !racers[1].GroundDrifting && HasSuperDrift(racers[1]) && dust.SuperDriftFireCount > 0,
                    "releasing guest drift input earns a super drift and fire on the host");
                HoldItem(racers[1], RaceItem.Pepper);
                await WaitFor(() => racers[1].Item == RaceItem.None && racers[1].Boost > .1f &&
                    racers[1].GuardCooldown > .1f, "guest pickup and shield actions resolve on the host");
                float before = elapsed;
                CrossOnlineCheckLap(Player, track.Laps);
                await WaitFor(() => elapsed > before + .5f, "host keeps simulating after finishing");
                Require(!finished && racers[1].Finish < 0, "host waits for the guest");
                CrossOnlineCheckLap(racers[1], track.Laps);
                await WaitFor(() => finished, "host final result");
                await WaitFor(() => Disk.Exists(Path.Combine(folder, "guest-finished.txt")), "guest final result arrived");
                ReturnOnlineLobby();
                await WaitFor(() => lobby is { Locked: false } && onlineUi.Visible, "room reopens after results");
                await WaitFor(() => lobby?.Members.Length == 1, "guest left");
            }
            else
            {
                await WaitFor(() => Player.Distance > BogCircuit.Grid + 9 && Player.Speed > 16, "guest reaches the road at drifting speed");
                pressed.Add(Key.D);
                pressed.Add(Key.Space);
                await WaitFor(() => Player.GroundDrifting && charge > .08f && dust.DriftSmokeCount > 0,
                    "guest receives their drift charge and smoke");
                pressed.Remove(Key.D);
                await WaitFor(() => Player.GroundDrifting && charge > 1.22f, "guest keeps charging while releasing the steering key");
                pressed.Remove(Key.Space);
                await WaitFor(() => !Player.GroundDrifting && HasSuperDrift(Player) && dust.SuperDriftFireCount > 0,
                    "guest receives their released super drift and draws fire from the host state");
                await WaitFor(() => racing && Player.Item == RaceItem.Pepper, "host item reached guest");
                useItem = requestGuard = true;
                await WaitFor(() => racing && Player.Boost > .1f && Player.GuardCooldown > .1f, "guest received item and shield outcomes");
                await WaitFor(() => finished && resultsUi.Visible, "guest receives shared results");
                Require(Player.Finish > 0 && localRacerIndex == 1, "guest sees their own finish");
                Disk.WriteAllText(Path.Combine(folder, "guest-finished.txt"), "PASS");
                await WaitFor(() => lobby is { Locked: false } && onlineUi.Visible, "host reopened lobby");
            }
            ExitOnline();
            Disk.WriteAllText(Path.Combine(folder, role + "-pass.txt"), "PASS");
            string verified = voidProgress ? "invite, rival lap splits, a shared live finale, results and lobby."
                : "code, shared track, inputs, drift smoke and fire, item and shield, results and lobby.";
            if (checkChat) verified += " Lobby chat passed.";
            GD.Print($"ONLINE {role.ToUpperInvariant()} PASS | {verified}");
            QuitSoundtrackCheck();
        }
        catch (Exception error)
        {
            GD.PrintErr($"{error.Message} ({onlineStatus.Text})");
            GetTree().Quit(2);
        }
    }

    private async Task CheckLobbyChat(bool host)
    {
        const string hostText = "Ready at the starting line? [b]";
        const string guestText = "Ready! See you out there.";
        Require(lobby is { ChatEnabled: true } && onlineChatInput.Editable && !onlineChatSend.Disabled,
            "the relay advertises chat in the open lobby");
        if (!host)
            await WaitFor(() => chatHistory.Contains(("Host Fixture", hostText)), "host chat reached the guest");
        onlineChatInput.Text = host ? hostText : guestText;
        onlineChatInput.EmitSignal(LineEdit.SignalName.TextSubmitted, onlineChatInput.Text);
        Require(onlineChatInput.Text.Length == 0, "sending clears the chat field");
        await WaitFor(() => chatHistory.Contains(("Host Fixture", hostText)) &&
            chatHistory.Contains(("Guest Fixture", guestText)), "the relay echoed both named chat messages");
        Require(onlineChatLog.GetParsedText().Contains(hostText, StringComparison.Ordinal),
            "the chat transcript shows literal message text");
    }

    private async Task CheckOnlineVoidProgress(bool host, string folder)
    {
        string completed = Path.Combine(folder, "guest-sealed.txt");
        if (host)
        {
            await WaitFor(() => elapsed > .25f && racers[1].Speed > .1f, "the guest is racing");
            for (int lapNumber = 1; lapNumber <= track.Laps; lapNumber++)
            {
                CrossOnlineCheckLap(racers[1], lapNumber);
                if (lapNumber == 1)
                {
                    await WaitFor(() => lapSplitHud.Visible && lapSplitRider.Text == racers[1].Name && VoidBattleLap() == 2,
                        "the host sees the rival's split");
                    await WaitFor(() => Disk.Exists(Path.Combine(folder, "guest-first-lap.txt")), "guest saw the lap time");
                }
                else if (lapNumber < track.Laps)
                {
                    float nextSplit = elapsed + .12f;
                    await WaitFor(() => elapsed >= nextSplit, "next lap crossing");
                }
            }
        }
        else
        {
            await WaitFor(() => Player.LapCrossings.Count == 1 && lapSplitHud.Visible && VoidBattleLap() == 2,
                "the first lap split arrives and advances the map");
            Require(lapSplitRider.Text == "You", "the crossing appears in the split display");
            Disk.WriteAllText(Path.Combine(folder, "guest-first-lap.txt"), "PASS");
        }
        await WaitFor(() => racers[1].Finish > 0 && voidFinaleAge >= 2.15f, "the guest's finish starts the shared seal");
        Require(!finished && !voidFinaleCamera && racers[0].Finish < 0 && racers[1].LapCrossings.Count == 5 &&
            voidSealAt == racers[1].Finish, "the guest's splits drive the map with the host still racing");
        await WaitFor(() => voidFinaleAge >= VoidFinaleDuration && voidSummoning!.Shard.Visible &&
            !voidSummoning.Caster.Visible, "the shard in the live race");
        if (host)
        {
            await WaitFor(() => Disk.Exists(completed), "guest saw the live seal");
            CrossOnlineCheckLap(Player, track.Laps);
            await WaitFor(() => gateText.Text == "FINISHED · WAITING" && !finished && racers.Skip(2).Any(racer => racer.Finish < 0),
                "the world keeps running for the rest of the pack");
            Require(timerText.Text == FormatTime(Player.Finish), "the finish time stays fixed while the rest of the pack races");
            foreach (var other in racers.Skip(2)) CrossOnlineCheckLap(other, track.Laps);
            await WaitFor(() => finished && resultsUi.Visible, "the host shows results after the field finishes");
            await WaitFor(() => Disk.Exists(Path.Combine(folder, "guest-finished.txt")), "the guest's shared results");
            ReturnOnlineLobby();
            await WaitFor(() => lobby is { Locked: false } && onlineUi.Visible, "the host reopened the lobby");
            await WaitFor(() => lobby?.Members.Length == 1, "the guest left");
        }
        else
        {
            Disk.WriteAllText(completed, "PASS");
            await WaitFor(() => finished && resultsUi.Visible, "the guest shows results after the field finishes");
            Require(!voidFinaleCamera && voidSummoning!.Shard.Visible && resultStandings.GetChildCount() == 42,
                "the results show all lap splits and leave Bael sealed");
            Disk.WriteAllText(Path.Combine(folder, "guest-finished.txt"), "PASS");
            await WaitFor(() => lobby is { Locked: false } && onlineUi.Visible, "the host reopened the lobby");
        }
    }

    private void CrossOnlineCheckLap(MountedRacer racer, int lapNumber)
    {
        // Skip the drive between checkpoints while exercising the real crossing and timing code.
        int gate = lapNumber * track.GateCount;
        float at = track.Gate(gate);
        racer.GatesCleared = gate;
        racer.Distance = at;
        for (int i = racer.LapCrossings.Count; i < lapNumber - 1; i++)
            racer.LapCrossings.Add(elapsed * (i + 1) / lapNumber);
        var point = track.Surface(at, 0);
        var tangent = track.Forward(at) * .1f;
        RecordGate(racer, point - tangent, point + tangent, 1f / 60);
        Require(racer.GatesCleared == gate + 1 && racer.LapCrossings.Count == lapNumber,
            "the checkpoint clock recorded the lap at the line");
    }

    private async Task WaitFor(Func<bool> predicate, string operation, int seconds = 24)
    {
        ulong limit = Time.GetTicksMsec() + (ulong)seconds * 1000;
        while (!predicate())
        {
            if (Time.GetTicksMsec() > limit) throw new InvalidOperationException($"Timed out waiting for {operation}.");
            await ToSignal(GetTree().CreateTimer(.04), SceneTreeTimer.SignalName.Timeout);
        }
    }
}
