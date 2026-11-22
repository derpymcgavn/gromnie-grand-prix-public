using Godot;

public partial class GrandPrix
{
    private const string TouchLayoutFile = "user://touch-layout.cfg";
    private const float MinTouchScale = .75f, MaxTouchScale = 1.35f;
    private static readonly Vector2 TouchCanvas = new(1280, 800), TouchMargin = new(16, 16);
    private static readonly Rect2 TouchToolbar = new(280, 105, 720, 238);
    private readonly HashSet<Key> touchPressed = [];
    private readonly Dictionary<Key, TouchWidget> touchWidgets = [];
    private Dictionary<Key, TouchPlacement> touchLayout = [];
    private Dictionary<Key, TouchPlacement>? touchLayoutBeforeEdit;
    private Control? raceTouchUi, touchLayoutMenu;
    private RaceTouchStick? touchStick;
    private Button? touchLayoutButton, touchModeButton;
    private Label? touchLayoutStatus;
    private bool touchFlightPreview, touchAirborne;
    private Key? selectedTouchControl;
    private int layoutFinger = -1;
    private Vector2 layoutDragOffset;

    private sealed record TouchWidget(Control View, string Title, Vector2 DefaultPosition, TouchScreenButton? Hitbox = null);
    private readonly record struct TouchPlacement(Vector2 Position, float Scale = 1);
    private bool EditingTouchLayout => touchLayoutMenu?.Visible == true;

    private void MakeRaceTouchUi()
    {
        var controls = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        raceTouchUi = controls;
        raceUi.AddChild(controls);
        controls.VisibilityChanged += () =>
        {
            if (!controls.IsVisibleInTree()) ClearTouchInput();
        };

        touchStick = new RaceTouchStick { Size = new(202, 202), MouseFilter = Control.MouseFilterEnum.Ignore };
        controls.AddChild(touchStick);
        touchWidgets.Add(Key.None, new(touchStick, "Joystick", new(34, 570)));
        AddTouchAction(Key.Q, "HIT LEFT", 900, 578, 106, 74);
        AddTouchAction(Key.C, "GUARD", 1021, 578, 106, 74);
        AddTouchAction(Key.E, "HIT RIGHT", 1142, 578, 106, 74);
        AddTouchAction(Key.F, "ITEM", 922, 666, 148, 106);
        AddTouchAction(Key.Space, "DRIFT", 1084, 666, 164, 106, held: true);
        AddTouchAction(Key.R, "AIR ROLL", 1084, 666, 164, 106);
        AddTouchAction(Key.V, "CAMERA", 827, 27, 104, 60);
        AddTouchAction(Key.Escape, "PAUSE", 943, 27, 104, 60);

        touchLayout = testMode ? DefaultTouchLayout() : ReadTouchLayout(TouchLayoutFile);
        ApplyTouchLayout();
        ShowTouchMode(airborne: false);
    }

    private void AddTouchAction(Key key, string title, int x, int y, int width, int height, bool held = false)
    {
        var size = new Vector2(width, height);
        var normal = held ? Surface("efd196eb", "fff3c8", 19, 2) : Surface("172821dc", "a4a66c", 17, 2);
        var active = Surface("c9e8ac", "f3ffdf", 17, 2);
        var ink = new Color("172921");
        var panel = new Panel { Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", normal);
        raceTouchUi!.AddChild(panel);
        var label = new Label
        {
            Size = size, Text = title, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", height >= 100 ? 23 : 17);
        label.AddThemeColorOverride("font_color", held ? ink : Cream);
        panel.AddChild(label);
        var touch = new TouchScreenButton
        {
            Position = size * .5f, Shape = new RectangleShape2D { Size = size }, ShapeCentered = false
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
            label.AddThemeColorOverride("font_color", held ? ink : Cream);
            if (held) touchPressed.Remove(key);
        };
        touchWidgets.Add(key, new(panel, title, new(x, y), touch));
    }

    private static bool VisibleTouchControl(Key key, bool airborne) => key switch
    {
        Key.R => airborne,
        Key.Space or Key.F or Key.Q or Key.E => !airborne,
        _ => true
    };

    private void ShowTouchMode(bool airborne)
    {
        touchStick?.SetVerticalLabels(airborne, FreeFlightControls);
        foreach (var (key, widget) in touchWidgets)
            widget.View.Visible = VisibleTouchControl(key, airborne);
    }

    private void UpdateRaceTouchMode()
    {
        if (touchStick is null || EditingTouchLayout) return;
        bool airborne = Player.Flight is not null;
        touchStick.SetVerticalLabels(airborne, FreeFlightControls);
        if (airborne == touchAirborne) return;
        touchAirborne = airborne;
        if (airborne) touchPressed.Remove(Key.Space);
        ShowTouchMode(airborne);
    }

    private void ClearTouchInput()
    {
        touchPressed.Clear();
        touchStick?.Reset();
        layoutFinger = -1;
    }

    private bool IsTouchHeld(Key key) => touchPressed.Contains(key) ||
        key == Key.W && (touchStick?.Throttle == true || (!touchAirborne && touchPressed.Contains(Key.Space))) ||
        key == Key.S && touchStick?.Brake == true;

    private float LocalSteering => Mathf.Clamp(
        (Has(Key.D, Key.Right) ? 1 : 0) - (Has(Key.A, Key.Left) ? 1 : 0) + (touchStick?.Steering ?? 0), -1, 1);

    private bool HandleRaceTouchInput(InputEvent input)
    {
        if (EditingTouchLayout) return HandleTouchLayoutDrag(input);
        return raceTouchUi?.IsVisibleInTree() == true && racing && !paused && !finished &&
            (input is InputEventScreenTouch or InputEventScreenDrag) && touchStick!.HandleTouch(input);
    }

    private Dictionary<Key, TouchPlacement> DefaultTouchLayout() =>
        touchWidgets.ToDictionary(entry => entry.Key, entry => new TouchPlacement(entry.Value.DefaultPosition));

    private void ApplyTouchLayout()
    {
        foreach (var (key, placement) in touchLayout)
        {
            var view = touchWidgets[key].View;
            view.Position = placement.Position;
            view.Scale = Vector2.One * placement.Scale;
            view.Modulate = selectedTouchControl == key ? new Color("c0f5bc") : Colors.White;
        }
    }

    private Rect2 TouchBounds(Key key, TouchPlacement placement) =>
        new(placement.Position, touchWidgets[key].View.Size * placement.Scale);

    private TouchPlacement ConstrainTouchPlacement(Key key, TouchPlacement placement)
    {
        float scale = float.IsFinite(placement.Scale) ? Math.Clamp(placement.Scale, MinTouchScale, MaxTouchScale) : 1;
        Vector2 size = touchWidgets[key].View.Size * scale;
        Vector2 position = placement.Position.IsFinite() ? placement.Position : touchWidgets[key].DefaultPosition;
        return new(position.Clamp(TouchMargin, TouchCanvas - TouchMargin - size), scale);
    }

    private bool TouchLayoutFits(Dictionary<Key, TouchPlacement> layout)
    {
        foreach (var (key, placement) in layout)
        {
            Rect2 bounds = TouchBounds(key, placement);
            if (bounds.Intersects(TouchToolbar)) return false;
            foreach (var (other, otherPlacement) in layout)
            {
                if ((int)other <= (int)key) continue;
                bool together = VisibleTouchControl(key, false) && VisibleTouchControl(other, false) ||
                    VisibleTouchControl(key, true) && VisibleTouchControl(other, true);
                if (together && bounds.Intersects(TouchBounds(other, otherPlacement))) return false;
            }
        }
        return true;
    }

    private Dictionary<Key, TouchPlacement> ReadTouchLayout(string path)
    {
        var layout = DefaultTouchLayout();
        using var config = new ConfigFile();
        if (config.Load(path) != Error.Ok) return layout;
        foreach (Key key in touchWidgets.Keys)
        {
            string section = key.ToString();
            var current = layout[key];
            var position = config.GetValue(section, "position", current.Position);
            var scale = config.GetValue(section, "scale", current.Scale);
            if (position.VariantType == Variant.Type.Vector2 && scale.VariantType is Variant.Type.Float or Variant.Type.Int)
                layout[key] = ConstrainTouchPlacement(key, new(position.AsVector2(), scale.AsSingle()));
        }
        return TouchLayoutFits(layout) ? layout : DefaultTouchLayout();
    }

    private Error WriteTouchLayout(string path)
    {
        using var config = new ConfigFile();
        foreach (var (key, placement) in touchLayout)
        {
            string section = key.ToString();
            config.SetValue(section, "position", placement.Position);
            config.SetValue(section, "scale", placement.Scale);
        }
        return config.Save(path);
    }

    private void MakeTouchLayoutMenu()
    {
        var menu = PausePanel(pauseUi, "TouchLayout");
        touchLayoutMenu = menu;
        var shade = new ColorRect { Color = new("101c1980"), MouseFilter = Control.MouseFilterEnum.Stop };
        menu.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Plate(menu, 280, 105, 720, 238, "182922", "a4a66c");
        Text(menu, "TOUCH LAYOUT", 300, 117, 680, 23, Gold, HorizontalAlignment.Center);
        touchLayoutStatus = Text(menu, "Drag a control or tap it to resize.", 296, 154, 688, 17, Cream, HorizontalAlignment.Center);
        Action(menu, "SMALLER", 300, 190, 104, 51, () => ResizeTouchControl(-.1f));
        Action(menu, "LARGER", 411, 190, 104, 51, () => ResizeTouchControl(.1f));
        Action(menu, "SWAP SIDES", 522, 190, 132, 51, MirrorTouchLayout);
        Action(menu, "RESET", 661, 190, 88, 51, () =>
        {
            touchLayout = DefaultTouchLayout();
            ApplyTouchLayout();
            touchLayoutStatus.Text = "Default layout restored.";
        });
        touchModeButton = Action(menu, "EDIT FLIGHT", 756, 190, 224, 51, () =>
        {
            touchFlightPreview = !touchFlightPreview;
            SetTouchLayoutPreview();
        });
        Action(menu, "CANCEL", 527, 274, 106, 48, () => CloseTouchLayout(save: false, focusMenu: true));
        Action(menu, "DONE", 647, 274, 106, 48, () => CloseTouchLayout(save: true, focusMenu: true), true);
        menu.Hide();
    }

    private void OpenTouchLayout()
    {
        if (raceTouchUi is null || touchLayoutMenu is null || EditingTouchLayout) return;
        ClearTouchInput();
        touchLayoutBeforeEdit = new(touchLayout);
        selectedTouchControl = null;
        touchFlightPreview = touchAirborne;
        racePauseMenu.Hide();
        raceTouchUi.Reparent(touchLayoutMenu, keepGlobalTransform: false);
        foreach (var widget in touchWidgets.Values) widget.Hitbox?.Hide();
        raceTouchUi.Show();
        touchLayoutMenu.Show();
        SetTouchLayoutPreview();
    }

    private void SetTouchLayoutPreview()
    {
        layoutFinger = -1;
        selectedTouchControl = null;
        touchModeButton!.Text = touchFlightPreview ? "EDIT GROUND" : "EDIT FLIGHT";
        touchLayoutStatus!.Text = touchFlightPreview ? "Drag a flight control or tap it to resize." : "Drag a control or tap it to resize.";
        ShowTouchMode(touchFlightPreview);
        ApplyTouchLayout();
    }

    private void CloseTouchLayout(bool save, bool focusMenu = false)
    {
        if (!EditingTouchLayout) return;
        if (save && !testMode && WriteTouchLayout(TouchLayoutFile) != Error.Ok)
        {
            touchLayoutStatus!.Text = "Could not save the layout. Try again.";
            return;
        }
        if (!save) touchLayout = touchLayoutBeforeEdit!;
        touchLayoutBeforeEdit = null;
        layoutFinger = -1;
        selectedTouchControl = null;
        raceTouchUi!.Hide();
        raceTouchUi.Reparent(raceUi, keepGlobalTransform: false);
        foreach (var widget in touchWidgets.Values) widget.Hitbox?.Show();
        ApplyTouchLayout();
        ShowTouchMode(touchAirborne);
        touchLayoutMenu!.Hide();
        racePauseMenu.Show();
        if (focusMenu) touchLayoutButton?.GrabFocus();
    }

    private bool HandleTouchLayoutDrag(InputEvent input)
    {
        if (input is InputEventScreenTouch touch)
        {
            if (!touch.Pressed && touch.Index == layoutFinger)
            {
                layoutFinger = -1;
                return true;
            }
            if (!touch.Pressed || layoutFinger >= 0) return false;
            Vector2 point = raceTouchUi!.GetGlobalTransformWithCanvas().AffineInverse() * touch.Position;
            foreach (var (key, widget) in touchWidgets)
            {
                if (!widget.View.Visible || !TouchBounds(key, touchLayout[key]).HasPoint(point)) continue;
                selectedTouchControl = key;
                layoutFinger = touch.Index;
                layoutDragOffset = point - touchLayout[key].Position;
                touchLayoutStatus!.Text = $"{widget.Title} selected";
                ApplyTouchLayout();
                return true;
            }
        }
        if (input is InputEventScreenDrag drag && drag.Index == layoutFinger && selectedTouchControl is { } selected)
        {
            Vector2 point = raceTouchUi!.GetGlobalTransformWithCanvas().AffineInverse() * drag.Position;
            TryPlaceTouchControl(selected, touchLayout[selected] with { Position = point - layoutDragOffset });
            return true;
        }
        return false;
    }

    private void ResizeTouchControl(float step)
    {
        if (selectedTouchControl is not { } key)
        {
            touchLayoutStatus!.Text = "Tap a control to select it.";
            return;
        }
        var placement = touchLayout[key];
        float scale = Math.Clamp(placement.Scale + step, MinTouchScale, MaxTouchScale);
        Vector2 position = placement.Position + touchWidgets[key].View.Size * ((placement.Scale - scale) * .5f);
        TryPlaceTouchControl(key, new(position, scale));
    }

    private void TryPlaceTouchControl(Key key, TouchPlacement placement)
    {
        var previous = touchLayout[key];
        touchLayout[key] = ConstrainTouchPlacement(key, placement);
        if (!TouchLayoutFits(touchLayout))
        {
            touchLayout[key] = previous;
            touchLayoutStatus!.Text = "Leave space between controls and the editor.";
            return;
        }
        touchLayoutStatus!.Text = $"{touchWidgets[key].Title} selected";
        ApplyTouchLayout();
    }

    private void MirrorTouchLayout()
    {
        var mirrored = new Dictionary<Key, TouchPlacement>(touchLayout);
        foreach (var (key, placement) in touchLayout)
        {
            if (key is Key.Escape or Key.V) continue;
            mirrored[key] = placement with { Position = new(TouchCanvas.X - TouchBounds(key, placement).End.X, placement.Position.Y) };
        }
        if (!TouchLayoutFits(mirrored))
        {
            touchLayoutStatus!.Text = "Move overlapping controls before swapping sides.";
            return;
        }
        touchLayout = mirrored;
        ApplyTouchLayout();
        touchLayoutStatus!.Text = "Driving controls swapped sides.";
    }
}
