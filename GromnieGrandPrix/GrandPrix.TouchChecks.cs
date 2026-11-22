using Godot;

public partial class GrandPrix
{
    private void CheckTouchControls()
    {
        StartPresentationRace();
        SetPhysicsProcess(false);
        if (raceTouchUi is null) MakeRaceTouchUi();
        if (touchLayoutMenu is null) MakeTouchLayoutMenu();

        Vector2 At(Key key, Vector2 fraction)
        {
            var view = touchWidgets[key].View;
            return view.GetGlobalTransformWithCanvas() * (view.Size * fraction);
        }
        Vector2 Center(Key key) => At(key, new(.5f, .5f));
        void Touch(int finger, Vector2 position, bool down)
        {
            using var touch = new InputEventScreenTouch { Index = finger, Position = position, Pressed = down };
            GetViewport().PushInput(touch, inLocalCoords: true);
        }
        void Tap(int finger, Key key, bool down) => Touch(finger, Center(key), down);
        void Drag(int finger, Vector2 position)
        {
            using var drag = new InputEventScreenDrag { Index = finger, Position = position };
            GetViewport().PushInput(drag, inLocalCoords: true);
        }

        Tap(0, Key.None, true);
        Require(LocalSteering == 0 && !IsHeld(Key.W) && !IsHeld(Key.S), "joystick dead zone is neutral");
        Drag(0, At(Key.None, new(.78f, .15f)));
        Require(LocalSteering is > .2f and < .8f && IsHeld(Key.W) && !IsHeld(Key.S), "diagonal input steers and accelerates");
        Tap(1, Key.Space, true);
        Require(IsHeld(Key.Space) && IsHeld(Key.W) && LocalSteering > .2f, "the action thumb can drift while steering");
        Tap(1, Key.Space, false);
        Drag(0, At(Key.None, new(.3f, .92f)));
        Require(LocalSteering < -.2f && IsHeld(Key.S) && !IsHeld(Key.W), "pulling the joystick down brakes while steering");
        Tap(0, Key.None, false);
        Require(LocalSteering == 0 && !IsHeld(Key.S), "releasing the joystick returns to neutral");
        Tap(1, Key.Space, true);
        Require(IsHeld(Key.W), "holding Drift still applies throttle");
        Tap(1, Key.Space, false);

        Tap(1, Key.F, true);
        Require(useItem, "the Item button queues the equipped item");
        Tap(1, Key.F, false);
        Tap(1, Key.E, true);
        Require(pendingSwipe == 1, "the right attack queues a right swipe");
        Tap(1, Key.E, false);
        Tap(1, Key.C, true);
        Require(requestGuard, "the Guard button queues a shield block");
        Tap(1, Key.C, false);
        Touch(0, At(Key.None, new(.5f, .15f)), true);
        Tap(1, Key.Escape, true);
        Require(paused && touchPressed.Count == 0 && !IsHeld(Key.W), "pause releases the held touch controls");
        Tap(1, Key.Escape, false);
        Tap(0, Key.None, false);

        OpenTouchLayout();
        Require(EditingTouchLayout && TouchLayoutFits(touchLayout), "pause opens a non-overlapping layout");
        var original = touchLayout[Key.C];
        var start = Center(Key.C);
        Tap(0, Key.C, true);
        Drag(0, start + raceTouchUi!.GetGlobalTransformWithCanvas().BasisXform(new Vector2(-28, -106)));
        Tap(0, Key.C, false);
        Require(!requestGuard && touchLayout[Key.C].Position.DistanceTo(original.Position) > 80, "dragging moves the editor button without firing it");
        ResizeTouchControl(.1f);
        Require(touchLayout[Key.C].Scale > original.Scale, "the selected control can be resized");
        TryPlaceTouchControl(Key.C, touchLayout[Key.C] with { Position = touchLayout[Key.E].Position });
        Require(TouchLayoutFits(touchLayout) && touchLayout[Key.C].Position != touchLayout[Key.E].Position, "overlapping controls are rejected");
        CloseTouchLayout(save: false);
        Require(touchLayout[Key.C] == original, "cancel restores the saved layout");

        OpenTouchLayout();
        MirrorTouchLayout();
        Require(touchLayout[Key.None].Position.X > 900 && touchLayout[Key.Space].Position.X < 45, "swapping sides moves the joystick and actions");
        touchFlightPreview = true;
        SetTouchLayoutPreview();
        Require(touchWidgets[Key.R].View.Visible && !touchWidgets[Key.Space].View.Visible, "air roll has its own editable control");
        string layoutPath = $"user://touch-layout-check-{Guid.NewGuid():N}.cfg";
        try
        {
            Require(WriteTouchLayout(layoutPath) == Error.Ok, "the touch layout saves");
            var loaded = ReadTouchLayout(layoutPath);
            Require(touchLayout.All(pair => loaded[pair.Key] == pair.Value), "positions and sizes survive reloading");
        }
        finally { DirAccess.RemoveAbsolute(layoutPath); }
        CloseTouchLayout(save: true);
        SetPaused(false);

        raceSettings = raceSettings with { FreeFlight = true };
        Tap(1, Key.Space, true);
        Require(LaunchFlight(Player), "the touch test can launch into flight");
        UpdateRaceTouchMode();
        Require(touchWidgets[Key.R].View.Visible && !touchWidgets[Key.Space].View.Visible &&
            !touchWidgets[Key.F].View.Visible && !IsHeld(Key.W), "flight swaps the actions without carrying a held drift");
        Tap(1, Key.Space, false);
        Tap(1, Key.R, true);
        Require(requestRoll, "the airborne action queues a barrel roll");
        Tap(1, Key.R, false);
        Player.Land();
        UpdateRaceTouchMode();
        Require(touchWidgets[Key.Space].View.Visible && !touchWidgets[Key.R].View.Visible, "landing restores the ground controls");
        Require(new OnlineInput(1, 1, -1, 0, 0).SteeringAxis == -1 &&
                new OnlineInput(1, 1, -1, 0, 0, -.35f).SteeringAxis == -.35f,
            "older online inputs retain digital steering and new ones retain the analog value");
        GD.Print("TOUCH CONTROLS PASS | joystick, multitouch actions, layout editor, persistence, pause and flight.");
    }
}
