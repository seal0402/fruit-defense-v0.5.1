using Godot;
using FruitDefense.Core;
using FruitDefense.Presentation;

namespace FruitDefense;

public partial class PreviewPortrait : Control
{
    public GameController Controller { get; set; } = null!;
    public HeroKind Hero { get; set; }
    public EnemyKind? Enemy { get; set; }
    public bool UseMemoryClock { get; set; }
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        float time = UseMemoryClock ? Controller.MemoryPosition : Controller.AnimationTime;
        Vector2 center = Size / 2 + new Vector2(0, MathF.Sin(time * 2) * 5);
        DrawCircle(center, MathF.Min(Size.X, Size.Y) * .44f, new Color("254740"));
        if (Enemy is EnemyKind kind)
        {
            FruitArt.Enemy(this, center, time, 0);
            DrawArc(center, 58, 0, MathF.Tau, 48, kind == EnemyKind.Boss ? new Color("ffad78") : new Color("c6d98a"), 3, true);
        }
        else FruitArt.Hero(this, center + new Vector2(0, 10), Hero, 2.1f, time);
    }
}
