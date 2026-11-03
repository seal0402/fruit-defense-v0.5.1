using System.Numerics;
using FruitDefense.Core;
using FruitDefense.Entities;

namespace FruitDefense.Combat;

// 同一个命中入口调用不同实现，展示接口、多态与行为组合。
public interface IAttackBehavior
{
    void OnHit(BattleWorld world, Projectile projectile, Enemy enemy, Vector2 impact);
}

public sealed class StandardAttack : IAttackBehavior
{
    public void OnHit(BattleWorld world, Projectile projectile, Enemy enemy, Vector2 impact)
    {
        world.ResolveHit(projectile, enemy, projectile.Definition.Damage);
        projectile.Active = false;
    }
}

public sealed class PiercingAttack : IAttackBehavior
{
    public void OnHit(BattleWorld world, Projectile projectile, Enemy enemy, Vector2 impact)
        => world.ResolveHit(projectile, enemy, projectile.Definition.Damage);
}

public sealed class SplashAttack : IAttackBehavior
{
    public void OnHit(BattleWorld world, Projectile projectile, Enemy enemy, Vector2 impact)
    {
        world.ResolveHit(projectile, enemy, projectile.Definition.Damage);
        float radius = world.Rules.CellSize * projectile.Definition.SplashRadiusMultiplier;
        foreach (Enemy other in world.Enemies)
        {
            if (other.Id == enemy.Id || !other.Alive) continue;
            if (Vector2.DistanceSquared(other.Position, impact) <= radius * radius)
                world.DamageEnemy(other, projectile.Definition.SplashDamage * (projectile.Critical ? 2 : 1), projectile.Definition.Kind, projectile.Critical ? "暴击" : "");
        }
        world.Emit(new(CombatEventKind.Splash, impact, radius, HeroKind.Shotgun));
        projectile.Active = false;
    }
}
