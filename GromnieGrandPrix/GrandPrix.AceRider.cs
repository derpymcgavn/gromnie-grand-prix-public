using System.Text.Json;
using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private AceRiderSnapshot? aceRider;
    private string aceEndpoint = "";
    private Control aceRiderUi = null!;
    private Control nativeAcePane = null!, acePairingPane = null!;
    private LineEdit aceAddress = null!, aceCode = null!;
    private Label aceStatus = null!;
    private Button aceConnect = null!, aceUseCustom = null!;
    private HttpRequest aceRequest = null!;
    private LineEdit nativeAceAddress = null!, nativeAceAccount = null!, nativeAcePassword = null!;
    private Button nativeAceConnectButton = null!, nativeAceImportButton = null!;
    private ItemList nativeAceCharacters = null!;
    private Label nativeAceStatus = null!;
    private readonly Dictionary<Control, Button> aceImportTabs = [];

    private void MakeAceRiderUi(CanvasLayer canvas)
    {
        Action(stableUi, "IMPORT FROM ACE", 300, 350, 168, 30, OpenAceRider);
        aceRiderUi = Layer(canvas);
        MakeModal(aceRiderUi, "IMPORT RIDER", 116, 580);
        BuildNativeAcePane();
        BuildRiderTemplates();
        BuildAcePairingPane();
        var tabGroup = new ButtonGroup();
        AddTab(nativeAcePane, "SERVER", 420, 113);
        AddTab(templatePane, "SAVED TEMPLATES", 541, 172);
        AddTab(acePairingPane, "PAIRING CODE", 721, 139);
        Action(aceRiderUi, "CLOSE", 420, 628, 212, 42, CloseAceRider);
        aceUseCustom = Action(aceRiderUi, "USE CUSTOM RIDER", 644, 628, 216, 42, UseCustomRider);
        aceRiderUi.Hide();

        void AddTab(Control pane, string label, int x, int width)
        {
            var tab = Action(aceRiderUi, label, x, 175, width, 42, () => ShowAceImportPane(pane));
            tab.ToggleMode = true;
            tab.ButtonGroup = tabGroup;
            tab.AddThemeStyleboxOverride("pressed", Surface("446d50", "abd1a0", 7));
            aceImportTabs.Add(pane, tab);
        }
    }

    private Control RiderImportPane(string name)
    {
        var pane = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        aceRiderUi.AddChild(pane);
        pane.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return pane;
    }

    private ItemList RiderImportList(Control parent, Vector2 position, Vector2 size)
    {
        var list = new ItemList { Position = position, Size = size, Theme = ThemeForUi() };
        list.AddThemeStyleboxOverride("panel", Surface("20352b", "687f56", 8));
        list.AddThemeStyleboxOverride("selected_focus", Surface("43664a", "9bc693", 4));
        list.AddThemeStyleboxOverride("selected", Surface("365843", "82a07a", 4));
        list.AddThemeConstantOverride("v_separation", 6);
        parent.AddChild(list);
        return list;
    }

    private void BuildNativeAcePane()
    {
        nativeAcePane = RiderImportPane("Server");
        Text(nativeAcePane, "Sign out of AC first. Your password is never saved.",
            420, 226, 440, 14, Soft).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Text(nativeAcePane, "Shard server", 420, 263, 440, 15, Soft);
        nativeAceAddress = new LineEdit
        {
            Position = new(420, 282), Size = new(440, 37), MaxLength = 260,
            PlaceholderText = "shard.example.net:9000", Theme = ThemeForUi()
        };
        nativeAcePane.AddChild(nativeAceAddress);
        Text(nativeAcePane, "Account", 420, 326, 215, 15, Soft);
        Text(nativeAcePane, "Password", 645, 326, 215, 15, Soft);
        nativeAceAccount = new LineEdit
        {
            Position = new(420, 345), Size = new(215, 37), MaxLength = 50, Theme = ThemeForUi()
        };
        nativeAcePassword = new LineEdit
        {
            Position = new(645, 345), Size = new(215, 37), MaxLength = 254, Secret = true, Theme = ThemeForUi()
        };
        nativeAcePassword.TextSubmitted += _ => ConnectNativeAce();
        nativeAceAddress.TextChanged += _ => ClearNativeAceOnAddressChange();
        nativeAceAccount.TextChanged += _ => ClearNativeAceOnAddressChange();
        nativeAcePane.AddChild(nativeAceAccount);
        nativeAcePane.AddChild(nativeAcePassword);
        nativeAceConnectButton = Action(nativeAcePane, "CONNECT", 420, 389, 440, 35, ConnectNativeAce);
        nativeAceCharacters = RiderImportList(nativeAcePane, new(420, 433), new(440, 109));
        nativeAceCharacters.ItemSelected += _ => UpdateNativeAceButtons();
        nativeAceCharacters.ItemActivated += _ => { if (!nativeAceBusy) ImportNativeAceCharacter(); };
        nativeAceImportButton = Action(nativeAcePane, "IMPORT CHARACTER", 420, 551, 440, 39, ImportNativeAceCharacter, true);
        nativeAceStatus = Text(nativeAcePane, "", 420, 594, 440, 14, Soft);
        nativeAceStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        SetNativeAceBusy(false, "Connect to your shard to list your characters.");
        nativeAcePane.Hide();
    }

    private void BuildAcePairingPane()
    {
        acePairingPane = RiderImportPane("Pairing Code");
        Text(acePairingPane, "In Asheron’s Call, type /gromnielook on a shard with the bridge.", 420, 243, 440, 16, Soft)
            .AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Text(acePairingPane, "Appearance endpoint", 420, 298, 440, 15);
        aceAddress = new LineEdit { Position = new(420, 320), Size = new(440, 40), MaxLength = 512,
            PlaceholderText = "https://your-shard.example/rider-appearance", Theme = ThemeForUi() };
        acePairingPane.AddChild(aceAddress);
        Text(acePairingPane, "One-time code from AC chat", 420, 374, 440, 15);
        aceCode = new LineEdit { Position = new(420, 396), Size = new(440, 40), MaxLength = 25,
            PlaceholderText = "Paste your code", Theme = ThemeForUi() };
        acePairingPane.AddChild(aceCode);
        aceCode.TextSubmitted += _ => FetchAceRider();
        aceStatus = Text(acePairingPane, "", 420, 491, 440, 15, Soft);
        aceStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        aceConnect = Action(acePairingPane, "IMPORT", 420, 447, 440, 38, FetchAceRider, true);
        acePairingPane.Hide();

        aceRequest = new HttpRequest { Timeout = 10, MaxRedirects = 0, BodySizeLimit = AceRiderSnapshot.MaxResponseBytes };
        AddChild(aceRequest);
        aceRequest.RequestCompleted += ReceiveAceRider;
    }

    private void OpenAceRider()
    {
        LoadRiderTemplates();
        aceAddress.Text = aceEndpoint;
        aceCode.Clear();
        aceStatus.Text = aceRider is { } active ? $"Current rider · {active.Name}" : "Use the HTTPS endpoint supplied by your shard.";
        aceUseCustom.Disabled = aceRider is null;
        aceRiderUi.Show();
        ShowAceImportPane(nativeAcePane);
    }

    private void ShowAceImportPane(Control pane)
    {
        if (pane != nativeAcePane && nativeAcePane.Visible)
        {
            ResetNativeAcePane();
        }
        if (pane != acePairingPane && acePairingPane.Visible)
            ResetAcePairing();
        foreach (var (section, button) in aceImportTabs)
        {
            section.Visible = section == pane;
            button.ButtonPressed = section == pane;
        }
        if (pane == templatePane)
        {
            RefreshRiderTemplates();
            riderTemplateList.GrabFocus();
        }
        else if (pane == acePairingPane)
        {
            if (aceAddress.Text.Length == 0) aceAddress.GrabFocus();
            else aceCode.GrabFocus();
        }
        else if (!nativeAceBusy) nativeAceAddress.GrabFocus();
    }

    private void CloseAceRider()
    {
        ResetNativeAcePane();
        ResetAcePairing();
        aceRiderUi.Hide();
    }

    private void ResetNativeAcePane()
    {
        CancelNativeAce();
        nativeAcePassword.Clear();
        SetNativeAceCharacters([]);
        SetNativeAceBusy(false, "Connect to your shard to list your characters.");
    }

    private void ResetAcePairing()
    {
        aceRequest.CancelRequest();
        aceConnect.Disabled = false;
        aceAddress.Editable = true;
        aceCode.Clear();
    }

    private void UseCustomRider()
    {
        RebuildShowcase(creation.Resolve(appearance), "You");
        aceRider = null;
        FillStable();
        SaveGarage();
        CloseAceRider();
    }

    private int? SelectedNativeAceCharacterIndex()
    {
        int[] selected = nativeAceCharacters.GetSelectedItems();
        return selected.Length == 0 ? null : selected[0];
    }

    private void SetNativeAceCharacters(IEnumerable<string> names)
    {
        nativeAceCharacters.Clear();
        foreach (string name in names) nativeAceCharacters.AddItem(name);
        if (nativeAceCharacters.ItemCount > 0) nativeAceCharacters.Select(0);
        UpdateNativeAceButtons();
    }

    private void SetNativeAceBusy(bool busy, string message)
    {
        nativeAceBusy = busy;
        nativeAceStatus.Text = message;
        nativeAceAddress.Editable = nativeAceAccount.Editable = nativeAcePassword.Editable = !busy;
        UpdateNativeAceButtons();
    }

    private void UpdateNativeAceButtons()
    {
        nativeAceConnectButton.Disabled = nativeAceBusy;
        nativeAceImportButton.Disabled = nativeAceBusy || SelectedNativeAceCharacterIndex() is null;
    }

    private void ClearNativeAceOnAddressChange()
    {
        if (nativeAceBusy || nativeAceCharacters.ItemCount == 0) return;
        CancelNativeAce();
        SetNativeAceCharacters([]);
        nativeAceStatus.Text = "Connect again to list characters on this account.";
    }

    private void FetchAceRider()
    {
        if (aceConnect.Disabled) return;
        if (!AceRiderSnapshot.TryEndpoint(aceAddress.Text, out var endpoint))
        {
            aceStatus.Text = "Enter the shard’s HTTPS appearance URL. Local testing also accepts 127.0.0.1.";
            return;
        }
        string code = aceCode.Text.Trim().Replace("-", "").ToUpperInvariant();
        if (!AceRiderSnapshot.IsPairingCode(code))
        {
            aceStatus.Text = "Enter the 20-character code from /gromnielook.";
            return;
        }
        var error = aceRequest.Request(endpoint!.AbsoluteUri, [$"Authorization: Bearer {code}"], Godot.HttpClient.Method.Post);
        if (error != Error.Ok)
        {
            aceStatus.Text = "Could not start the connection to the shard.";
            return;
        }
        aceConnect.Disabled = true;
        aceAddress.Editable = false;
        aceStatus.Text = "Fetching your character’s appearance…";
    }

    private void ReceiveAceRider(long result, long status, string[] headers, byte[] body)
    {
        aceConnect.Disabled = false;
        aceAddress.Editable = true;
        if (!aceRiderUi.Visible || !acePairingPane.Visible) return;
        if (result != (long)HttpRequest.Result.Success || status != 200)
        {
            aceStatus.Text = status switch
            {
                401 or 403 or 404 => "Code expired, was already used, or the endpoint is incorrect.",
                429 => "The shard is busy. Try again in a moment.",
                _ => "Could not fetch your appearance. Check the URL and shard bridge."
            };
            return;
        }
        AceRiderSnapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<AceRiderSnapshot>(body) ?? throw new InvalidDataException("Empty appearance.");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or ArgumentException or KeyNotFoundException)
        {
            aceStatus.Text = error is InvalidDataException ? error.Message : "The shard returned an unreadable appearance.";
            return;
        }
        if (TryApplyImportedRider(snapshot, out string message, aceAddress.Text.Trim())) aceCode.Clear();
        aceStatus.Text = message;
    }

    private bool TryApplyImportedRider(AceRiderSnapshot snapshot, out string message, string? legacyEndpoint = null)
    {
        if (!TryUseImportedRider(snapshot, out message, legacyEndpoint)) return false;
        LoadRiderTemplates();
        message = RememberImportedRiderTemplate(snapshot)
            ? $"Imported {snapshot.Name} and saved the look."
            : $"Imported {snapshot.Name}. Could not save a local template.";
        return true;
    }

    private bool TryUseImportedRider(AceRiderSnapshot snapshot, out string message, string? legacyEndpoint = null)
    {
        try { RebuildShowcase(snapshot.Resolve(assets), snapshot.Name); }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or KeyNotFoundException)
        {
            message = error is InvalidDataException ? error.Message : "Could not load the character’s appearance.";
            return false;
        }
        aceRider = snapshot;
        if (legacyEndpoint is not null) aceEndpoint = legacyEndpoint;
        aceUseCustom.Disabled = false;
        FillStable();
        SaveGarage();
        message = "";
        return true;
    }
}
