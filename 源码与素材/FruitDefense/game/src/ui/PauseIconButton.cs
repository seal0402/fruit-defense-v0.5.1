using Godot;

namespace FruitDefense.UI;

public partial class PauseIconButton : Button
{
    public override void _Ready()
    {
        foreach (string name in new[] { "normal", "hover", "pressed", "focus" })
            AddThemeStyleboxOverride(name, new StyleBoxEmpty());
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }
    public override void _Draw()
    {
        Vector2 center = Size / 2;
        DrawCircle(center, 30, IsHovered() ? new Color("365b4d") : new Color("25453d"));
        DrawArc(center, 29, 0, MathF.Tau, 40, new Color("72977c"), 1.5f, true);
        DrawRect(new Rect2(center.X - 11, center.Y - 12, 7, 24), new Color("e6eacd"));
        DrawRect(new Rect2(center.X + 4, center.Y - 12, 7, 24), new Color("e6eacd"));
    }
}
