using System.Numerics;

namespace FruitDefense.Core;

public enum HeroKind { Standard, Sniper, Shotgun }
public enum EnemyKind { Regular, Runner, Armored, Boss }
public enum DifficultyKind { Easy, Standard, Challenge }
public enum BattleState { Ready, Running, Paused, Victory, Defeat, ChoosingUpgrade }
public enum CombatEventKind { Shot, Hit, Kill, Splash, BarrierHit, BarrierBroken, CoreHit, WaveStarted, Victory, Defeat, Skill, BossWindup, BossInterrupted, BossReinforcements, RunnerTell, EnemySpawn, UpgradeDropped, UpgradeChosen, Chain }

public sealed record CombatEvent(CombatEventKind Kind, Vector2 Position, float Value = 0,
    HeroKind Hero = HeroKind.Standard, string Text = "", EnemyKind Enemy = EnemyKind.Regular);

// 普通C#配置类：伤害、射程、飞行速度和发射间隔是彼此独立的参数。
public sealed class GameRules
{
    public float BarrierHealth { get; set; } = 520;
    public int CoreLives { get; set; } = 8;
    public float CellSize { get; set; } = 105;
    public float ProjectileSpeed { get; set; } = 1100;
    public float ProjectileGravity { get; set; } = 75;
    public float WaveBreakSeconds { get; set; } = 5;
    public EnemyDefinition Enemy { get; set; } = new();
    public List<HeroDefinition> Heroes { get; set; } = [];
    public List<WaveDefinition> Waves { get; set; } = [];
    public string DifficultyName { get; set; } = "轻松";
    public string DifficultyDescription { get; set; } = "更少、更慢的敌人，适合熟悉拉弓操作。";
    public List<DifficultyDefinition> Difficulties { get; set; } = [];
    public int LevelId { get; set; } = 1;
    public string LevelName { get; set; } = "果园初战";
    public List<EnemyDefinition> EnemyTypes { get; set; } =
    [
        new() { Kind = EnemyKind.Runner, Name = "疾走小偷", Health = 40, Speed = 32, AttackDamage = 8, Reward = 120 },
        new() { Kind = EnemyKind.Armored, Name = "铁壳守卫", Health = 85, Speed = 21, AttackDamage = 16, Armor = 5, Reward = 160 },
        new() { Kind = EnemyKind.Boss, Name = "果园掠夺者", Health = 450, Speed = 17, AttackDamage = 35, AttackInterval = 2, Reward = 800 }
    ];
    public EnemyDefinition EnemyFor(EnemyKind kind) => kind == EnemyKind.Regular ? Enemy : EnemyTypes.Single(e => e.Kind == kind);

    public int ChapterId => (LevelId - 1) / 3 + 1;
    public int StageInChapter => (LevelId - 1) % 3 + 1;
    public bool ChapterFinal => StageInChapter == 3;
    public static string ChapterName(int chapter) => chapter switch { 1 => "果园防线", 2 => "铁壳军团", _ => "裂隙围城" };
    public static string StageName(int id) => id switch
    {
        1 => "果园初战", 2 => "混合入侵", 3 => "首领来袭",
        4 => "边境警报", 5 => "铁壳推进", 6 => "双首领突袭",
        7 => "裂隙前哨", 8 => "双重危机", 9 => "终局围城",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
    public static string StageButton(int id) => $"{(id - 1) % 3 + 1} · {StageName(id)}";
    public static int BossWaves(int id) => id switch { 1 or 2 => 0, 3 or 4 or 5 or 7 => 1, 6 or 8 => 2, 9 => 3, _ => 0 };

    public GameRules ForLevel(int id)
    {
        if (id is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(id));
        LevelId = id;
        LevelName = StageName(id);
        int chapter = ChapterId - 1;
        for (int i = 0; i < Waves.Count; i++)
        {
            Waves[i].EnemyKinds = id == 1 ? [EnemyKind.Regular] : i == 0 && chapter == 0
                ? [EnemyKind.Regular, EnemyKind.Runner] : [EnemyKind.Regular, EnemyKind.Runner, EnemyKind.Armored];
            Waves[i].HasBoss = i >= Waves.Count - BossWaves(id);
            Waves[i].Count += chapter;
        }
        float healthScale = 1 + .12f * chapter + (chapter > 0 ? .025f * (StageInChapter - 1) : 0);
        foreach (EnemyDefinition enemy in EnemyTypes.Prepend(Enemy))
        {
            enemy.Health *= healthScale;
            enemy.Speed *= 1 + .03f * chapter;
        }
        EnemyDefinition boss = EnemyFor(EnemyKind.Boss);
        boss.Name = ChapterId switch { 1 => "果园掠夺者", 2 => "铁甲掠夺者", _ => "裂隙统领" };
        boss.Health = 450 + 150 * chapter;
        boss.Armor = chapter == 0 ? 0 : chapter == 1 ? 2 : 3;
        boss.AttackDamage = 35 + 5 * chapter;
        return this;
    }

    public const int MaxLivingEnemies = 5;
    public const float ViewWidth = 720;
    public const float ViewHeight = 1280;
    public const float BarrierY = 1000;
    public const float SpawnY = 170;
    public static readonly Vector2 LaunchPoint = new(360, 1080);
    public static readonly Vector2 CorePoint = new(360, 1210);
    public static float BattleDistance => LaunchPoint.Y - SpawnY;

    // 难度只调整敌人、波次和防线；三位射手的攻击规则保持一致。
    public GameRules ForDifficulty(DifficultyKind kind)
    {
        DifficultyDefinition profile = Difficulties.Single(d => d.Kind == kind);
        return new GameRules
        {
            BarrierHealth = profile.BarrierHealth, CoreLives = CoreLives,
            CellSize = CellSize, ProjectileSpeed = ProjectileSpeed,
            ProjectileGravity = ProjectileGravity, WaveBreakSeconds = WaveBreakSeconds,
            DifficultyName = profile.Name, DifficultyDescription = profile.Description,
            Heroes = Heroes, Difficulties = Difficulties,
            EnemyTypes = EnemyTypes.Select(e => new EnemyDefinition
            {
                Kind = e.Kind, Name = e.Name, Health = e.Health, Speed = e.Speed * profile.EnemySpeed / Enemy.Speed,
                AttackDamage = e.AttackDamage, AttackInterval = e.AttackInterval, Armor = e.Armor,
                Reward = e.Reward, SkillSlots = [.. e.SkillSlots]
            }).ToList(),
            Enemy = new EnemyDefinition
            {
                Name = Enemy.Name, Health = Enemy.Health, Speed = profile.EnemySpeed,
                AttackDamage = Enemy.AttackDamage, AttackInterval = Enemy.AttackInterval,
                Reward = Enemy.Reward, SkillSlots = [.. Enemy.SkillSlots]
            },
            Waves = profile.Waves.Select(w => new WaveDefinition
            {
                Name = w.Name, Count = w.Count, SpawnInterval = w.SpawnInterval,
                SpeedMultiplier = w.SpeedMultiplier
            }).ToList()
        };
    }

    public void Validate()
    {
        if (BarrierHealth <= 0 || CoreLives <= 0 || CellSize <= 0 || ProjectileSpeed <= 0
            || ProjectileGravity < 0 || WaveBreakSeconds < 0)
            throw new ArgumentException("关卡基础参数必须为有效正值。");
        if (Heroes.Count != 3 || Heroes.Select(h => h.Kind).Distinct().Count() != 3)
            throw new ArgumentException("第一版需要普通、狙击、霰弹三类射手。");
        if (Heroes.Any(h => h.Damage <= 0 || h.SplashDamage < 0 || h.RangeRatio <= 0 || h.Cooldown <= 0))
            throw new ArgumentException("射手参数无效。");
        if (Enemy.Health <= 0 || Enemy.Speed <= 0 || Enemy.AttackDamage <= 0 || Enemy.AttackInterval <= 0)
            throw new ArgumentException("敌人参数无效。");
        if (EnemyTypes.Count != 3 || EnemyTypes.Select(e => e.Kind).Distinct().Count() != 3
            || EnemyTypes.Any(e => e.Kind == EnemyKind.Regular || !Enum.IsDefined(e.Kind)
                || e.Health <= 0 || e.Speed <= 0 || e.Armor < 0 || e.AttackDamage <= 0 || e.AttackInterval <= 0))
            throw new ArgumentException("需要有效的冲刺兵、护甲兵和首领定义。");
        if (Waves.Count == 0 || Waves.Any(w => w.Count <= 0 || w.SpawnInterval <= 0 || w.SpeedMultiplier <= 0))
            throw new ArgumentException("需要有效的有限波次。");
        if (Difficulties.Count != 3 || Difficulties.Select(d => d.Kind).Distinct().Count() != 3)
            throw new ArgumentException("需要轻松、标准、挑战三档难度。");
        foreach (DifficultyDefinition profile in Difficulties)
            if (profile.BarrierHealth <= 0 || profile.EnemySpeed <= 0 || profile.Waves.Count == 0
                || profile.Waves.Any(w => w.Count <= 0 || w.SpawnInterval <= 0 || w.SpeedMultiplier <= 0))
                throw new ArgumentException("难度参数无效。");
    }
}

public sealed class HeroDefinition
{
    public HeroKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public float Damage { get; set; }
    public float SplashDamage { get; set; }
    public float RangeRatio { get; set; }
    public float Cooldown { get; set; }
    public string Color { get; set; } = "FFFFFF";
    // 空槽位先保留数据位置，后续技能系统可以逐项填入技能标识。
    public List<string?> SkillSlots { get; set; } = [null, null, null];
    public float Range => RangeRatio * GameRules.BattleDistance;
    public string SkillName => Kind switch { HeroKind.Standard => "三连速射", HeroKind.Sniper => "贯穿强击", _ => "震荡重炮" };
    public float SkillCooldown => Kind switch { HeroKind.Standard => 10, HeroKind.Sniper => 14, _ => 16 };
    public float SplashRadiusMultiplier { get; set; } = 1;
    public HeroDefinition SkillAttack() => new()
    {
        Kind = Kind, Name = Name, RangeRatio = RangeRatio, Cooldown = Cooldown, Color = Color,
        Damage = Kind switch { HeroKind.Standard => 20, HeroKind.Sniper => 80, _ => 100 },
        SplashDamage = Kind == HeroKind.Shotgun ? 35 : 0,
        SplashRadiusMultiplier = Kind == HeroKind.Shotgun ? 1.5f : 1
    };
}

public sealed class EnemyDefinition
{
    public EnemyKind Kind { get; set; }
    public float Armor { get; set; }
    public string Name { get; set; } = "果园小偷";
    public float Health { get; set; } = 60;
    public float Speed { get; set; } = 26;
    public float AttackDamage { get; set; } = 12;
    public float AttackInterval { get; set; } = 1.15f;
    public int Reward { get; set; } = 100;
    public List<string?> SkillSlots { get; set; } = [null, null, null];
}

public sealed class WaveDefinition
{
    public List<EnemyKind> EnemyKinds { get; set; } = [EnemyKind.Regular];
    public bool HasBoss { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public float SpawnInterval { get; set; }
    public float SpeedMultiplier { get; set; } = 1;
}

public sealed class DifficultyDefinition
{
    public DifficultyKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public float BarrierHealth { get; set; }
    public float EnemySpeed { get; set; }
    public List<WaveDefinition> Waves { get; set; } = [];
}
