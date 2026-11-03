namespace FruitDefense.Defense;

public sealed class BarrierState(float maximumHealth)
{
    public float MaximumHealth { get; } = maximumHealth;
    public float Health { get; private set; } = maximumHealth;
    public bool Broken => Health <= 0;
    public bool EverDamaged { get; private set; }
    public void TakeDamage(float damage)
    {
        if (damage <= 0 || Broken || !float.IsFinite(damage)) return;
        EverDamaged = true;
        Health = MathF.Max(0, Health - damage);
    }
}

// 核心扣的是“入侵次数”，与敌人攻击力完全分开；集合保证每只敌人只结算一次。
public sealed class CoreState(int maximumLives)
{
    private readonly HashSet<int> _processedEnemies = [];
    public int MaximumLives { get; } = maximumLives;
    public int Lives { get; private set; } = maximumLives;
    public bool RegisterIntrusion(int enemyId)
    {
        if (Lives == 0 || !_processedEnemies.Add(enemyId)) return false;
        Lives--;
        return true;
    }
}
