using System.Numerics;
using FruitDefense.Combat;
using FruitDefense.Defense;
using FruitDefense.Entities;
using FruitDefense.Progression;

namespace FruitDefense.Core;

// 此类不依赖Godot：管理一局游戏的状态，画面只读取状态并监听事件。
public sealed class BattleWorld
{
    private readonly Dictionary<HeroKind, IAttackBehavior> _attacks = new()
    {
        [HeroKind.Standard] = new StandardAttack(),
        [HeroKind.Sniper] = new PiercingAttack(),
        [HeroKind.Shotgun] = new SplashAttack()
    };
    private int _nextEnemyId;
    private int _nextProjectileId;
    private int _spawnedInWave;
    private float _spawnTimer = .7f;
    private float _breakTimer = -1;
    private bool _bossSpawned;
    private int _reinforcementsPending;
    private readonly List<PendingShot> _pendingShots = [];
    private sealed class PendingShot(HeroDefinition attack, Vector2 velocity, float delay)
    {
        public HeroDefinition Attack = attack;
        public Vector2 Velocity = velocity;
        public float Delay = delay;
    }
    private readonly Random _criticalRandom;
    private readonly Random _upgradeRandom;
    private readonly bool _offerUpgrades;
    public UpgradeState Upgrades { get; }
    public List<UpgradeKind> OfferedUpgrades { get; } = [];
    public int Stars => State == BattleState.Victory ? StarPotential : 0;
    public int StarPotential => Core.Lives == 0 ? 0 : Barrier.Broken ? 1 : Barrier.EverDamaged ? 2 : 3;
    public GameRules Rules { get; }
    public BarrierState Barrier { get; }
    public CoreState Core { get; }
    public Dictionary<HeroKind, HeroState> Heroes { get; }
    public List<Enemy> Enemies { get; } = [];
    public List<Projectile> Projectiles { get; } = [];
    public BattleState State { get; private set; } = BattleState.Ready;
    public int Score { get; private set; }
    public int Kills { get; private set; }
    public int Intrusions { get; private set; }
    public int WaveIndex { get; private set; }
    public int TotalSpawned { get; private set; }
    public float Elapsed { get; private set; }
    public float NextWaveIn => MathF.Max(0, _breakTimer);
    public bool BetweenWaves => _breakTimer >= 0;
    public float? NextArrivalIn => State is BattleState.Running or BattleState.Paused or BattleState.ChoosingUpgrade
        ? BetweenWaves ? NextWaveIn : Rules.Waves.Count > 0 && _spawnedInWave < Rules.Waves[WaveIndex].Count
            ? MathF.Max(0, _spawnTimer) : null
        : null;
    public event Action<CombatEvent>? EventRaised;

    public BattleWorld(GameRules rules, UpgradeState? upgrades = null, bool offerUpgrades = false)
    {
        Rules = rules;
        Upgrades = upgrades?.Clone() ?? new();
        _offerUpgrades = offerUpgrades;
        _criticalRandom = new(rules.LevelId * 7919 + 17);
        _upgradeRandom = new(rules.LevelId * 3571 + 37);
        Barrier = new(rules.BarrierHealth);
        Core = new(rules.CoreLives);
        Heroes = rules.Heroes.ToDictionary(h => h.Kind, h => new HeroState(h));
    }

    public void Start()
    {
        if (State != BattleState.Ready) return;
        State = BattleState.Running;
        if (Rules.Waves.Count > 0) AnnounceWave();
    }

    public void TogglePause()
    {
        if (State == BattleState.Running) State = BattleState.Paused;
        else if (State == BattleState.Paused) State = BattleState.Running;
    }

    public bool TryShoot(HeroKind kind, Vector2 direction, float power, bool useSkill = false)
    {
        if (State != BattleState.Running || !Heroes.TryGetValue(kind, out HeroState? hero) || !hero.Ready)
            return false;
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || direction.LengthSquared() < .0001f)
            return false;
        direction = Vector2.Normalize(direction);
        if (!float.IsFinite(power)) return false;
        float speed = Rules.ProjectileSpeed * (.4f + .6f * Math.Clamp(power, 0, 1));
        bool skill = useSkill && hero.SkillReady;
        HeroDefinition source = skill ? hero.Definition.SkillAttack() : hero.Definition;
        HeroDefinition attack = new()
        {
            Kind = source.Kind, Name = source.Name, RangeRatio = source.RangeRatio, Cooldown = source.Cooldown, Color = source.Color,
            Damage = (source.Damage + Upgrades.FlatDamage) * (1 + Upgrades.DamageBonus),
            SplashDamage = source.SplashDamage > 0 ? (source.SplashDamage + Upgrades.FlatDamage) * (1 + Upgrades.DamageBonus) : 0,
            SplashRadiusMultiplier = source.SplashRadiusMultiplier
        };
        FireVolley(attack, direction * speed);
        if (skill)
        {
            hero.BeginSkillRecovery();
            if (kind == HeroKind.Standard)
            {
                _pendingShots.Add(new(attack, direction * speed, .08f));
                _pendingShots.Add(new(attack, direction * speed, .16f));
            }
            Emit(new(CombatEventKind.Skill, GameRules.LaunchPoint, Hero: kind, Text: hero.Definition.SkillName));
        }
        hero.BeginRecovery(Upgrades.AttackSpeedBonus);
        Emit(new(CombatEventKind.Shot, GameRules.LaunchPoint, power, kind));
        return true;
    }

    private void FireVolley(HeroDefinition attack, Vector2 velocity)
    {
        int count = Upgrades.AmmoCount;
        for (int i = 0; i < count; i++)
        {
            float angle = count == 1 ? 0 : (i - (count - 1) / 2f) * .024f;
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            var projectile = new Projectile(++_nextProjectileId, attack, GameRules.LaunchPoint,
                new(velocity.X * c - velocity.Y * s, velocity.X * s + velocity.Y * c))
            {
                Critical = _criticalRandom.NextDouble() < Upgrades.CriticalChance,
                ChainEnabled = Upgrades.Count(UpgradeKind.ChainLightning) > 0,
                BurnEnabled = Upgrades.Count(UpgradeKind.BurningRounds) > 0
            };
            Projectiles.Add(projectile);
        }
    }
    public bool SelectUpgrade(UpgradeKind kind)
    {
        if (State != BattleState.ChoosingUpgrade || !OfferedUpgrades.Contains(kind) || !Upgrades.Add(kind)) return false;
        OfferedUpgrades.Clear();
        State = BattleState.Running;
        Emit(new(CombatEventKind.UpgradeChosen, GameRules.LaunchPoint, Text: UpgradeState.Cards.Single(c => c.Kind == kind).Name));
        return true;
    }
    public void ResolveHit(Projectile projectile, Enemy enemy, float damage)
    {
        DamageEnemy(enemy, damage * (projectile.Critical ? 2 : 1), projectile.Definition.Kind, projectile.Critical ? "暴击" : "");
        if (projectile.BurnEnabled && enemy.Alive)
        {
            if (enemy.BurnRemaining <= 0) enemy.BurnTick = 1;
            enemy.BurnRemaining = 3;
            enemy.BurnHero = projectile.Definition.Kind;
        }
        if (!projectile.ChainEnabled || projectile.ChainTriggered) return;
        projectile.ChainTriggered = true;
        Enemy? secondary = Enemies.Where(e => e.Alive && e.Id != enemy.Id &&
                Vector2.Distance(e.Position, enemy.Position) <= Rules.CellSize * 1.5f)
            .OrderBy(e => Vector2.DistanceSquared(e.Position, enemy.Position)).FirstOrDefault();
        if (secondary == null) return;
        DamageEnemy(secondary, 18, projectile.Definition.Kind, "闪电");
        Emit(new(CombatEventKind.Chain, secondary.Position, 40, projectile.Definition.Kind));
    }

    public Enemy SpawnEnemy(Vector2 position, float multiplier = 1, EnemyKind kind = EnemyKind.Regular)
    {
        var enemy = new Enemy(++_nextEnemyId, position, Rules.EnemyFor(kind), multiplier);
        Enemies.Add(enemy);
        TotalSpawned++;
        Emit(new(CombatEventKind.EnemySpawn, position, Enemy: kind));
        return enemy;
    }

    public void Update(float delta)
    {
        if (State != BattleState.Running || delta <= 0 || !float.IsFinite(delta)) return;
        // 固定小步更新，避免窗口卡顿时跳过碰撞、同时瞬间结算大量攻击。
        float remaining = MathF.Min(delta, .25f);
        while (remaining > .00001f && State == BattleState.Running)
        {
            float step = MathF.Min(remaining, 1f / 120);
            Step(step);
            remaining -= step;
        }
    }

    private void Step(float delta)
    {
        Elapsed += delta;
        foreach (HeroState hero in Heroes.Values) hero.Update(delta);
        UpdateSpawning(delta);
        foreach (PendingShot shot in _pendingShots)
        {
            shot.Delay -= delta;
            if (shot.Delay <= 0) FireVolley(shot.Attack, shot.Velocity);
        }
        _pendingShots.RemoveAll(s => s.Delay <= 0);
        while (_reinforcementsPending > 0 && Enemies.Count(e => e.Alive) < GameRules.MaxLivingEnemies)
        {
            SpawnEnemy(new(280 + _reinforcementsPending * 80, GameRules.SpawnY));
            _reinforcementsPending--;
        }
        foreach (Enemy enemy in Enemies) UpdateEnemy(enemy, delta);

        foreach (Projectile projectile in Projectiles)
            if (projectile.Active) UpdateProjectile(projectile, delta);

        // 先处理本步弹药命中，再判断触碰核心：已被击杀的敌人不能再扣核心生命。
        foreach (Enemy enemy in Enemies)
        {
            if (!enemy.Alive || !Barrier.Broken) continue;
            float coreContactRadius = enemy.CollisionRadius + 34;
            if (Vector2.DistanceSquared(enemy.Position, GameRules.CorePoint) > coreContactRadius * coreContactRadius) continue;
            if (Core.RegisterIntrusion(enemy.Id))
            {
                Intrusions++;
                Emit(new(CombatEventKind.CoreHit, GameRules.CorePoint, 1));
            }
            enemy.Consumed = true;
        }
        Enemies.RemoveAll(e => !e.Alive);
        Projectiles.RemoveAll(p => !p.Active);

        if (Core.Lives == 0)
        {
            State = BattleState.Defeat;
            Emit(new(CombatEventKind.Defeat, GameRules.CorePoint));
            return;
        }
        CheckWaveCompletion();
    }

    private void UpdateEnemy(Enemy enemy, float delta)
    {
        if (!enemy.Alive) return;
        enemy.Age += delta;
        if (enemy.BurnRemaining > 0)
        {
            enemy.BurnRemaining = MathF.Max(0, enemy.BurnRemaining - delta);
            enemy.BurnTick -= delta;
            if (enemy.BurnTick <= .0001f)
            {
                enemy.BurnTick += 1;
                DamageEnemy(enemy, 8, enemy.BurnHero, "灼烧");
                if (!enemy.Alive) return;
            }
        }
        enemy.HurtFlash = MathF.Max(0, enemy.HurtFlash - delta);
        if (enemy.StaggerRemaining > 0) { enemy.StaggerRemaining -= delta; return; }
        float speed = enemy.Speed;
        if (enemy.Kind == EnemyKind.Runner && !enemy.DashUsed && enemy.Age >= 5)
        {
            enemy.DashUsed = true;
            enemy.DashTime = 1.4f;
            Emit(new(CombatEventKind.RunnerTell, enemy.Position, Text: "即将冲刺"));
        }
        if (enemy.DashTime > 0)
        {
            speed *= enemy.DashTime > .8f ? 0 : 1.8f;
            enemy.DashTime -= delta;
        }
        if (Barrier.Broken)
        {
            enemy.AttackingBarrier = false;
            enemy.WindupRemaining = 0;
            Vector2 offset = GameRules.CorePoint - enemy.Position;
            float distance = offset.Length();
            if (distance > .001f) enemy.Position += offset / distance * MathF.Min(distance, speed * delta);
            return;
        }
        float stopY = GameRules.BarrierY - enemy.CollisionRadius;
        enemy.AttackingBarrier = enemy.Position.Y >= stopY;
        if (!enemy.AttackingBarrier)
        {
            enemy.Position = new(enemy.Position.X, MathF.Min(stopY, enemy.Position.Y + speed * delta));
            return;
        }
        if (enemy.Kind == EnemyKind.Boss)
        {
            if (enemy.WindupRemaining > 0)
            {
                enemy.WindupRemaining -= delta;
                if (enemy.WindupRemaining > 0) return;
                HitBarrier(enemy);
                enemy.AttackTimer = enemy.AttackInterval;
                return;
            }
            enemy.AttackTimer -= delta;
            if (enemy.AttackTimer <= 0)
            {
                enemy.WindupRemaining = 2.5f;
                enemy.WindupDamage = 0;
                Emit(new(CombatEventKind.BossWindup, enemy.Position, Text: "重击蓄力"));
            }
            return;
        }
        enemy.AttackTimer -= delta;
        if (enemy.AttackTimer <= 0)
        {
            enemy.AttackTimer += enemy.AttackInterval;
            HitBarrier(enemy);
        }
    }
    private void HitBarrier(Enemy enemy)
    {
        Barrier.TakeDamage(enemy.AttackDamage);
        Emit(new(CombatEventKind.BarrierHit, new(enemy.Position.X, GameRules.BarrierY), enemy.AttackDamage));
        if (Barrier.Broken) Emit(new(CombatEventKind.BarrierBroken, new(GameRules.LaunchPoint.X, GameRules.BarrierY)));
    }

    private void UpdateProjectile(Projectile projectile, float delta)
    {
        Vector2 before = projectile.Position;
        Vector2 displacement = projectile.Velocity * delta + new Vector2(0, .5f * Rules.ProjectileGravity * delta * delta);
        float stepLength = displacement.Length();
        float remainingRange = projectile.Definition.Range - projectile.DistanceTravelled;
        if (remainingRange <= 0) { projectile.Active = false; return; }
        if (stepLength > remainingRange) displacement *= remainingRange / stepLength;
        Vector2 after = before + displacement;
        projectile.Position = after;
        projectile.Velocity += new Vector2(0, Rules.ProjectileGravity * delta);
        projectile.DistanceTravelled += displacement.Length();
        projectile.Age += delta;
        projectile.Trail.Enqueue(before);
        while (projectile.Trail.Count > 10) projectile.Trail.Dequeue();

        // 线段扫掠防止高速子弹穿过敌人却没有触发命中；按接触先后处理。
        var contacts = Enemies.Where(e => e.Alive && !projectile.HitEnemies.Contains(e.Id))
            .Select(e => (Enemy: e, T: SegmentContact(before, after, e.Position, e.CollisionRadius + 7)))
            .Where(c => c.T >= 0).OrderBy(c => c.T).ToArray();
        foreach (var contact in contacts)
        {
            if (!projectile.Active) break;
            if (!contact.Enemy.Alive) continue;
            projectile.HitEnemies.Add(contact.Enemy.Id);
            Vector2 impact = Vector2.Lerp(before, after, contact.T);
            _attacks[projectile.Definition.Kind].OnHit(this, projectile, contact.Enemy, impact);
        }
        if (projectile.DistanceTravelled >= projectile.Definition.Range - .001f || projectile.Age > 4
            || after.X < 20 || after.X > GameRules.ViewWidth - 20 || after.Y < 126 || after.Y > GameRules.ViewHeight - 26)
            projectile.Active = false;
    }

    public static float SegmentContact(Vector2 start, Vector2 end, Vector2 center, float radius)
    {
        Vector2 direction = end - start;
        Vector2 relative = start - center;
        if (relative.LengthSquared() <= radius * radius) return 0;
        float a = direction.LengthSquared();
        if (a < .000001f) return -1;
        float b = 2 * Vector2.Dot(relative, direction);
        float c = relative.LengthSquared() - radius * radius;
        float discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return -1;
        float t = (-b - MathF.Sqrt(discriminant)) / (2 * a);
        return t >= 0 && t <= 1 ? t : -1;
    }

    public void DamageEnemy(Enemy enemy, float damage, HeroKind hero, string text = "")
    {
        if (!enemy.Alive || damage <= 0) return;
        float actual = MathF.Min(enemy.Health, MathF.Max(1, damage - enemy.Armor));
        enemy.TakeDamage(actual);
        Emit(new(CombatEventKind.Hit, enemy.Position, actual, hero, text));
        if (enemy.Kind == EnemyKind.Boss && enemy.Alive)
        {
            if (enemy.WindupRemaining > 0 && (enemy.WindupDamage += actual) >= 100)
            {
                enemy.WindupRemaining = 0;
                enemy.StaggerRemaining = 1;
                enemy.AttackTimer = enemy.AttackInterval;
                Emit(new(CombatEventKind.BossInterrupted, enemy.Position, Text: "蓄力打断"));
            }
            if (!enemy.ReinforcementsUsed && enemy.Health <= enemy.MaxHealth * .5f)
            {
                enemy.ReinforcementsUsed = true;
                _reinforcementsPending += 2;
                Emit(new(CombatEventKind.BossReinforcements, enemy.Position, Text: "召唤援兵 ×2"));
            }
        }
        if (!enemy.Alive)
        {
            Score += enemy.Reward;
            Kills++;
            Emit(new(CombatEventKind.Kill, enemy.Position, enemy.Reward, hero, Enemy: enemy.Kind));
        }
    }

    private void UpdateSpawning(float delta)
    {
        if (Rules.Waves.Count == 0) return;
        if (_breakTimer >= 0)
        {
            _breakTimer -= delta;
            if (_breakTimer > 0) return;
            _breakTimer = -1;
            _spawnedInWave = 0;
            _bossSpawned = false;
            _spawnTimer = .6f;
            AnnounceWave();
        }
        WaveDefinition wave = Rules.Waves[WaveIndex];
        if (_spawnedInWave >= wave.Count)
        {
            if (wave.HasBoss && !_bossSpawned && Enemies.Count(e => e.Alive) <= 2)
            {
                SpawnEnemy(new(360, GameRules.SpawnY), kind: EnemyKind.Boss);
                _bossSpawned = true;
            }
            return;
        }
        if (Enemies.Count(e => e.Alive) >= GameRules.MaxLivingEnemies) return;
        _spawnTimer -= delta;
        if (_spawnTimer > 0) return;
        // 相邻列与前后错位便于玩家利用贯穿与近程溅射。
        float[] lanes = [120, 200, 280, 360, 440, 520, 600];
        int lane = (_spawnedInWave * 3 + WaveIndex * 2) % lanes.Length;
        SpawnEnemy(new(lanes[lane], GameRules.SpawnY), wave.SpeedMultiplier, wave.EnemyKinds[_spawnedInWave % wave.EnemyKinds.Count]);
        _spawnedInWave++;
        _spawnTimer += wave.SpawnInterval;
    }

    private void CheckWaveCompletion()
    {
        if (Rules.Waves.Count == 0 || _breakTimer >= 0 || _spawnedInWave < Rules.Waves[WaveIndex].Count || Enemies.Count != 0 || _reinforcementsPending > 0
            || Rules.Waves[WaveIndex].HasBoss && !_bossSpawned) return;
        if (WaveIndex == Rules.Waves.Count - 1)
        {
            State = BattleState.Victory;
            Emit(new(CombatEventKind.Victory, GameRules.CorePoint));
        }
        else
        {
            WaveIndex++;
            _breakTimer = Rules.WaveBreakSeconds;
            // 清波自动拾取掉落，选择期间冻结战斗；弹药清空防止旧弹带入下一波。
            Projectiles.Clear();
            _pendingShots.Clear();
            if (_offerUpgrades)
            {
                OfferedUpgrades.AddRange(UpgradeState.Cards.Where(c => Upgrades.CanAdd(c.Kind))
                    .OrderBy(_ => _upgradeRandom.Next()).Take(3).Select(c => c.Kind));
                State = BattleState.ChoosingUpgrade;
                Emit(new(CombatEventKind.UpgradeDropped, new(360, 700), Text: "本波掉落增益卡"));
            }
        }
    }

    private void AnnounceWave() => Emit(new(CombatEventKind.WaveStarted, new(GameRules.LaunchPoint.X, 250), WaveIndex + 1,
        Text: Rules.Waves[WaveIndex].Name));
    public void Emit(CombatEvent combatEvent) => EventRaised?.Invoke(combatEvent);
}
