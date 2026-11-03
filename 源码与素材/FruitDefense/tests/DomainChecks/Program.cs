using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FruitDefense.Core;
using FruitDefense.Defense;
using FruitDefense.Entities;
using FruitDefense.Inputs;
using FruitDefense.Progression;

string configPath = args[0];
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
jsonOptions.Converters.Add(new JsonStringEnumConverter());
GameRules Load() => JsonSerializer.Deserialize<GameRules>(File.ReadAllText(configPath), jsonOptions)!;
GameRules Sandbox()
{
    GameRules rules = Load();
    rules.Waves.Clear();
    rules.Enemy.Speed = .001f;
    rules.Enemy.Health = 60;
    rules.Enemy.AttackDamage = 16;
    rules.Enemy.AttackInterval = .95f;
    rules.EnemyFor(EnemyKind.Armored).Health = 100;
    rules.EnemyFor(EnemyKind.Boss).Health = 600;
    rules.EnemyFor(EnemyKind.Boss).AttackDamage = 45;
    rules.ProjectileGravity = 0;
    return rules;
}
BattleWorld Running(GameRules? rules = null) { var world = new BattleWorld(rules ?? Sandbox()); world.Start(); return world; }
void Tick(BattleWorld world, float seconds) { for (int i = 0; i < seconds * 120; i++) world.Update(1f / 120); }
void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); }
void Near(float actual, float expected, string detail) => Check(MathF.Abs(actual - expected) < .05f, $"{detail}: expected {expected}, got {actual}");
List<object> results = [];
int failures = 0;
void Test(string name, Action action)
{
    try { action(); results.Add(new { name, passed = true }); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.Message }); Console.WriteLine($"FAIL {name}: {e.Message}"); }
}

Test("配置完整且三类攻击有不同射程和恢复间隔", () =>
{
    GameRules r = Load(); r.Validate();
    Check(r.Heroes[1].Range > r.Heroes[0].Range && r.Heroes[0].Range > r.Heroes[2].Range, "射程顺序");
    Check(r.Heroes[0].Cooldown < r.Heroes[1].Cooldown && r.Heroes[0].Cooldown < r.Heroes[2].Cooldown, "普通发射更快");
});
Test("三档难度从轻松到挑战逐级增加，默认采用轻松", () =>
{
    GameRules config = Load();
    GameRules[] profiles = Enum.GetValues<DifficultyKind>().Select(config.ForDifficulty).ToArray();
    foreach (GameRules profile in profiles) profile.Validate();
    Check(profiles.Select(p => p.Waves.Sum(w => w.Count)).SequenceEqual(new[] { 12, 15, 19 }), "各难度数量不正确");
    Check(profiles[0].Enemy.Speed < profiles[1].Enemy.Speed && profiles[1].Enemy.Speed < profiles[2].Enemy.Speed, "速度未递增");
    Check(config.Waves.Sum(w => w.Count) == 12 && config.Enemy.Speed == profiles[0].Enemy.Speed, "默认不是轻松档");
    Check(profiles[0].Waves.All(w => w.SpawnInterval >= 2.2f) && profiles[0].Heroes[0].Cooldown == .22f, "轻松档节奏不正确");
});
Test("快射在0.22秒恢复，不能提前发射", () =>
{
    BattleWorld w = Running(); w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1);
    Tick(w, .20f); Check(!w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1), "提前绕过冷却");
    Tick(w, .03f); Check(w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1), "0.22秒后仍不能快射");
});
Test("独立恢复且冷却不能连续绕过", () =>
{
    BattleWorld w = Running();
    Check(w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1), "普通首次发射");
    Check(!w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1), "冷却期间应拒绝");
    Check(w.TryShoot(HeroKind.Sniper, -Vector2.UnitY, 1), "其他射手可发射");
    Tick(w, .51f);
    Check(w.Heroes[HeroKind.Standard].Ready && !w.Heroes[HeroKind.Sniper].Ready, "独立恢复");
});
Test("暂停冻结时间、弹药和冷却", () =>
{
    BattleWorld w = Running(); w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1);
    Vector2 position = w.Projectiles[0].Position;
    float cooldown = w.Heroes[HeroKind.Standard].CooldownRemaining;
    w.TogglePause(); Tick(w, 2);
    Check(w.Projectiles[0].Position == position, "暂停时弹药移动了");
    Near(w.Heroes[HeroKind.Standard].CooldownRemaining, cooldown, "暂停时冷却改变");
    Check(!w.TryShoot(HeroKind.Sniper, -Vector2.UnitY, 1), "暂停时不应发射");
    w.TogglePause(); Tick(w, .1f); Check(w.Elapsed > 0, "继续后未推进");
});
Test("普通弹药命中最近敌人后结束", () =>
{
    BattleWorld w = Running();
    Enemy first = w.SpawnEnemy(new(360, 944)); Enemy second = w.SpawnEnemy(new(360, 864));
    w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1); Tick(w, .5f);
    Near(first.Health, 40, "首个目标伤害"); Near(second.Health, 60, "普通弹不应贯穿");
    Check(w.Projectiles.Count == 0, "命中后未结束");
});
Test("狙击整段贯穿且重复重叠只命中一次", () =>
{
    GameRules r = Sandbox(); r.Enemy.Health = 200; r.ProjectileSpeed = 150;
    BattleWorld w = Running(r);
    Enemy first = w.SpawnEnemy(new(360, 944)); Enemy second = w.SpawnEnemy(new(360, 844));
    int hits = 0; w.EventRaised += e => { if (e.Kind == CombatEventKind.Hit) hits++; };
    w.TryShoot(HeroKind.Sniper, -Vector2.UnitY, 1); Tick(w, 2.5f);
    Near(first.Health, 150, "贯穿首个敌人"); Near(second.Health, 150, "贯穿第二个敌人");
    Check(hits == 2, $"重复伤害，命中事件数为{hits}");
});
Test("霰弹直击、范围溅射和范围外目标分别正确", () =>
{
    GameRules r = Sandbox(); r.Enemy.Health = 200;
    BattleWorld w = Running(r);
    Enemy direct = w.SpawnEnemy(new(360, 944));
    Enemy near = w.SpawnEnemy(new(420, 944)); Enemy far = w.SpawnEnemy(new(530, 944));
    w.TryShoot(HeroKind.Shotgun, -Vector2.UnitY, 1); Tick(w, .4f);
    Near(direct.Health, 130, "直击不能重复结算");
    Near(near.Health, 175, "溅射伤害"); Near(far.Health, 200, "范围外应无伤害");
});
Test("短程弹药到达射程后结束且不能伤害远方目标", () =>
{
    BattleWorld w = Running(); Enemy far = w.SpawnEnemy(new(360, 724));
    w.TryShoot(HeroKind.Shotgun, -Vector2.UnitY, 1); Tick(w, 1);
    Near(far.Health, 60, "超出射程仍被击中"); Check(w.Projectiles.Count == 0, "超程弹药残留");
});
Test("高速弹药使用扫掠检测而不漏过敌人", () =>
{
    GameRules r = Sandbox(); r.ProjectileSpeed = 100000;
    BattleWorld w = Running(r); Enemy target = w.SpawnEnemy(new(360, 844));
    w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1); w.Update(1f / 120);
    Near(target.Health, 40, "高速碰撞未命中");
});
Test("核心每个敌人仅扣一次且固定扣一点", () =>
{
    var core = new CoreState(3);
    Check(core.RegisterIntrusion(42), "首次入侵"); Check(!core.RegisterIntrusion(42), "重复入侵");
    Check(core.Lives == 2, "相同敌人重复扣血"); core.RegisterIntrusion(43); Check(core.Lives == 1, "每只应扣一点");
});
Test("隔离网承受攻击力，核心生命保持独立", () =>
{
    GameRules r = Sandbox(); r.BarrierHealth = 100;
    BattleWorld w = Running(r); w.SpawnEnemy(new(360, GameRules.BarrierY - Enemy.Radius));
    Tick(w, .6f); Near(w.Barrier.Health, 84, "隔离网攻击结算");
    Check(w.Core.Lives == r.CoreLives, "隔离网受击误扣核心生命");
});
Test("越过破网不会提前扣血，触碰水晶后一次性处理", () =>
{
    GameRules r = Sandbox(); r.Enemy.Speed = 120; r.Enemy.AttackDamage = 999;
    BattleWorld w = Running(r); w.Barrier.TakeDamage(r.BarrierHealth);
    w.SpawnEnemy(new(360, GameRules.BarrierY + 10)); Tick(w, .3f);
    Check(w.Core.Lives == r.CoreLives, "刚突破就提前扣血");
    Tick(w, 3); Check(w.Core.Lives == r.CoreLives - 1, "核心不是固定一次一点");
    Tick(w, 2); Check(w.Core.Lives == r.CoreLives - 1 && w.Enemies.Count == 0, "入侵敌人残留或重复伤害");
});
Test("破网后向下射击可击杀敌人并保住核心", () =>
{
    GameRules r = Sandbox(); r.Enemy.Speed = 32;
    BattleWorld w = Running(r); w.Barrier.TakeDamage(r.BarrierHealth);
    Enemy enemy = w.SpawnEnemy(new(360, 1140));
    Check(w.TryShoot(HeroKind.Shotgun, Vector2.UnitY, .85f), "向下发射仍被限制");
    Tick(w, .5f);
    Check(!enemy.Alive && w.Kills == 1 && w.Core.Lives == r.CoreLives, "下方拦截没有保护核心");
});
Test("击杀奖励一次且死亡敌人不能入侵", () =>
{
    BattleWorld w = Running(); w.Barrier.TakeDamage(9999);
    Enemy e = w.SpawnEnemy(GameRules.CorePoint);
    w.DamageEnemy(e, 999, HeroKind.Standard); w.DamageEnemy(e, 999, HeroKind.Standard); w.Update(.1f);
    Check(w.Score == w.Rules.Enemy.Reward && w.Kills == 1, "重复奖励");
    Check(w.Core.Lives == w.Rules.CoreLives, "死亡敌人扣核心");
});
Test("最后一波处理完毕并保有核心时胜利", () =>
{
    GameRules r = Sandbox(); r.Waves.Add(new() { Name = "测试", Count = 2, SpawnInterval = .1f });
    BattleWorld w = Running(r); Tick(w, 1);
    Check(w.Enemies.Count == 2, "波次未正确生成");
    foreach (Enemy e in w.Enemies.ToArray()) w.DamageEnemy(e, 999, HeroKind.Standard);
    w.Update(.1f); Check(w.State == BattleState.Victory, "未结算胜利");
});
Test("最后敌人耗尽核心时优先判负", () =>
{
    GameRules r = Sandbox(); r.CoreLives = 1; r.Waves.Add(new() { Count = 1, SpawnInterval = 1 });
    BattleWorld w = Running(r); Tick(w, .8f); w.Barrier.TakeDamage(9999);
    w.Enemies[0].Position = GameRules.CorePoint; w.Update(.1f);
    Check(w.State == BattleState.Defeat, "失败被胜利覆盖");
});
Test("拉弓可完整转向、穿过原点，保留长度限制和取消", () =>
{
    var aim = new AimController(); Check(!aim.Begin(Vector2.Zero), "战场任意点击都开始拉弓");
    Check(aim.Begin(GameRules.LaunchPoint), "发射口无法拉弓");
    aim.Move(GameRules.LaunchPoint + new Vector2(9999, 9999));
    Check(aim.Pull <= AimController.MaximumPull + .01f && aim.Direction.Y < 0, "拉弓限制无效");
    aim.Move(GameRules.LaunchPoint - new Vector2(0, 110));
    Check(aim.Direction.Y > .99f && aim.Valid, "向上拉弓不能向下发射");
    aim.Move(GameRules.LaunchPoint + new Vector2(110, 0));
    Check(aim.Direction.X < -.99f && MathF.Abs(aim.Direction.Y) < .01f, "水平瞄准仍被限制");
    aim.Move(GameRules.LaunchPoint); Check(!aim.Valid && aim.Pull < .001f, "拉回原点不应强制偏移");
    aim.Cancel(); Check(!aim.Dragging && aim.DragPoint == GameRules.LaunchPoint, "取消后状态残留");
});
Test("重开使用完整新状态", () =>
{
    GameRules r = Sandbox(); BattleWorld old = Running(r);
    old.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1); old.Barrier.TakeDamage(100);
    old.Core.RegisterIntrusion(10); old.SpawnEnemy(new(360, 844));
    var fresh = new BattleWorld(r);
    Check(fresh.State == BattleState.Ready && fresh.Projectiles.Count == 0 && fresh.Enemies.Count == 0, "对象未清空");
    Check(fresh.Heroes.Values.All(h => h.Ready) && fresh.Core.Lives == r.CoreLives && fresh.Score == 0, "数值未重置");
});
Test("三档难度均可用正常攻击完成三波", () =>
{
    foreach (DifficultyKind difficulty in Enum.GetValues<DifficultyKind>())
    {
    BattleWorld w = Running(Load().ForDifficulty(difficulty));
    for (int frame = 0; frame < 120 * 150 && w.State == BattleState.Running; frame++)
    {
        foreach (HeroKind kind in new[] { HeroKind.Shotgun, HeroKind.Standard, HeroKind.Sniper })
        {
            HeroState hero = w.Heroes[kind]; if (!hero.Ready) continue;
            Enemy? target = w.Enemies.Where(e => e.Alive && Vector2.Distance(e.Position, GameRules.LaunchPoint) < hero.Definition.Range - 12)
                .OrderByDescending(e => e.Position.Y).FirstOrDefault();
            if (target == null) continue;
            float flightTime = Vector2.Distance(target.Position, GameRules.LaunchPoint) / w.Rules.ProjectileSpeed;
            Vector2 prediction = target.Position;
            if (!target.AttackingBarrier) prediction.Y += target.Speed * flightTime;
            prediction.Y -= .5f * w.Rules.ProjectileGravity * flightTime * flightTime;
            w.TryShoot(kind, prediction - GameRules.LaunchPoint, 1);
        }
        w.Update(1f / 120);
    }
    Check(w.State == BattleState.Victory, $"默认关卡未完成：{w.State}, kills={w.Kills}, core={w.Core.Lives}");
    Console.WriteLine($"  PLAYTHROUGH difficulty={difficulty}, score={w.Score}, kills={w.Kills}, core={w.Core.Lives}, seconds={w.Elapsed:0.0}");
    }
});
Test("不防守时能正常破网和失败", () =>
{
    BattleWorld w = Running(Load()); Tick(w, 240);
    Check(w.State == BattleState.Defeat && w.Barrier.Broken && w.Core.Lives == 0, "失败流程不完整");
});

Test("快射技能三发顺序结算，暂停冻结连发和技能冷却", () =>
{
    GameRules r = Sandbox(); r.Enemy.Health = 300;
    BattleWorld w = Running(r); Enemy e = w.SpawnEnemy(new(360, 944));
    w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1, true);
    w.TogglePause(); Tick(w, 1);
    Check(w.Projectiles.Count == 1 && w.Heroes[HeroKind.Standard].SkillCooldownRemaining == 10, "暂停仍在连发");
    w.TogglePause(); Tick(w, .5f);
    Near(e.Health, 240, "三连发总伤害");
    Check(w.Heroes[HeroKind.Standard].SkillsUsed == 1, "技能次数重复");
});
Test("技能冷却时回退普通攻击，其他英雄技能独立", () =>
{
    BattleWorld w = Running();
    w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1, true); Tick(w, .3f);
    Check(w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1, true), "不能回退普通攻击");
    Check(w.Heroes[HeroKind.Standard].SkillsUsed == 1 && w.Heroes[HeroKind.Sniper].SkillReady, "技能冷却共享或绕过");
    Tick(w, 10); Check(w.Heroes[HeroKind.Standard].SkillReady, "技能未恢复");
});
Test("狙击技能贯穿每只敌人一次且伤害80", () =>
{
    GameRules r = Sandbox(); r.Enemy.Health = 300;
    BattleWorld w = Running(r);
    Enemy a = w.SpawnEnemy(new(360, 944)), b = w.SpawnEnemy(new(360, 780));
    w.TryShoot(HeroKind.Sniper, -Vector2.UnitY, 1, true); Tick(w, 2);
    Near(a.Health, 220, "首个贯穿"); Near(b.Health, 220, "后续贯穿");
});
Test("重炮技能直击100、溅射35、范围扩大且不重复伤害", () =>
{
    GameRules r = Sandbox(); r.Enemy.Health = 300;
    BattleWorld w = Running(r);
    Enemy a = w.SpawnEnemy(new(360, 944)), b = w.SpawnEnemy(new(485, 944)), c = w.SpawnEnemy(new(560, 944));
    w.TryShoot(HeroKind.Shotgun, -Vector2.UnitY, 1, true); Tick(w, .4f);
    Near(a.Health, 200, "直击"); Near(b.Health, 265, "扩大溅射"); Near(c.Health, 300, "范围外");
});
Test("护甲减免每次5点且最低1点", () =>
{
    BattleWorld w = Running();
    Enemy e = w.SpawnEnemy(new(360, 600), kind: EnemyKind.Armored);
    w.DamageEnemy(e, 20, HeroKind.Standard); Near(e.Health, 85, "护甲减免");
    w.DamageEnemy(e, 1, HeroKind.Standard); Near(e.Health, 84, "最低伤害");
});
Test("冲刺兵先停顿预警再冲刺且只触发一次", () =>
{
    BattleWorld w = Running(); Enemy e = w.SpawnEnemy(new(360, 170), kind: EnemyKind.Runner);
    int tells = 0; w.EventRaised += ev => { if (ev.Kind == CombatEventKind.RunnerTell) tells++; };
    Tick(w, 5.1f); float y = e.Position.Y; Tick(w, .25f);
    Near(e.Position.Y, y, "预警没有停顿"); Tick(w, 10);
    Check(tells == 1 && e.DashUsed && e.DashTime <= 0, "重复冲刺");
});
Test("BOSS重击预警可被累计100伤害打断并硬直", () =>
{
    BattleWorld w = Running(); Enemy boss = w.SpawnEnemy(new(360, 958), kind: EnemyKind.Boss);
    Tick(w, .6f); Check(boss.WindupRemaining > 0, "没有预警");
    float wall = w.Barrier.Health;
    w.DamageEnemy(boss, 80, HeroKind.Sniper); Check(boss.WindupRemaining > 0, "过早打断");
    w.DamageEnemy(boss, 20, HeroKind.Standard); Check(boss.WindupRemaining == 0 && boss.StaggerRemaining > 0, "未打断");
    Tick(w, 1); Near(w.Barrier.Health, wall, "硬直期间仍重击");
});
Test("BOSS蓄力未打断造成45防线伤害，暂停冻结蓄力", () =>
{
    BattleWorld w = Running(); Enemy boss = w.SpawnEnemy(new(360, 958), kind: EnemyKind.Boss);
    Tick(w, .6f); float charge = boss.WindupRemaining; w.TogglePause(); Tick(w, 3);
    Near(boss.WindupRemaining, charge, "暂停蓄力继续"); w.TogglePause(); Tick(w, 2.5f);
    Near(w.Barrier.Health, w.Rules.BarrierHealth - 45, "重击伤害");
});
Test("BOSS半血只召唤两兵，触碰核心仍只扣1", () =>
{
    BattleWorld w = Running(); Enemy boss = w.SpawnEnemy(new(360, 500), kind: EnemyKind.Boss);
    w.DamageEnemy(boss, 310, HeroKind.Sniper); w.Update(.02f);
    Check(w.TotalSpawned == 3, "援兵不是两只");
    w.DamageEnemy(boss, 20, HeroKind.Standard); w.Update(.02f); Check(w.TotalSpawned == 3, "重复召唤");
    w.Barrier.TakeDamage(9999); boss.Position = GameRules.CorePoint; w.Update(.02f);
    Check(w.Core.Lives == 7 && !boss.Alive, "首领核心伤害非固定1点");
});
Test("出怪上限5且BOSS与援兵处理完才胜利", () =>
{
    GameRules r = Load().ForDifficulty(DifficultyKind.Challenge).ForLevel(3);
    BattleWorld w = Running(r); Tick(w, 15);
    Check(w.Enemies.Count == GameRules.MaxLivingEnemies, "出怪上限错误");
    bool sawBoss = false, sawAdds = false;
    for (int step = 0; step < 20000 && w.State == BattleState.Running; step++)
    {
        w.Update(.05f);
        foreach (Enemy e in w.Enemies.ToArray())
        {
            if (e.Kind == EnemyKind.Boss && !e.ReinforcementsUsed) { sawBoss = true; w.DamageEnemy(e, 310, HeroKind.Sniper); }
            else w.DamageEnemy(e, 9999, HeroKind.Standard);
        }
        if (w.TotalSpawned >= r.Waves.Sum(wave => wave.Count) + 3) sawAdds = true;
    }
    Check(sawBoss && sawAdds && w.TotalSpawned == r.Waves.Sum(wave => wave.Count) + 3 && w.State == BattleState.Victory, "首领/援兵结算错误");
});
Test("三关三档均能以实际弹药完成，含首领且不超过5敌人", () =>
{
    foreach (int stage in new[] { 1, 2, 3 })
    foreach (DifficultyKind difficulty in Enum.GetValues<DifficultyKind>())
    {
        BattleWorld w = Running(Load().ForDifficulty(difficulty).ForLevel(stage));
        int peak = 0, bosses = 0;
        w.EventRaised += ev => { if (ev.Kind == CombatEventKind.EnemySpawn && ev.Enemy == EnemyKind.Boss) bosses++; };
        for (int step = 0; step < 24000 && w.State == BattleState.Running; step++)
        {
            foreach (HeroKind kind in new[] { HeroKind.Shotgun, HeroKind.Standard, HeroKind.Sniper })
            {
                HeroState hero = w.Heroes[kind]; if (!hero.Ready) continue;
                Enemy? target = w.Enemies.Where(e => e.Alive && Vector2.Distance(e.Position, GameRules.LaunchPoint) < hero.Definition.Range - 12)
                    .OrderByDescending(e => e.WindupRemaining > 0).ThenByDescending(e => e.Position.Y).FirstOrDefault();
                if (target == null) continue;
                float flight = Vector2.Distance(target.Position, GameRules.LaunchPoint) / w.Rules.ProjectileSpeed;
                Vector2 p = target.Position;
                if (!target.AttackingBarrier && target.DashTime <= 0) p.Y += target.Speed * flight;
                p.Y -= .5f * w.Rules.ProjectileGravity * flight * flight;
                w.TryShoot(kind, p - GameRules.LaunchPoint, 1, hero.SkillReady);
            }
            w.Update(1f / 120); peak = Math.Max(peak, w.Enemies.Count);
        }
        Check(w.State == BattleState.Victory && peak <= GameRules.MaxLivingEnemies && bosses == (stage == 3 ? 1 : 0),
            $"第{stage}关/{difficulty}: {w.State}, core={w.Core.Lives}, peak={peak}");
        Console.WriteLine($"  CAMPAIGN level={stage}; difficulty={difficulty}; kills={w.Kills}; core={w.Core.Lives}; seconds={w.Elapsed:0.0}");
    }
});
Test("本地存档往返、用户名验证与损坏备份恢复", () =>
{
    string directory = Path.Combine(Path.GetDirectoryName(args[1])!, "save-test-" + Guid.NewGuid().ToString("N"));
    string path = Path.Combine(directory, "player.json");
    var store = new ProfileStore(path); PlayerProfile profile = store.Load();
    Check(!profile.SetUsername("   ") && !profile.SetUsername("a\nb") && !profile.SetUsername(new string('a', 13)), "非法用户名通过");
    Check(profile.SetUsername(" 新玩家 "), "合法用户名拒绝");
    profile.DisplayHero = HeroKind.Sniper; profile.SelectedLevel = 3; profile.SoundEnabled = false;
    profile.SeenEnemies.Add(EnemyKind.Boss);
    Check(store.Save(profile), "存档失败");
    PlayerProfile restored = store.Load();
    Check(restored.Username == "新玩家" && restored.DisplayHero == HeroKind.Sniper && restored.SelectedLevel == 3
        && !restored.SoundEnabled && restored.SeenEnemies.Contains(EnemyKind.Boss), "数据未保存");
    File.WriteAllText(path, "{broken");
    Check(store.Load().Username == "果园守护者" && Directory.GetFiles(directory, "*.damaged-*").Length == 1, "坏档未备份恢复");
});
Test("成绩按关卡与难度分别记录并保留最高星级", () =>
{
    GameRules r = Sandbox(); r.Waves.Add(new() { Count = 1, SpawnInterval = 1 });
    BattleWorld w = Running(r); Tick(w, .8f); w.DamageEnemy(w.Enemies[0], 999, HeroKind.Standard); w.Update(.02f);
    PlayerProfile profile = new(); profile.Record(w, DifficultyKind.Easy);
    Check(profile.Stages["1:Easy"].Stars == 3 && profile.TotalKills == 1 && profile.TotalBattles == 1, "满血成绩");
    w.Core.RegisterIntrusion(-1); profile.Record(w, DifficultyKind.Easy);
    Check(profile.Stages["1:Easy"].Stars == 3, "低星覆盖高星");
    profile.Record(w, DifficultyKind.Standard); Check(profile.Stages.Count == 2, "不同难度混合记录");
});

Test("评分严格区分未伤网、伤网、破网和失败", () =>
{
    foreach ((float damage, int stars) in new[] { (0f, 3), (1f, 2), (float.MaxValue, 1) })
    {
        GameRules r = Sandbox(); r.Waves.Add(new() { Count = 1, SpawnInterval = 1 });
        BattleWorld w = Running(r); Tick(w, .8f); w.Barrier.TakeDamage(damage);
        w.DamageEnemy(w.Enemies[0], 9999, HeroKind.Standard); w.Update(.02f);
        Check(w.State == BattleState.Victory && w.Stars == stars && w.Core.Lives == 8, $"防线伤害{damage}星级错误");
    }
    GameRules failure = Sandbox(); failure.CoreLives = 1;
    BattleWorld lost = Running(failure); lost.Barrier.TakeDamage(9999); lost.SpawnEnemy(GameRules.CorePoint); lost.Update(.02f);
    Check(lost.State == BattleState.Defeat && lost.Stars == 0, "失败仍授星");
    PlayerProfile profile = new(); profile.Record(lost, DifficultyKind.Easy);
    Check(profile.Stages["1:Easy"].Stars == 0 && profile.Stages["1:Easy"].Clears == 0, "失败计入通关");
});
Test("九关分三章且后期首领波次逐步增加", () =>
{
    int[] counts = [0, 0, 1, 1, 1, 2, 1, 2, 3];
    for (int id = 1; id <= 9; id++)
    {
        GameRules r = Load().ForDifficulty(DifficultyKind.Easy).ForLevel(id);
        Check(r.ChapterId == (id - 1) / 3 + 1 && r.StageInChapter == (id - 1) % 3 + 1
            && r.Waves.Count(w => w.HasBoss) == counts[id - 1], "章节/首领配置错误");
        r.Validate();
    }
});
Test("攻速加法叠加且发射间隔按频率计算", () =>
{
    UpgradeState buffs = new(); buffs.Add(UpgradeKind.Speed10); buffs.Add(UpgradeKind.Speed20);
    Near(buffs.AttackSpeedBonus, .3f, "攻速叠加");
    BattleWorld w = new(Sandbox(), buffs); w.Start(); w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1);
    Near(w.Heroes[HeroKind.Standard].CooldownRemaining, .22f / 1.3f, "攻速不是间隔缩减");
    Tick(w, .18f); Check(w.Heroes[HeroKind.Standard].Ready, "攻速没有生效");
    Near(w.Rules.Heroes[0].Cooldown, .22f, "共享配置被污染");
});
Test("双倍弹药实际发射2、4、8枚且技能连发同样翻倍", () =>
{
    UpgradeState buffs = new();
    for (int i = 1; i <= 3; i++)
    {
        Check(buffs.Add(UpgradeKind.DoubleAmmo), "双倍卡被拒绝");
        BattleWorld w = new(Sandbox(), buffs); w.Start(); w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1);
        Check(w.Projectiles.Count == 1 << i, "只改显示没有发射多枚弹药");
    }
    Check(!buffs.Add(UpgradeKind.DoubleAmmo) && buffs.AmmoCount == 8, "弹药上限未生效");
    BattleWorld skill = new(Sandbox(), buffs); skill.Start(); skill.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1, true); Tick(skill, .18f);
    Check(skill.Projectiles.Count == 24, "三连技能没有扩展到24枚");
});
Test("额外伤害与百分比组合生效且原始配置不变", () =>
{
    UpgradeState buffs = new(); buffs.Add(UpgradeKind.FlatDamage5); buffs.Add(UpgradeKind.Damage20); buffs.Add(UpgradeKind.Damage20);
    GameRules r = Sandbox(); r.Enemy.Health = 300; BattleWorld w = new(r, buffs); w.Start();
    Enemy e = w.SpawnEnemy(new(360, 944)); w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1); Tick(w, .4f);
    Near(e.Health, 265, "应造成(20+5)*1.4=35"); Near(r.Heroes[0].Damage, 20, "基础伤害被写坏");
});
Test("暴击率叠加到100%并作用于直击和溅射", () =>
{
    UpgradeState buffs = new(); for (int i = 0; i < 10; i++) buffs.Add(UpgradeKind.Critical10);
    Check(!buffs.Add(UpgradeKind.Critical10), "暴击率超过上限");
    GameRules r = Sandbox(); r.Enemy.Health = 300; BattleWorld w = new(r, buffs); w.Start();
    Enemy a = w.SpawnEnemy(new(360, 944)), b = w.SpawnEnemy(new(420, 944));
    w.TryShoot(HeroKind.Shotgun, -Vector2.UnitY, 1); Tick(w, .4f);
    Near(a.Health, 160, "暴击直击"); Near(b.Health, 250, "暴击溅射");
});
Test("闪电只传递一次，燃烧造成三次周期伤害且不会递归传播", () =>
{
    UpgradeState buffs = new(); buffs.Add(UpgradeKind.ChainLightning); buffs.Add(UpgradeKind.BurningRounds);
    GameRules r = Sandbox(); r.Enemy.Health = 300; BattleWorld w = new(r, buffs); w.Start();
    Enemy direct = w.SpawnEnemy(new(360, 944)), nearby = w.SpawnEnemy(new(440, 944)), second = w.SpawnEnemy(new(360, 844));
    w.TryShoot(HeroKind.Sniper, -Vector2.UnitY, 1); Tick(w, 4);
    Near(direct.Health, 226, "50直击+24灼烧"); Near(second.Health, 226, "贯穿附加灼烧");
    Near(nearby.Health, 282, "18点闪电不能递归附加灼烧");
    Check(!buffs.Add(UpgradeKind.ChainLightning) && !buffs.Add(UpgradeKind.BurningRounds), "唯一技能重复获取");
});
Test("清波掉落三选一并冻结战斗，不能重复或选择未掉落卡", () =>
{
    GameRules r = Sandbox(); r.Waves.Add(new() { Count = 1, SpawnInterval = 1 }); r.Waves.Add(new() { Count = 1, SpawnInterval = 1 });
    BattleWorld w = new(r, offerUpgrades: true); w.Start(); Tick(w, .8f);
    w.DamageEnemy(w.Enemies[0], 999, HeroKind.Standard); w.Update(.02f);
    Check(w.State == BattleState.ChoosingUpgrade && w.OfferedUpgrades.Count == 3 && w.OfferedUpgrades.Distinct().Count() == 3, "没有三张不同的卡");
    float time = w.Elapsed, next = w.NextWaveIn; Tick(w, 2);
    Check(w.Elapsed == time && w.NextWaveIn == next && !w.TryShoot(HeroKind.Standard, -Vector2.UnitY, 1), "选卡没有冻结");
    UpgradeKind absent = Enum.GetValues<UpgradeKind>().First(k => !w.OfferedUpgrades.Contains(k));
    Check(!w.SelectUpgrade(absent), "选择未掉落卡成功");
    UpgradeKind card = w.OfferedUpgrades[0];
    Check(w.SelectUpgrade(card) && !w.SelectUpgrade(card) && w.Upgrades.Count(card) == 1 && w.State == BattleState.Running, "重复拾取");
});
Test("存档保留章节检查点、加成副本和旧评分数据", () =>
{
    var profile = new PlayerProfile { SelectedLevel = 8, Checkpoint = new() { LevelId = 8, Difficulty = DifficultyKind.Standard } };
    profile.Checkpoint.Upgrades.Add(UpgradeKind.Speed20); profile.Checkpoint.Upgrades.Add(UpgradeKind.DoubleAmmo);
    string path = Path.Combine(Path.GetDirectoryName(args[1])!, "checkpoint-test-" + Guid.NewGuid().ToString("N") + ".json");
    ProfileStore store = new(path); Check(store.Save(profile), "检查点写入失败");
    PlayerProfile loaded = store.Load();
    Check(loaded.SelectedLevel == 8 && loaded.Checkpoint?.LevelId == 8 && loaded.Checkpoint.Upgrades.AmmoCount == 2, "检查点丢失");
    UpgradeState copy = loaded.Checkpoint!.Upgrades.Clone(); copy.Add(UpgradeKind.Speed20);
    Near(loaded.Checkpoint.Upgrades.AttackSpeedBonus, .2f, "副本修改影响原检查点");
    loaded.Stages["1:Easy"] = new() { Stars = 3, BestScore = 500, Clears = 2 };
    GameRules r = Sandbox(); r.Waves.Add(new() { Count = 1, SpawnInterval = 1 });
    BattleWorld w = Running(r); Tick(w, .8f); w.Barrier.TakeDamage(1); w.DamageEnemy(w.Enemies[0], 999, HeroKind.Standard); w.Update(.02f);
    loaded.Record(w, DifficultyKind.Easy);
    Check(loaded.Stages["1:Easy"].LegacyStars == 3 && loaded.Stages["1:Easy"].Stars == 2
        && loaded.Stages["1:Easy"].BestScore == 500 && loaded.Stages["1:Easy"].Clears == 3, "历史评分没有隔离保留");
});
Test("三章九关三难度带真实增益和弹药连续通关", () =>
{
    foreach (DifficultyKind difficulty in Enum.GetValues<DifficultyKind>())
    foreach (int chapter in new[] { 1, 2, 3 })
    {
        UpgradeState carried = new();
        for (int level = (chapter - 1) * 3 + 1; level <= chapter * 3; level++)
        {
            BattleWorld w = new(Load().ForDifficulty(difficulty).ForLevel(level), carried, true); w.Start();
            int cards = 0, bossCount = 0, peak = 0;
            w.EventRaised += ev => { if (ev.Kind == CombatEventKind.EnemySpawn && ev.Enemy == EnemyKind.Boss) bossCount++; };
            for (int step = 0; step < 36000 && w.State is BattleState.Running or BattleState.ChoosingUpgrade; step++)
            {
                if (w.State == BattleState.ChoosingUpgrade)
                {
                    UpgradeKind choice = w.OfferedUpgrades.OrderBy(k => k == UpgradeKind.DoubleAmmo ? 0 : k == UpgradeKind.Damage20 ? 1 : 2).First();
                    w.SelectUpgrade(choice); cards++;
                }
                foreach (HeroKind kind in new[] { HeroKind.Shotgun, HeroKind.Standard, HeroKind.Sniper })
                {
                    HeroState hero = w.Heroes[kind]; if (!hero.Ready) continue;
                    Enemy? target = w.Enemies.Where(e => e.Alive && Vector2.Distance(e.Position, GameRules.LaunchPoint) < hero.Definition.Range - 12)
                        .OrderByDescending(e => e.WindupRemaining > 0).ThenByDescending(e => e.Position.Y).FirstOrDefault();
                    if (target == null) continue;
                    float flight = Vector2.Distance(target.Position, GameRules.LaunchPoint) / w.Rules.ProjectileSpeed;
                    Vector2 p = target.Position;
                    if (!target.AttackingBarrier && target.DashTime <= 0) p.Y += target.Speed * flight;
                    p.Y -= .5f * w.Rules.ProjectileGravity * flight * flight;
                    w.TryShoot(kind, p - GameRules.LaunchPoint, 1, hero.SkillReady);
                }
                w.Update(1f / 120); peak = Math.Max(peak, w.Enemies.Count);
            }
            Check(w.State == BattleState.Victory && cards == 2 && bossCount == GameRules.BossWaves(level) && peak <= GameRules.MaxLivingEnemies,
                $"{difficulty}/{level}: state={w.State}, core={w.Core.Lives}, cards={cards}, bosses={bossCount}, peak={peak}");
            carried = w.Upgrades.Clone();
            Console.WriteLine($"  CHAPTER chapter={chapter}; stage={level}; difficulty={difficulty}; cards={carried.Stacks.Values.Sum()}; kills={w.Kills}; stars={w.Stars}; seconds={w.Elapsed:0.0}");
        }
        Check(carried.Stacks.Values.Sum() == 6, "同章加成未保留");
    }
});

Test("轻松档九关慢速普通射手每0.9秒一发且四发漏一仍可通关", () =>
{
    foreach (int chapter in new[] { 1, 2, 3 })
    {
        UpgradeState carried = new();
        for (int level = (chapter - 1) * 3 + 1; level <= chapter * 3; level++)
        {
            BattleWorld w = new(Load().ForDifficulty(DifficultyKind.Easy).ForLevel(level), carried, true); w.Start();
            float nextShot = .9f;
            int attempts = 0, misses = 0, peak = 0;
            for (int frame = 0; frame < 120 * 360 && w.State is BattleState.Running or BattleState.ChoosingUpgrade; frame++)
            {
                if (w.State == BattleState.ChoosingUpgrade)
                {
                    w.SelectUpgrade(w.OfferedUpgrades.OrderBy(k => k == UpgradeKind.DoubleAmmo ? 0 : k == UpgradeKind.Damage20 ? 1 : 2).First());
                }
                if (w.Elapsed >= nextShot)
                {
                    Enemy? target = w.Enemies.Where(e => e.Alive && Vector2.Distance(e.Position, GameRules.LaunchPoint) < w.Heroes[HeroKind.Standard].Definition.Range - 24)
                        .OrderByDescending(e => e.Position.Y).FirstOrDefault();
                    if (target != null)
                    {
                        attempts++;
                        float flight = Vector2.Distance(target.Position, GameRules.LaunchPoint) / w.Rules.ProjectileSpeed;
                        Vector2 aim = target.Position;
                        if (!target.AttackingBarrier && target.DashTime <= 0) aim.Y += target.Speed * flight;
                        aim.Y -= .5f * w.Rules.ProjectileGravity * flight * flight;
                        Vector2 direction = aim - GameRules.LaunchPoint;
                        if (attempts % 4 == 0) { direction = Vector2.UnitX; misses++; }
                        w.TryShoot(HeroKind.Standard, direction, 1);
                    }
                    nextShot += .9f;
                }
                w.Update(1f / 120);
                peak = Math.Max(peak, w.Enemies.Count);
            }
            Check(w.State == BattleState.Victory && w.Core.Lives > 0 && peak <= GameRules.MaxLivingEnemies,
                $"慢速射击第{level}关未通关：{w.State}, core={w.Core.Lives}, kills={w.Kills}");
            Check(w.Heroes[HeroKind.Sniper].ShotsFired == 0 && w.Heroes[HeroKind.Shotgun].ShotsFired == 0
                && w.Heroes.Values.All(h => h.SkillsUsed == 0), "测试意外使用了其他射手或技能");
            carried = w.Upgrades.Clone();
            Console.WriteLine($"  RELAXED_PLAY level={level}; shots={attempts}; plannedMisses={misses}; stars={w.Stars}; core={w.Core.Lives}; peak={peak}; seconds={w.Elapsed:0.0}");
        }
    }
});

string summary = JsonSerializer.Serialize(new { total = results.Count, passed = results.Count - failures, failed = failures, results }, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 1) File.WriteAllText(args[1], summary);
Console.WriteLine($"RESULT {results.Count - failures}/{results.Count} passed");
return failures == 0 ? 0 : 1;

