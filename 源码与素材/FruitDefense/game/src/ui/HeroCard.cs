using Godot;
using FruitDefense.Core;
using FruitDefense.Entities;
using FruitDefense.Presentation;

namespace FruitDefense.UI;

// 战斗中的英雄入口只显示形象、选中圈和恢复进度；数值不占用战场空间。
public partial class HeroCard : Button
{
    public HeroState Hero { get; set; } = null!;
    public bool Selected { get; set; }
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        foreach (string style in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            AddThemeStyleboxOverride(style, new StyleBoxEmpty());
    }
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        if (Hero == null) return;
        Vector2 center = new(Size.X / 2, 50);
        Color accent = FruitArt.Accent(Hero.Definition.Kind);
        DrawCircle(center, 43, Selected ? new Color("315445") : new Color("203d34"));
        DrawArc(center, 43, 0, MathF.Tau, 48, Selected ? accent : new Color("4d715a"), Selected ? 3 : 1.5f, true);
        FruitArt.Hero(this, center + new Vector2(0, 5), Hero.Definition.Kind, .78f);
        float skillFraction = 1 - Hero.SkillCooldownRemaining / Hero.Definition.SkillCooldown;
        DrawArc(center, 47, -MathF.PI / 2, -MathF.PI / 2 + MathF.Max(.001f, skillFraction) * MathF.Tau, 48,
            Hero.SkillReady ? new Color("f2df9a") : new Color("759a8a"), 2, true);
        DrawCircle(center + new Vector2(32, 32), 7, Hero.SkillReady ? new Color("f2df9a") : new Color("39594e"));
        if (!Hero.Ready)
        {
            DrawCircle(center, 40, new Color(0.02f, .08f, .08f, .48f));
            float fraction = 1 - Hero.CooldownRemaining / Hero.RecoveryDuration;
            DrawArc(center, 43, -MathF.PI / 2, -MathF.PI / 2 + fraction * MathF.Tau, 48, accent, 4, true);
        }
    }
}
