using System.Numerics;
using FruitDefense.Core;

namespace FruitDefense.Entities;

public sealed class HeroState(HeroDefinition definition)
{
    public HeroDefinition Definition { get; } = definition;
    public float CooldownRemaining { get; private set; }
    public float RecoveryDuration { get; private set; } = definition.Cooldown;
    public bool Ready => CooldownRemaining <= .0001f;
    public int ShotsFired { get; private set; }
    public float SkillCooldownRemaining { get; private set; }
    public bool SkillReady => SkillCooldownRemaining <= .0001f;
    public int SkillsUsed { get; private set; }
    public void Update(float delta)
    {
        CooldownRemaining = MathF.Max(0, CooldownRemaining - delta);
        SkillCooldownRemaining = MathF.Max(0, SkillCooldownRemaining - delta);
    }
    public void BeginSkillRecovery() { SkillCooldownRemaining = Definition.SkillCooldown; SkillsUsed++; }
    public void BeginRecovery(float speedBonus = 0)
    {
        RecoveryDuration = Definition.Cooldown / (1 + speedBonus);
        CooldownRemaining = RecoveryDuration;
        ShotsFired++;
    }
}

public sealed class Enemy(int id, Vector2 position, EnemyDefinition definition, float speedMultiplier)
{
    public const float Radius = 24;
    public int Id { get; } = id;
    public string Name { get; } = definition.Name;
    public EnemyKind Kind { get; } = definition.Kind;
    public float Armor { get; } = definition.Armor;
    public float CollisionRadius => Kind == EnemyKind.Boss ? 42 : Radius;
    public float Age { get; set; }
    public float WindupRemaining { get; set; }
    public float WindupDamage { get; set; }
    public float StaggerRemaining { get; set; }
    public bool ReinforcementsUsed { get; set; }
    public float DashTime { get; set; }
    public float BurnRemaining { get; set; }
    public float BurnTick { get; set; }
    public HeroKind BurnHero { get; set; }
    public bool DashUsed { get; set; }
    public Vector2 Position { get; set; } = position;
    public float MaxHealth { get; } = definition.Health;
    public float Health { get; private set; } = definition.Health;
    public float Speed { get; } = definition.Speed * speedMultiplier;
    public float AttackDamage { get; } = definition.AttackDamage;
    public float AttackInterval { get; } = definition.AttackInterval;
    public int Reward { get; } = definition.Reward;
    public IReadOnlyList<string?> SkillSlots { get; } = definition.SkillSlots.ToArray();
    public float AttackTimer { get; set; } = .4f;
    public bool AttackingBarrier { get; set; }
    public bool Consumed { get; set; }
    public bool Alive => Health > 0 && !Consumed;
    public float HurtFlash { get; set; }

    public void TakeDamage(float amount)
    {
        if (!Alive || amount <= 0) return;
        Health = MathF.Max(0, Health - amount);
        HurtFlash = .14f;
    }
}

public sealed class Projectile(int id, HeroDefinition definition, Vector2 position, Vector2 velocity)
{
    public bool Critical { get; set; }
    public bool ChainTriggered { get; set; }
    public bool ChainEnabled { get; set; }
    public bool BurnEnabled { get; set; }
    public int Id { get; } = id;
    public HeroDefinition Definition { get; } = definition;
    public Vector2 Position { get; set; } = position;
    public Vector2 Velocity { get; set; } = velocity;
    public float DistanceTravelled { get; set; }
    public float Age { get; set; }
    public bool Active { get; set; } = true;
    public HashSet<int> HitEnemies { get; } = [];
    public Queue<Vector2> Trail { get; } = new();
}
