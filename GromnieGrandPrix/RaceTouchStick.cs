using Godot;

internal sealed partial class RaceTouchStick : Control
{
    private static readonly Color BaseTint = new("172821dc"), EdgeTint = new("a4a66c"),
        DirectionTint = new("fff3d2c0"), KnobTint = new("9ec78e"), ActiveTint = new("c9e8ac");
    private static readonly Vector2[] Directions = [Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right];
    private Vector2 axis;
    private int finger = -1;
    private string upperLabel = "THROTTLE", lowerLabel = "BRAKE";

    public float Steering => MathF.CopySign(Math.Max(0, Math.Abs(axis.X) - .13f) / .87f, axis.X);
    public bool Throttle => axis.Y < -.22f;
    public bool Brake => axis.Y > .22f;

    public void SetVerticalLabels(bool airborne, bool freeFlight)
    {
        string upper = !airborne ? "THROTTLE" : freeFlight ? "DIVE" : "";
        string lower = !airborne ? "BRAKE" : freeFlight ? "CLIMB" : "";
        if (upperLabel == upper && lowerLabel == lower) return;
        upperLabel = upper;
        lowerLabel = lower;
        QueueRedraw();
    }

    public bool HandleTouch(InputEvent input)
    {
        var toLocal = GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 center = Size * .5f;
        float radius = center.X;
        if (input is InputEventScreenTouch touch)
        {
            if (!touch.Pressed && touch.Index == finger)
            {
                Reset();
                return true;
            }
            Vector2 offset = toLocal * touch.Position - center;
            if (!touch.Pressed || finger >= 0 || offset.LengthSquared() > radius * radius) return false;
            finger = touch.Index;
            SetAxis(offset / radius);
            return true;
        }
        if (input is InputEventScreenDrag drag && drag.Index == finger)
        {
            SetAxis((toLocal * drag.Position - center) / radius);
            return true;
        }
        return false;
    }

    private void SetAxis(Vector2 value)
    {
        axis = value.LimitLength();
        QueueRedraw();
    }

    public void Reset()
    {
        if (finger < 0) return;
        finger = -1;
        SetAxis(Vector2.Zero);
    }

    public override void _Draw()
    {
        Vector2 center = Size * .5f;
        float radius = center.X - 2;
        DrawCircle(center, radius, BaseTint, antialiased: true);
        DrawArc(center, radius, 0, Mathf.Tau, 48, EdgeTint, 2, true);
        foreach (Vector2 direction in Directions)
        {
            if (upperLabel.Length == 0 && direction.Y != 0) continue;
            Vector2 tip = center + direction * (radius - 12);
            Vector2 side = direction.Rotated(Mathf.Pi * .5f) * 7;
            DrawLine(tip - direction * 8 - side, tip, DirectionTint, 2, true);
            DrawLine(tip, tip - direction * 8 + side, DirectionTint, 2, true);
        }
        if (finger < 0 && upperLabel.Length > 0)
        {
            var font = ThemeDB.FallbackFont;
            DrawString(font, new(center.X - 43, 44), upperLabel, HorizontalAlignment.Center, 86, 13, DirectionTint);
            DrawString(font, new(center.X - 43, Size.Y - 34), lowerLabel, HorizontalAlignment.Center, 86, 13, DirectionTint);
        }
        DrawCircle(center + axis * (radius * .55f), radius * .28f, finger >= 0 ? ActiveTint : KnobTint, antialiased: true);
    }
}
