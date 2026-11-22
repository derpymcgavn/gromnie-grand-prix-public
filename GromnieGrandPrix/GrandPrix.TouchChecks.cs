using Godot;

public partial class GrandPrix
{
    private void CheckTouchControls()
    {
        StartPresentationRace();
        SetPhysicsProcess(false);
        if (raceTouchUi is null) MakeRaceTouchUi();
        var throttle = touchGo!;
        var brake = touchBrake!;

        Panel Button(Key key) => (Panel)raceTouchUi!.FindChild($"Touch_{key}", owned: false);
        Vector2 Center(Key key)
        {
            var panel = Button(key);
            return panel.GetGlobalTransformWithCanvas() * (panel.Size * .5f);
        }
        void Tap(int finger, Key key, bool down)
        {
            using var touch = new InputEventScreenTouch { Index = finger, Position = Center(key), Pressed = down };
            GetViewport().PushInput(touch, inLocalCoords: true);
        }

        Tap(0, Key.A, true);
        Tap(1, Key.W, true);
        Require(IsHeld(Key.A) && IsHeld(Key.W), "steering and throttle work together");
        using (var drag = new InputEventScreenDrag { Index = 0, Position = Center(Key.D) })
            GetViewport().PushInput(drag, inLocalCoords: true);
        Require(!IsHeld(Key.A) && IsHeld(Key.D) && IsHeld(Key.W), "the steering thumb can slide between pads");
        Tap(1, Key.W, false);
        Tap(1, Key.Space, true);
        Require(IsHeld(Key.D) && IsHeld(Key.W) && IsHeld(Key.Space), "drift supplies throttle while steering");
        Tap(1, Key.Space, false);
        Require(IsHeld(Key.D) && !IsHeld(Key.W) && !IsHeld(Key.Space), "releasing drift leaves steering held");
        Tap(0, Key.D, false);

        Tap(1, Key.F, true);
        Require(useItem, "the Item button queues the equipped item");
        Tap(1, Key.F, false);
        Tap(0, Key.E, true);
        Require(pendingSwipe == 1, "the right attack queues a right swipe");
        Tap(0, Key.E, false);
        Tap(0, Key.C, true);
        Require(requestGuard, "the Guard button queues a shield block");
        Tap(0, Key.C, false);

        Tap(1, Key.W, true);
        Tap(0, Key.Escape, true);
        Require(paused && touchPressed.Count == 0, "pausing releases the held touch controls");
        SetPaused(false);
        Tap(0, Key.Escape, false);
        Tap(1, Key.W, false);

        raceSettings = raceSettings with { FreeFlight = true };
        Tap(1, Key.Space, true);
        Require(LaunchFlight(Player), "the touch test can launch into flight");
        UpdateRaceTouchFlightLabels();
        Require(throttle.Text == "DIVE" && brake.Text == "CLIMB" && touchAirRoll.Visible &&
                !touchDrift.Visible && !touchSwipes.Visible && !touchItem.Visible && !IsHeld(Key.Space) && !IsHeld(Key.W),
            "flight swaps pedals and actions without carrying a held drift");
        Tap(1, Key.Space, false);
        Tap(1, Key.R, true);
        Require(requestRoll, "the airborne action queues a barrel roll");
        Tap(1, Key.R, false);
        Player.Land();
        UpdateRaceTouchFlightLabels();
        Require(throttle.Text == "THROTTLE" && brake.Text == "BRAKE" &&
                touchDrift.Visible && touchSwipes.Visible && touchItem.Visible && !touchAirRoll.Visible,
            "landing restores the ground controls");
        GD.Print("TOUCH CONTROLS PASS | two-thumb steering, pedal and drift, item, attacks, pause and flight.");
    }
}
