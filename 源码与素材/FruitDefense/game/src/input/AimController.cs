using System.Numerics;
using FruitDefense.Core;

namespace FruitDefense.Inputs;

// 拉弓输入只负责瞄准；是否能够发射由BattleWorld检查。
public sealed class AimController
{
    public const float MaximumPull = 132;
    public bool Dragging { get; private set; }
    public Vector2 DragPoint { get; private set; } = GameRules.LaunchPoint;
    public float Pull => Vector2.Distance(DragPoint, GameRules.LaunchPoint);
    public float Power => Math.Clamp(Pull / MaximumPull, 0, 1);
    public bool Valid => Dragging && Pull >= 18;
    public Vector2 Direction => Pull < .001f ? new(0, -1) : Vector2.Normalize(GameRules.LaunchPoint - DragPoint);

    public bool Begin(Vector2 mouse)
    {
        if (Vector2.DistanceSquared(mouse, GameRules.LaunchPoint) > 105 * 105) return false;
        Dragging = true;
        Move(mouse);
        return true;
    }

    public void Move(Vector2 mouse)
    {
        if (!Dragging) return;
        Vector2 offset = mouse - GameRules.LaunchPoint;
        // 完整转向：上拉向下发射，防线被突破后仍能拦截水晶前的敌人。
        if (offset.LengthSquared() > MaximumPull * MaximumPull)
            offset = Vector2.Normalize(offset) * MaximumPull;
        DragPoint = GameRules.LaunchPoint + offset;
    }

    public void Cancel() { Dragging = false; DragPoint = GameRules.LaunchPoint; }
}
