using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private static readonly Color Cream = new("fff3d2");
    private static readonly Color Soft = new("c5ccb1");
    private static readonly Color Gold = new("ffc461");
    private static readonly Color Mint = new("8ee6ba");
    private static readonly SystemFont FallbackFont = new() { FontNames = ["Bahnschrift", "Segoe UI"], FontWeight = 700 };
    private static readonly string[] InterfaceFontTypes = ["Label", "Button", "OptionButton", "LineEdit", "ItemList", "PopupMenu", "TooltipLabel"];
    private readonly List<Label> titleLabels = [];
    private Theme? interfaceTheme;
    private Control stableUi = null!, raceUi = null!, pauseUi = null!, resultsUi = null!, datUi = null!;
    private VBoxContainer options = null!;
    private Control courseUi = null!;
    private Button courseButton = null!;
    private LineEdit courseSearch = null!;
    private ItemList courseList = null!;
    private OptionButton weaponPicker = null!, shieldPicker = null!, mountPicker = null!;
    private Control shieldHud = null!;
    private Label shieldStatus = null!;
    private ColorRect shieldMeter = null!;
    private Label courseStatus = null!, mapName = null!, resultCourse = null!, itemHint = null!;
    private TextureRect itemIcon = null!;
    private Label stableName = null!, bestLabel = null!, portraitName = null!, placeText = null!, lapText = null!,
        timerText = null!, itemText = null!, speedText = null!, calloutText = null!, resultTitle = null!, resultTime = null!, datMessage = null!;
    private Control lapSplitHud = null!, resultStandings = null!;
    private Label lapSplitRider = null!, lapSplitTime = null!;
    private (MountedRacer? Rider, int Lap, float Crossing) shownLapSplit;
    private ColorRect driftMeter = null!, balanceMeter = null!;
    private Label gateText = null!, pyrealText = null!, lapHeader = null!;
    private LineEdit datField = null!;
    private TrackMap map = null!;
    private bool rebuilding;

    private void MakeUi()
    {
        bool mobile = OS.HasFeature("android");
        var canvas = new CanvasLayer { Layer = 2 };
        AddChild(canvas);
        stableUi = Layer(canvas);
        raceUi = Layer(canvas);
        pauseUi = Layer(canvas);
        resultsUi = Layer(canvas);
        datUi = Layer(canvas);
        courseUi = Layer(canvas);
        courseUi.Hide();
        raceUi.Hide();
        pauseUi.Hide();
        resultsUi.Hide();
        datUi.Hide();

        Plate(stableUi, 28, 30, 466, 740, "172821f6", "7b8d5b");
        Text(stableUi, "THE DERETH CUP", 52, 54, 340, 16, Gold);
        Text(stableUi, "GROMNIE", 49, 74, 420, 64, display: true);
        Text(stableUi, "GRAND PRIX", 51, 137, 420, 50, Mint, display: true);
        Plate(stableUi, 53, 209, 414, 1, "667951");
        Text(stableUi, "MOUNT", 53, 229, 106, 16, Cream);
        mountPicker = new OptionButton { Position = new(170, 217), Size = new(298, 42), Theme = ThemeForUi() };
        foreach (var mount in RaceMount.All) mountPicker.AddItem(mount.Name, (int)mount.Kind);
        mountPicker.ItemSelected += index => SelectMount((RaceMountKind)mountPicker.GetItemId((int)index));
        stableUi.AddChild(mountPicker);
        BuildMountColorPicker();

        Text(stableUi, "RIDER APPEARANCE", 53, 355, 330, 17, Cream);
        var riderOptions = new ScrollContainer
        {
            Position = new(52, 382), Size = new(419, 126), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true, Theme = ThemeForUi()
        };
        stableUi.AddChild(riderOptions);
        options = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        options.AddThemeConstantOverride("separation", 8);
        riderOptions.AddChild(options);

        Text(stableUi, "WEAPON  /  Q · E", 53, 515, 198, 14, Soft);
        Text(stableUi, "SHIELD  /  C", 266, 515, 195, 14, Soft);
        var weaponSelect = new OptionButton { Position = new(53, 537), Size = new(202, 43), Theme = ThemeForUi(),
            FitToLongestItem = false, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        for (int i = 0; i < RaceWeapon.All.Length; i++)
        {
            var weapon = RaceWeapon.All[i];
            weaponSelect.AddItem(weapon.Name);
            weaponSelect.GetPopup().SetItemTooltip(i, weapon.Description);
        }
        weaponSelect.ItemSelected += index =>
        {
            weaponIndex = (int)index;
            RebuildShowcase();
            SaveGarage();
        };
        stableUi.AddChild(weaponSelect);
        weaponPicker = weaponSelect;
        shieldPicker = new OptionButton { Position = new(266, 537), Size = new(202, 43), Theme = ThemeForUi(),
            FitToLongestItem = false, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        foreach (var shield in RaceShield.All)
        {
            string label = shield.Name == "Prismatic Diamond Shield" ? "Prismatic" :
                shield.Name.EndsWith(" Shield", StringComparison.Ordinal) ? shield.Name[..^7] : shield.Name;
            shieldPicker.AddItem(label);
            shieldPicker.GetPopup().SetItemTooltip(shieldPicker.ItemCount - 1, shield.Name + "\n" + shield.Description);
        }
        shieldPicker.ItemSelected += index =>
        {
            shieldIndex = (int)index;
            RebuildShowcase();
            SaveGarage();
        };
        stableUi.AddChild(shieldPicker);
        courseButton = Action(stableUi, "", 53, 594, 415, 43, OpenCoursePicker);
        bestLabel = Text(stableUi, "", 53, 644, 415, 17, Soft);
        Action(stableUi, "START RACE", 53, 673, 415, 59, StartRace, true);
        courseStatus = Text(stableUi, "", 54, 741, 416, 16, Soft);
        courseStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Plate(stableUi, 941, 31, 311, 73, "172821eb", "829763");
        Text(stableUi, "STABLE PREVIEW", 965, 44, 263, 16, Gold, HorizontalAlignment.Right);
        portraitName = Text(stableUi, "", 957, 66, 271, 24, Cream, HorizontalAlignment.Right);
        portraitName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Action(stableUi, "TRACK EDITOR", 1038, 114, 214, 41, OpenTrackEditor);
        Action(stableUi, "TURN LEFT", 913, 722, 153, 47, () => previewYaw -= .42f);
        Action(stableUi, "TURN RIGHT", 1077, 722, 175, 47, () => previewYaw += .42f);

        Plate(raceUi, 28, 27, 263, 102, "172821ec", "7a8f65");
        Text(raceUi, "POSITION", 46, 39, 112, 15, Gold);
        placeText = Text(raceUi, "", 42, 56, 121, 49);
        lapHeader = Text(raceUi, "LAP", 189, 39, 83, 15, Gold);
        lapText = Text(raceUi, "", 184, 65, 95, 29, Cream);
        Plate(raceUi, 28, 138, 132, 39, "172821ec", "637951");
        timerText = Text(raceUi, "", 38, 145, 111, 22, Cream, HorizontalAlignment.Center);
        Plate(raceUi, 28, 184, 164, 27, "172821dc", "637951");
        weatherLabel = Text(raceUi, "", 39, 188, 142, 13, Soft);
        lapSplitHud = new Control { Position = new(484, 27), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        raceUi.AddChild(lapSplitHud);
        Plate(lapSplitHud, 0, 0, 312, 38, "172821ec", "637951");
        lapSplitRider = Text(lapSplitHud, "", 13, 9, 146, 16);
        lapSplitRider.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        lapSplitTime = Text(lapSplitHud, "", 165, 9, 133, 16, Mint, HorizontalAlignment.Right);
        Plate(raceUi, 1068, 27, 184, 171, "172821ec", "7a8f65");
        mapName = Text(raceUi, "", 1082, 39, 156, 16, Gold, HorizontalAlignment.Center);
        mapName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        map = new TrackMap(racers) { Position = new(1083, 62), Size = new(154, 123), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        raceUi.AddChild(map);
        Plate(raceUi, 1068, 205, 184, 31, "172821ec", "7a8f65");
        gateText = Text(raceUi, "", 1072, 211, 176, 14, Gold, HorizontalAlignment.Center);
        Plate(raceUi, 1092, 243, 160, 27, "172821dc", "637951");
        raceViewLabel = Text(raceUi, "CHASE CAM", 1098, 247, 148, 13, Soft, HorizontalAlignment.Center);
        calloutText = Text(raceUi, "", 290, 180, 700, 57, Gold, HorizontalAlignment.Center);
        calloutText.AddThemeFontOverride("font", FallbackFont);
        calloutText.AddThemeColorOverride("font_outline_color", new("192920"));
        calloutText.AddThemeConstantOverride("outline_size", 11);

        int itemY = mobile ? 470 : 655;
        Plate(raceUi, 28, itemY, 322, 90, "172821ec", "7a8f65");
        itemIcon = new TextureRect { Position = new(45, itemY + 25), Size = new(38, 38), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
        itemIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
        raceUi.AddChild(itemIcon);
        Text(raceUi, mobile ? "ITEM" : "ITEM  /  F TO USE", 94, itemY + 7, 240, 13, Gold);
        pyrealText = Text(raceUi, "", 216, itemY + 7, 118, 13, Gold, HorizontalAlignment.Right);
        itemText = Text(raceUi, "", 94, itemY + 23, 244, 24);
        itemHint = Text(raceUi, "", 94, itemY + 56, 244, 13, Soft);
        itemHint.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        int meterY = mobile ? 425 : 610;
        Plate(raceUi, 1073, meterY, 179, 135, "172821ec", "7a8f65");
        Text(raceUi, "RIDER BALANCE", 1089, meterY + 9, 149, 13, Gold, HorizontalAlignment.Center);
        balanceMeter = Meter(raceUi, 1089, meterY + 32, new("e9ad60"));
        speedText = Text(raceUi, "", 1086, meterY + 50, 154, 27, Cream, HorizontalAlignment.Center);
        Text(raceUi, "DRIFT CHARGE", 1089, meterY + 88, 149, 13, Mint, HorizontalAlignment.Center);
        driftMeter = Meter(raceUi, 1089, meterY + 113, Mint);
        shieldHud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        raceUi.AddChild(shieldHud);
        Plate(shieldHud, 1073, meterY - 63, 179, 55, "172821ed", "52674b");
        shieldStatus = Text(shieldHud, "", 1088, meterY - 55, 151, 14, Mint);
        shieldMeter = Meter(shieldHud, 1089, meterY - 29, new Color("84eac6"));
        shieldHud.Hide();
        if (mobile) MakeRaceTouchUi();
        else
        {
            Plate(raceUi, 196, 751, 888, 34, "172821e7", "52674b");
            Text(raceUi, "WASD / ARROWS  Drive    SPACE  Drift    Q / E  Attack    C  Guard    F  Item    R  Air roll    V  Camera    ESC  Pause", 203, 757, 874, 14, Cream,
                HorizontalAlignment.Center);
        }
        BuildRabbitCamera();
        MakeNowPlayingHud();

        MakeRacePauseMenu(mobile);

        MakeModal(resultsUi, "", 189, 455);
        resultCourse = Text(resultsUi, "", 405, 216, 470, 21, Mint, HorizontalAlignment.Center);
        resultCourse.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        resultTitle = Text(resultsUi, "", 406, 256, 468, 50, Gold, HorizontalAlignment.Center, display: true);
        resultTime = Text(resultsUi, "", 416, 320, 448, 22, Cream, HorizontalAlignment.Center);
        resultStandings = new Control { Position = new(410, 365), MouseFilter = Control.MouseFilterEnum.Ignore };
        resultsUi.AddChild(resultStandings);
        resultsAgain = Action(resultsUi, "RACE AGAIN", 448, 515, 384, 53, OnlineRaceButton, true);
        Action(resultsUi, "BACK TO STABLE", 448, 580, 384, 45, StableButton);

        MakeModal(datUi, "FIND YOUR ASHERON’S CALL DATs", 250, 330);
        datMessage = Text(datUi, "", 423, 315, 434, 17, Soft, HorizontalAlignment.Center);
        datMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        datField = new LineEdit { Position = new(426, 401), Size = new(428, 44), Theme = ThemeForUi(), PlaceholderText = "Asheron's Call folder" };
        datField.TextSubmitted += folder => LoadGame(folder);
        datUi.AddChild(datField);
        Action(datUi, "BROWSE", 426, 468, 165, 49, Browse);
        Action(datUi, "LOAD DATs", 605, 468, 249, 49, () => LoadGame(datField.Text), true);
        if (mobile) MakeAndroidDatUi(canvas);
        MakeCoursePicker();
        MakeAceRiderUi(canvas);
        MakeTrackEditorUi(canvas);
        MakeOnlineUi(canvas);
    }

    private void MakeCoursePicker()
    {
        MakeModal(courseUi, "CHOOSE A CIRCUIT", 92, 624);
        courseSearch = new LineEdit
        {
            Position = new(421, 155), Size = new(438, 44), Theme = ThemeForUi(),
            PlaceholderText = "Search the towns of Dereth"
        };
        courseSearch.TextChanged += PopulateCoursePicker;
        courseSearch.TextSubmitted += _ => ChooseHighlightedCourse();
        courseUi.AddChild(courseSearch);
        courseList = new ItemList { Position = new(421, 211), Size = new(438, 394), Theme = ThemeForUi() };
        courseList.AddThemeStyleboxOverride("panel", Surface("20352b", "687f56", 8));
        courseList.AddThemeStyleboxOverride("selected_focus", Surface("43664a", "9bc693", 4));
        courseList.AddThemeStyleboxOverride("selected", Surface("365843", "82a07a", 4));
        courseList.AddThemeConstantOverride("v_separation", 6);
        courseList.ItemActivated += index => ChooseCourse(courseList.GetItemMetadata((int)index).AsInt32());
        courseUi.AddChild(courseList);
        Text(courseUi, "Medium 7 × 7   /   XL 8 × 8   /   Point-to-point", 421, 614, 438, 15, Soft);
        Action(courseUi, "CANCEL", 421, 644, 137, 42, CloseCoursePicker);
        Action(courseUi, "EDITOR", 570, 644, 103, 42, OpenTrackEditor);
        Action(courseUi, "SELECT", 685, 641, 174, 48, ChooseHighlightedCourse, true);
    }

    private void ChooseHighlightedCourse()
    {
        int[] selection = courseList.GetSelectedItems();
        if (selection.Length > 0) ChooseCourse(courseList.GetItemMetadata(selection[0]).AsInt32());
    }
    private void OpenCoursePicker()
    {
        courseSearch.Text = "";
        PopulateCoursePicker("");
        courseUi.Show();
        courseSearch.GrabFocus();
    }

    private void PopulateCoursePicker(string query)
    {
        courseList.Clear();
        bool matches(string name) => name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        void add(int id, string name)
        {
            if (!matches(name)) return;
            int index = courseList.AddItem(name);
            courseList.SetItemMetadata(index, id);
            if (id == selectedTown) courseList.Select(index);
        }
        add(VoidCourseId, VoidCourseName + "     ·     XL");
        if (spanSprint is { } sprint) add(sprint.Id, sprint.Name + "   ·   Obsidian Span");
        foreach (var custom in customTracks.Values.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            add(custom.Id, custom.Name + "     ·     CUSTOM");
        foreach (int id in xlRoutes.Keys.OrderBy(id => CourseName(id), StringComparer.OrdinalIgnoreCase))
            add(id, CourseName(id) + "     ·     8×8");
        foreach (var ridge in ridgeCircuits.Values.OrderBy(ridge => ridge.Name, StringComparer.OrdinalIgnoreCase))
            add(ridge.Id, ridge.Name + " XL");
        foreach (var circuit in landmarkCircuits.Values.OrderBy(circuit => circuit.Name, StringComparer.OrdinalIgnoreCase))
            add(circuit.Id, CourseName(circuit.Id) + $"     ·     {circuit.GridSide}×{circuit.GridSide}");
        foreach (int id in mediumRoutes.Keys.OrderBy(id => CourseName(id), StringComparer.OrdinalIgnoreCase))
            add(id, CourseName(id) + "     ·     7×7");
        if (courseList.ItemCount > 0 && courseList.GetSelectedItems().Length == 0) courseList.Select(0);
        if (courseList.ItemCount > 0) courseList.EnsureCurrentIsVisible();
    }

    private void UpdateRaceCourseLabels()
    {
        bool obsidian = ridgeCircuits.ContainsKey(activeTown) || landmarkCircuits.GetValueOrDefault(activeTown)?.Battle is not null;
        lapSplitHud.Hide();
        shownLapSplit = default;
        lapHeader.Text = track.IsSprint ? "ROUTE" : "LAP";
        mapName.Text = track.IsSprint ? "OBSIDIAN SPAN" :
            (obsidian ? track.Name[(track.Name.IndexOf(" - ", StringComparison.Ordinal) + 3)..] : track.Name).ToUpperInvariant();
        mapName.AddThemeFontSizeOverride("font_size", track.IsVoid || track.IsMedium ? 13 : obsidian || landmarkCircuits.ContainsKey(activeTown) ? 14 : 16);
        resultCourse.Text = track.Name.ToUpperInvariant() + " RESULTS";
        map.SetTrack(track, FlyoverMapPoints(), localRacerIndex,
            track.IsVoid ? [VoidAerbaxPosition, Vector3.Zero, VoidAsheronPosition] : []);
    }
    private Control Layer(CanvasLayer canvas)
    {
        var layer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = ThemeForUi() };
        canvas.AddChild(layer);
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return layer;
    }

    private void MakeModal(Control parent, string title, int y, int height)
    {
        var shade = new ColorRect { Color = new("101c19ae"), MouseFilter = Control.MouseFilterEnum.Stop };
        parent.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Plate(parent, 389, y, 502, height, "182922", "a4a66c");
        Text(parent, title, 405, y + 27, 470, 21, Mint, HorizontalAlignment.Center);
    }

    private static StyleBoxFlat Surface(string color, string border, int radius = 10, int width = 1)
    {
        var box = new StyleBoxFlat { BgColor = new(color), BorderColor = new(border), ContentMarginLeft = 12, ContentMarginRight = 12 };
        box.SetCornerRadiusAll(radius);
        box.SetBorderWidthAll(width);
        return box;
    }

    private static void Plate(Control parent, float x, float y, float w, float h, string color, string rim = "24382e")
    {
        var panel = new Panel { Position = new(x, y), Size = new(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", Surface(color, rim, h < 3 ? 0 : 13));
        parent.AddChild(panel);
    }

    private static ColorRect Meter(Control parent, float x, float y, Color tint)
    {
        Plate(parent, x - 1, y - 1, 151, 10, "334338", "536851");
        var fill = new ColorRect { Color = tint, Position = new(x, y), Size = new(149, 8),
            MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(fill);
        return fill;
    }
    private Label Text(Control parent, string text, float x, float y, float w, int size, Color? color = null,
        HorizontalAlignment align = HorizontalAlignment.Left, bool display = false)
    {
        var label = new Label
        {
            Text = text, Position = new(x, y), Size = new(w, 0), HorizontalAlignment = align,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Cream);
        parent.AddChild(label);
        if (display) titleLabels.Add(label);
        return label;
    }

    private void ApplyUiFonts(DatArchive dat)
    {
        Font? Load(uint id, int nominalSize)
        {
            try { return DatUiFont.Load(dat, id, nominalSize, FallbackFont); }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                GD.PushWarning($"Could not load DAT font {id:X8}. {error.Message}");
                return null;
            }
        }

        var body = Load(0x4000000c, 19);
        var display = Load(0x40000014, 42);
        SetInterfaceFont(ThemeForUi(), body ?? FallbackFont);
        foreach (var label in titleLabels)
            if (display is not null) label.AddThemeFontOverride("font", display);
            else label.RemoveThemeFontOverride("font");
    }

    private Theme ThemeForUi()
    {
        if (interfaceTheme is not null) return interfaceTheme;
        var theme = new Theme { DefaultFontSize = 16 };
        SetInterfaceFont(theme, FallbackFont);
        foreach (string type in new[] { "Button", "OptionButton", "LineEdit" })
        {
            theme.SetStylebox("normal", type, Surface("263d31", "6a845e", 7));
            theme.SetStylebox("hover", type, Surface("395844", "a6bf88", 7));
            theme.SetStylebox("pressed", type, Surface("42634b", "c1de9b", 7));
            theme.SetStylebox("focus", type, Surface("00000000", "ffe09d", 7, 2));
            theme.SetColor("font_color", type, Cream);
            theme.SetColor("font_hover_color", type, Cream);
        }
        theme.SetStylebox("panel", "PopupMenu", Surface("263d31", "6a845e", 8));
        theme.SetStylebox("slider", "HSlider", Surface("415841", "7a925b", 5));
        theme.SetStylebox("grabber_area", "HSlider", Surface("4d9169", "4d9169", 5));
        return interfaceTheme = theme;
    }

    private static void SetInterfaceFont(Theme theme, Font font)
    {
        theme.DefaultFont = font;
        foreach (string type in InterfaceFontTypes) theme.SetFont("font", type, font);
    }

    private Button Action(Control parent, string title, int x, int y, int w, int h,
        System.Action pressed, bool primary = false)
    {
        var button = new Button { Text = title, Position = new(x, y), Size = new(w, h), Theme = ThemeForUi() };
        if (primary)
        {
            button.AddThemeStyleboxOverride("normal", Surface("ffc461", "ffd88b", 8, 2));
            button.AddThemeStyleboxOverride("hover", Surface("ffda88", "ffe4a3", 8, 2));
            button.AddThemeStyleboxOverride("pressed", Surface("eaaa4b", "ffe4a3", 8, 2));
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                button.AddThemeColorOverride(state, new("1d3027"));
            button.AddThemeFontSizeOverride("font_size", 23);
        }
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private void FillStable()
    {
        rebuilding = true;
        foreach (var child in options.GetChildren()) { options.RemoveChild(child); child.QueueFree(); }
        if (aceRider is { } imported)
        {
            var label = new Label { Text = imported.Name, Theme = ThemeForUi(),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            options.AddChild(label);
            var custom = new Button { Text = "Use custom rider", Theme = ThemeForUi() };
            custom.Pressed += UseCustomRider;
            options.AddChild(custom);
            rebuilding = false;
            UpdateStableLabels();
            return;
        }
        var heritage = creation.Find(appearance.NativeHeritageId)!;
        var gender = creation.FindGender(appearance)!;
        var heritages = creation.Rows.ToArray();
        AddChoice("Heritage", heritages.Select(r => r.Name), Array.IndexOf(heritages, heritage), i =>
        {
            appearance = new CharacterAppearance { NativeHeritageId = heritages[i].Id, GenderId = heritages[i].Genders[0].Id };
            ApplyLook(true);
        });
        var bodies = heritage.Genders.ToArray();
        AddChoice("Body", bodies.Select(r => r.Name), Array.IndexOf(bodies, gender), i =>
        {
            appearance = new CharacterAppearance { NativeHeritageId = heritage.Id, GenderId = bodies[i].Id };
            ApplyLook(true);
        });
        AddNumberChoice("Hair style", gender.HairStyles.Count, appearance.HairStyle, i => appearance.HairStyle = i);
        AddNumberChoice("Hair color", gender.HairColors.Count, appearance.HairColor, i => appearance.HairColor = i);
        AddNumberChoice("Eye color", gender.EyeColors.Count, appearance.EyeColor, i => appearance.EyeColor = i);
        AddNumberChoice("Eyes", gender.EyeStrips.Count, appearance.EyeStrip, i => appearance.EyeStrip = i);
        AddNumberChoice("Nose", gender.NoseStrips.Count, appearance.NoseStrip, i => appearance.NoseStrip = i);
        AddNumberChoice("Mouth", gender.MouthStrips.Count, appearance.MouthStrip, i => appearance.MouthStrip = i);
        AddShade();
        AddGear("Headgear", gender.Headgears, appearance.HeadgearStyle, i => appearance.HeadgearStyle = i);
        AddGear("Shirt", gender.Shirts, appearance.ShirtStyle, i => appearance.ShirtStyle = i);
        AddGear("Trousers", gender.Pants, appearance.TrousersStyle, i => appearance.TrousersStyle = i);
        AddGear("Footwear", gender.Footwear, appearance.FootwearStyle, i => appearance.FootwearStyle = i);
        AddNumberChoice("Headgear dye", gender.ClothingColors.Count, appearance.HeadgearColor, i => appearance.HeadgearColor = i);
        AddNumberChoice("Shirt dye", gender.ClothingColors.Count, appearance.ShirtColor, i => appearance.ShirtColor = i);
        AddNumberChoice("Trouser dye", gender.ClothingColors.Count, appearance.TrousersColor, i => appearance.TrousersColor = i);
        AddNumberChoice("Footwear dye", gender.ClothingColors.Count, appearance.FootwearColor, i => appearance.FootwearColor = i);
        rebuilding = false;
        UpdateStableLabels();
    }

    private void AddGear(string label, IReadOnlyList<CharacterCreationCatalog.GearOption> gear, int selected, System.Action<int> set)
    {
        if (gear.Count > 1)
            AddChoice(label, gear.Select((opt, i) => opt.Name.Length == 0 ? $"Style {i + 1}" : opt.Name), selected, i => { set(i); ApplyLook(false); });
    }

    private void AddNumberChoice(string label, int count, int selected, System.Action<int> set)
    {
        if (count > 1)
            AddChoice(label, Enumerable.Range(1, count).Select(i => $"{label} {i}"), selected, i => { set(i); ApplyLook(false); });
    }

    private HBoxContainer AddRow(string name)
    {
        var row = new HBoxContainer { CustomMinimumSize = new(386, 35) };
        row.AddThemeConstantOverride("separation", 12);
        options.AddChild(row);
        var label = new Label { Text = name, CustomMinimumSize = new(118, 0), Theme = ThemeForUi() };
        label.Modulate = Soft;
        row.AddChild(label);
        return row;
    }

    private void AddChoice(string name, IEnumerable<string> choices, int index, System.Action<int> set)
    {
        var row = AddRow(name);
        var select = new OptionButton
        {
            CustomMinimumSize = new(200, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FitToLongestItem = false, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, Theme = ThemeForUi()
        };
        foreach (string choice in choices) select.AddItem(choice);
        select.Select(index);
        select.ItemSelected += i => { if (!rebuilding) set((int)i); };
        row.AddChild(select);
    }

    private void AddShade()
    {
        var row = AddRow("Skin shade");
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = .1, Value = appearance.SkinShade, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        slider.ValueChanged += value => { appearance.SkinShade = (float)value; ApplyLook(false); };
        row.AddChild(slider);
    }

    private void SelectMount(RaceMountKind kind)
    {
        selectedMount = kind;
        previewYaw = MountPreviewYaw(kind);
        ApplyLook(false);
        AimGarage();
    }

    private static float MountPreviewYaw(RaceMountKind kind) => kind switch
    {
        RaceMountKind.Asheron => -1.15f,
        RaceMountKind.BaelZharon => -2.48f,
        _ => -2.23f
    };

    private void ApplyLook(bool refill)
    {
        RebuildShowcase();
        if (refill) FillStable();
        else UpdateStableLabels();
        SaveGarage();
    }

    private void UpdateStableLabels()
    {
        weaponPicker.Select(weaponIndex);
        shieldPicker.Select(shieldIndex);
        mountPicker.Select((int)selectedMount);
        UpdateMountColorPicker();
        bestLabel.Text = best >= 0 ? $"PERSONAL BEST   {FormatTime(best)}" : "PERSONAL BEST   —";
        courseButton.Text = $"COURSE   ·   {SelectedCourseName}";
        courseStatus.Text = courseProblem.Length > 0 ? courseProblem
            : selectedTown == VoidCourseId ? "5 laps  /  23.1 km total  /  Singularity flyover"
            : CustomCourse(selectedTown) is { } custom ? $"Custom circuit  /  {custom.GridSide} × {custom.GridSide}  /  {custom.Width:0} m wide"
            : spanSprint?.Id == selectedTown ? "Point-to-point  /  Obsidian Span  /  Cragstone finish"
            : ridgeCircuits.ContainsKey(selectedTown) ? "1 lap  /  8 × 8 landblocks  /  summit flight"
            : landmarkCircuits.TryGetValue(selectedTown, out var landmark) ? $"{landmark.GridSide} × {landmark.GridSide} landblocks  /  {landmark.Subtitle}"
            : xlRoutes.TryGetValue(selectedTown, out var xl) ? $"1 lap  /  8 × 8 landblocks  /  {xl.HalfWidth * 2:0.#} m wide"
            : mediumRoutes.TryGetValue(selectedTown, out var medium) ? $"1 lap  /  7 × 7 landblocks  /  {medium.HalfWidth * 2:0.#} m wide" : "";
    }

    private void UpdateRaceUi()
    {
        UpdateRaceTouchFlightLabels();
        placeText.Text = $"{place}/{RacerCount}";
        lapText.Text = track.IsSprint ? $"{Mathf.Clamp(Mathf.RoundToInt(100 * Player.Distance / track.Gate(track.GateCount)), 0, 100)}%" : $"{lap}/{track.Laps}";
        timerText.Text = FormatTime(Player.Finish >= 0 ? Player.Finish : elapsed);
        UpdateLapSplitHud();
        itemText.Text = ItemName(Player.Item) + (Player.ItemCharges > 1 ? $" ×{Player.ItemCharges}" : "");
        pyrealText.Text = Player.Pyreals > 0 ? $"PYREALS  {Player.Pyreals}/{MaxPyreals}" : "";
        itemText.Modulate = Player.Item == RaceItem.None ? Soft : Mint;
        itemIcon.Texture = ItemIcon(Player.Item);
        itemHint.Text = IsSudsed(Player) ? $"CONTROLS FLIPPED  ·  {Mathf.CeilToInt(Player.SudsedUntil - elapsed)}s"
            : Player.Flight is { } flight ? FlightControlHint(flight) : Player.ResetHold > 0 ? "Recalled to the last checkpoint" : Player.Frozen > 0 ? "Frostbitten · reduced speed"
            : Player.Phased > 0 ? "Ethereal · attacks pass through"
            : Player.Hexed > 0 ? "Zefir Hex · rabbit form"
            : Player.Might > 0 ? "Tusker Might · giant form"
            : Player.Invulnerable > 0 ? "Invulnerability active"
            : Player.Ward > 0 ? "Portal Ward active" : ItemHint(Player.Item);
        UpdateRaceSpeedUi();
        driftMeter.Size = new Vector2(149 * charge / 1.5f, 8);
        balanceMeter.Size = new Vector2(149 * Player.Stability / 100f, 8);
        UpdateShieldUi();
        int gate = track.IsSprint ? Player.GatesCleared : Player.GatesCleared % track.GateCount;
        gateText.Text = Player.Finish >= 0 && !finished ? "FINISHED · WAITING"
            : track.IsSprint && gate >= track.GateCount ? "CRAGSTONE FINISH"
            : Player.Finish >= 0 ? "FINISHED"
            : Player.Flight is not null ? $"AIRBORNE  ·  GATE {gate + 1}/{track.GateCount}"
            : Player.FreeDriving ? $"OFF TRACK  ·  GATE {gate + 1}/{track.GateCount}" : $"NEXT GATE   {gate + 1}/{track.GateCount}";
        calloutText.Text = countdown > 0 ? $"{Math.Ceiling(countdown)}" : notification > 0 ? message : "";
        map.QueueRedraw();
    }

    private void UpdateLapSplitHud()
    {
        const float displaySeconds = 3.6f;
        MountedRacer? latestRider = null;
        float latestCrossing = elapsed - displaySeconds;
        foreach (var racer in racers)
        {
            if (racer.LapCrossings.Count == 0) continue;
            float crossing = racer.LapCrossings[^1];
            if (!(crossing > latestCrossing && crossing <= elapsed)) continue;
            latestRider = racer;
            latestCrossing = crossing;
        }

        lapSplitHud.Visible = latestRider is not null && countdown <= 0 && !finished;
        if (!lapSplitHud.Visible || latestRider is null) return;
        lapSplitHud.Modulate = new Color(1, 1, 1, 1 - Mathf.SmoothStep(2.65f, displaySeconds, elapsed - latestCrossing));
        int completedLap = latestRider.LapCrossings.Count;
        var split = (latestRider, completedLap, latestCrossing);
        if (shownLapSplit == split) return;
        shownLapSplit = split;
        lapSplitRider.Text = latestRider == Player ? "You" : latestRider.Name;
        string label = track.Laps == 1 ? "FINISH" : $"LAP {completedLap}";
        lapSplitTime.Text = $"{label}  ·  {LapSplitDuration(latestRider, completedLap - 1)}";
    }

    private void UpdateRaceSpeedUi()
    {
        speedText.Text = UseMilesPerHour
            ? $"{Mathf.RoundToInt(Player.Speed / MetresPerSecondPerMph)} mph"
            : $"{Mathf.RoundToInt(Player.Speed * 3.6f)} km/h";
    }

    private void ShowResults(bool record)
    {
        raceTouchUi?.Hide();
        UpdateRaceViewUi();
        ClearRabbitCamera();
        UpdateRaceUi();
        string[] ordinals = ["", "1ST", "2ND", "3RD", "4TH", "5TH"];
        resultTitle.Text = Player.Finish < 0 ? "DID NOT FINISH" : $"{ordinals[place]} PLACE";
        resultTime.Text = Player.Finish < 0 ? "The finish window expired." : FormatTime(Player.Finish) + (record ? "   /   PERSONAL BEST" : "");
        ShowResultStandings();
        if (track.IsVoid && BeginVoidFinaleCamera()) return;
        PlayRaceResultsMusic();
        resultsUi.Show();
    }

    private void ShowResultStandings()
    {
        foreach (var child in resultStandings.GetChildren())
        {
            resultStandings.RemoveChild(child);
            child.QueueFree();
        }

        int splits = track.Laps > 1 ? track.Laps : 0;
        int nameWidth = splits > 0 ? 115 : 315;
        int totalWidth = splits > 0 ? 65 : 145;
        float splitWidth = (460f - nameWidth - totalWidth) / Math.Max(1, splits);
        Text(resultStandings, "RIDER", 0, 0, nameWidth, 13, Gold);
        Text(resultStandings, "TOTAL", nameWidth, 0, totalWidth, 13, Gold, HorizontalAlignment.Right);
        for (int i = 0; i < splits; i++)
            Text(resultStandings, $"LAP {i + 1}", nameWidth + totalWidth + splitWidth * i, 0, splitWidth, 13, Gold, HorizontalAlignment.Right);

        int y = 22;
        var standings = racers.OrderBy(r => r.Finish >= 0 ? r.Finish : float.MaxValue).ThenByDescending(RaceProgress);
        foreach (var racer in standings)
        {
            var color = racer == Player ? Mint : Soft;
            var name = Text(resultStandings, racer.Name, 0, y, nameWidth, 14, color);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            string total = racer.Finish >= 0 ? FormatTime(racer.Finish) : OnlineRace || track.IsVoid ? "DNF" : "Behind you";
            Text(resultStandings, total, nameWidth, y, totalWidth, 14, color, HorizontalAlignment.Right);
            for (int i = 0; i < splits; i++)
                Text(resultStandings, i < racer.LapCrossings.Count ? LapSplitDuration(racer, i) : "—",
                    nameWidth + totalWidth + splitWidth * i, y, splitWidth, 14, color, HorizontalAlignment.Right);
            y += 22;
        }
    }

    private static string LapSplitDuration(MountedRacer racer, int index) => FormatTime(racer.LapCrossings[index] -
        (index == 0 ? 0 : racer.LapCrossings[index - 1]));

    private static string FormatTime(float seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.ToString(@"m\:ss\.ff", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ShowDatPrompt(string folder, string message)
    {
        stableUi.Hide();
        datUi.Show();
        datField.Text = folder;
        datMessage.Text = message;
    }

    private void Browse()
    {
        var dialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true };
        AddChild(dialog);
        dialog.DirSelected += folder => { LoadGame(folder); dialog.QueueFree(); };
        dialog.Canceled += dialog.QueueFree;
        dialog.Popup();
    }
}

public partial class TrackMap : Control
{
    private BogCircuit? track;
    private readonly List<MountedRacer> racers;
    private readonly Control courseLayer = new()
    {
        MouseFilter = MouseFilterEnum.Ignore,
        ShowBehindParent = true
    };
    private Vector2[] road = [];
    private (int Index, float Height)[] voidDeck = [];
    private Vector2[] skyRoute = [];
    private Vector2[] battleStages = [];
    private Vector2 center;
    private float scale;
    private int playerIndex;

    public TrackMap(List<MountedRacer> racers)
    {
        this.racers = racers;
        AddChild(courseLayer);
        courseLayer.Draw += DrawCourse;
    }

    public void SetTrack(BogCircuit value, Vector3[] flyover, int localIndex, Vector3[] stages)
    {
        track = value;
        playerIndex = localIndex;
        var points = value.Curve.GetBakedPoints();
        var min = new Vector2(points.Min(p => p.X), points.Min(p => p.Z));
        var max = new Vector2(points.Max(p => p.X), points.Max(p => p.Z));
        if (value.GridSide > 1)
        {
            min = value.NativeBounds.Position;
            max = value.NativeBounds.End;
        }
        foreach (Vector3 stage in stages)
        {
            var at = new Vector2(stage.X, stage.Z);
            min = min.Min(at);
            max = max.Max(at);
        }
        var span = max - min;
        center = (min + max) / 2;
        scale = Math.Min((Size.X - 17) / Math.Max(1, span.X), (Size.Y - 17) / Math.Max(1, span.Y));
        int samples = value.IsVoid ? 240 : 120;
        var route = Enumerable.Range(0, samples + 1).Select(i => value.Point(i * value.Length / samples)).ToArray();
        road = route.Select(Plot).ToArray();
        voidDeck = value.IsVoid ? Enumerable.Range(0, samples)
            .Select(i => (Index: i, Height: (route[i].Y + route[i + 1].Y) * .5f)).OrderBy(segment => segment.Height).ToArray() : [];
        skyRoute = flyover.Select(Plot).ToArray();
        battleStages = stages.Select(Plot).ToArray();
        courseLayer.QueueRedraw();
        QueueRedraw();
    }

    private Vector2 Plot(Vector3 point) => Size / 2 + (new Vector2(point.X, point.Z) - center) * scale;
    private void DrawCourse()
    {
        if (track is null || road.Length < 2) return;
        if (track.GridSide > 1)
        {
            int cells = track.GridSide;
            var bounds = track.NativeBounds;
            Vector2 corner = Plot(new Vector3(bounds.Position.X, 0, bounds.Position.Y));
            Vector2 step = bounds.Size * scale / cells;
            var gridColor = new Color("6178618a");
            for (int cell = 0; cell <= cells; cell++)
            {
                Vector2 column = corner + new Vector2(step.X * cell, 0);
                Vector2 row = corner + new Vector2(0, step.Y * cell);
                courseLayer.DrawLine(column, column + new Vector2(0, step.Y * cells), gridColor, .75f, true);
                courseLayer.DrawLine(row, row + new Vector2(step.X * cells, 0), gridColor, .75f, true);
            }
        }
        if (track.IsVoid)
        {
            foreach (var (segment, height) in voidDeck)
            {
                Color tint = new Color("6fa7e8").Lerp(new Color("ee99df"), Mathf.Clamp((height - 32) / 130, 0, 1));
                courseLayer.DrawLine(road[segment], road[segment + 1], new Color("322b50"), 5, true);
                courseLayer.DrawLine(road[segment], road[segment + 1], tint, 2.25f, true);
            }
        }
        else
        {
            courseLayer.DrawPolyline(road, new Color("405947"), 8, true);
            courseLayer.DrawPolyline(road, new Color("819274"), 2.3f, true);
        }
        if (battleStages.Length == 3)
        {
            courseLayer.DrawCircle(battleStages[0], 3.3f, new Color("ff5367"), true, -1, true);
            courseLayer.DrawCircle(battleStages[1], 5, new Color("c385ff"), false, 1.5f, true);
            courseLayer.DrawCircle(battleStages[2], 3.3f, new Color("ffe29d"), true, -1, true);
        }
        if (track.IsSprint)
        {
            courseLayer.DrawCircle(road[0], 3.1f, new Color("b4ead5"), true, -1, true);
            courseLayer.DrawCircle(road[^1], 3.5f, new Color("ffc978"), true, -1, true);
        }
        for (int i = 1; i < skyRoute.Length; i += 2)
            courseLayer.DrawLine(skyRoute[i - 1], skyRoute[i], new Color("92dfd2"), 1.35f, true);
    }

    public override void _Draw()
    {
        if (track is null || road.Length < 2) return;
        if (racers.Count > playerIndex)
        {
            Vector2 gate = Plot(track.Point(track.Gate(racers[playerIndex].GatesCleared)));
            DrawCircle(gate, 8, new Color("ffd37c"), false, 2, true);
        }
        for (int i = racers.Count - 1; i >= 0; i--)
        {
            bool you = i == playerIndex;
            if (!you && racers[i].Phased > 0) continue;
            var position = Plot(racers[i].CoursePosition(track)).Clamp(Vector2.One * 6, Size - Vector2.One * 6);
            DrawCircle(position, you ? 6 : 4, new Color("172821"), true, -1, true);
            DrawCircle(position, you ? 4.5f : 2.8f, new Color(you ? "ffcf76" : "a2e0bb"), true, -1, true);
        }
    }
}










