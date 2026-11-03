using System.Text.Json;
using Godot;
using FruitDefense.Core;
using FruitDefense.UI;
using NVector2 = System.Numerics.Vector2;

namespace FruitDefense;

// 真正的Godot输入与绘制流程，只有显式传入 --integration 时才运行。
public static class PrototypeChecks
{
    public static async Task Run(GameController game, string reportPath)
    {
        var results = new List<object>();
        object? pacedRun = null;
        int failed = 0;
        async Task Settle() => await game.ToSignal(game.GetTree().CreateTimer(.12), SceneTreeTimer.SignalName.Timeout);
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        IEnumerable<Node> Descendants(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                yield return child;
                foreach (Node nested in Descendants(child)) yield return nested;
            }
        }
        Button Button(string text) => Descendants(game).OfType<Button>().First(b => b.Text == text && b.IsVisibleInTree());
        void Mouse(Vector2 point, bool pressed, MouseButton button = MouseButton.Left, int device = 0)
        {
            game.GetViewport().PushInput(new InputEventMouseButton
            {
                Position = point, GlobalPosition = point, ButtonIndex = button, Device = device,
                ButtonMask = pressed ? (button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
                Pressed = pressed
            }, true);
        }
        void Move(Vector2 point) => game.GetViewport().PushInput(new InputEventMouseMotion
            { Position = point, GlobalPosition = point, ButtonMask = MouseButtonMask.Left }, true);
        void Click(Button button)
        {
            Vector2 p = button.GetGlobalRect().GetCenter();
            game.GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
            Mouse(p, true); Mouse(p, false);
        }
        void Touch(Vector2 point, bool pressed, int index = 0, bool canceled = false) =>
            game.GetViewport().PushInput(new InputEventScreenTouch
                { Position = point, Index = index, Pressed = pressed, Canceled = canceled }, true);
        void Drag(Vector2 point, int index = 0) => game.GetViewport().PushInput(new InputEventScreenDrag
            { Position = point, Index = index }, true);
        void KeyPress(Key key)
        {
            game.GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = true }, true);
            game.GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = false }, true);
        }
        Vector2 Origin() => new(GameRules.LaunchPoint.X, GameRules.LaunchPoint.Y);
        void Shoot()
        {
            Vector2 p = Origin();
            Mouse(p, true); Move(p + new Vector2(0, 112)); Mouse(p + new Vector2(0, 112), false);
        }
        async Task Test(string name, Func<Task> run)
        {
            try { await run(); results.Add(new { name, passed = true }); GD.Print($"UI_PASS {name}"); }
            catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.Message }); GD.PrintErr($"UI_FAIL {name}: {e.Message}"); }
        }
        async Task Screenshot(string name)
        {
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(reportPath)!, "../画面截图"));
            Directory.CreateDirectory(directory);
            Assert(game.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name)) == Error.Ok, "截图失败");
        }
        void Pause() => Click(Descendants(game).OfType<PauseIconButton>().Single());
        void PickReward()
        {
            if (game.World.State != BattleState.ChoosingUpgrade) return;
            Click(Descendants(game).OfType<Button>().First(b => b.Name == "UpgradeChoice0" && b.IsVisibleInTree()));
        }
        void FastClearUntil(bool reward)
        {
            for (int i = 0; i < 20000 && game.World.State is BattleState.Running or BattleState.ChoosingUpgrade; i++)
            {
                if (game.World.State == BattleState.ChoosingUpgrade)
                {
                    if (reward) return;
                    PickReward();
                }
                game.World.Update(.05f);
                foreach (var enemy in game.World.Enemies.ToArray()) game.World.DamageEnemy(enemy, 99999, HeroKind.Standard);
            }
        }


        try
        {
            await Settle();
            await Test("主页可选三档难度并开始游戏", async () =>
            {
                Assert(game.CurrentMenu == MenuPage.Home, "没有从主页启动");
                Click(Button("挑战")); await Settle(); Assert(game.Rules.Waves.Sum(w => w.Count) == 19, "挑战无效");
                Click(Button("标准")); await Settle(); Assert(game.Rules.Waves.Sum(w => w.Count) == 15, "标准无效");
                Click(Button("轻松")); await Settle(); Assert(game.Difficulty == DifficultyKind.Easy, "默认轻松无效");
                await Screenshot("portrait-home.png");
                Click(Button("设置")); await Settle();
                Click(Button("声音：开")); Click(Button("返回")); await Settle();
                Click(Button("开始游戏")); await Settle();
                Assert(game.World.State == BattleState.Running && game.CurrentMenu == MenuPage.None, "开始无效");
            });
            await Test("720乘1280竖屏且战斗中只保留精简信息和图标", async () =>
            {
                Vector2 size = game.GetViewport().GetVisibleRect().Size;
                Assert(size == new Vector2(720, 1280) && game.GetWindow().Size.Y > game.GetWindow().Size.X, "不是竖屏");
                Button[] buttons = Descendants(game).OfType<Button>().Where(b => b.IsVisibleInTree()).ToArray();
                Assert(buttons.Length == 4 && buttons.All(b => b is HeroCard or PauseIconButton), "战场还有多余按钮");
                string[] texts = Descendants(game).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text).ToArray();
                Assert(texts.Contains("第1波") && texts.Contains("共3波") && !texts.Any(t => t.Contains("伤害") || t.Contains("操作指南") || t.Contains("守护目标")), "顶部含义或信息精简错误");
                Assert((GameRules.BarrierY - 126) / GameRules.ViewHeight > .68f, "战场比例过小");
                await Screenshot("portrait-battle.png");
            });
            await Test("英雄头像和快捷键均可切换", async () =>
            {
                KeyPress(Key.Key2); await Settle(); Assert(game.Selected == HeroKind.Sniper, "快捷键无效");
                Click(Descendants(game).OfType<HeroCard>().First(c => c.Hero.Definition.Kind == HeroKind.Standard));
                await Settle(); Assert(game.Selected == HeroKind.Standard, "头像无效");
            });
            await Test("鼠标拉弓和独立冷却接续发射", async () =>
            {
                int before = game.World.Heroes[HeroKind.Standard].ShotsFired;
                Shoot(); KeyPress(Key.Key2); Shoot(); await Settle();
                Assert(game.World.Heroes[HeroKind.Standard].ShotsFired == before + 1 &&
                    game.World.Heroes[HeroKind.Sniper].ShotsFired == 1 && !game.Aim.Dragging, "接续发射失败");
            });
            await Test("触屏拉弓、第二指隔离和模拟鼠标不重复发射", async () =>
            {
                game.ResetBattle(true);
                Vector2 p = Origin();
                Touch(p, true); Drag(p + new Vector2(0, 112));
                Assert(game.Aim.Valid, "触屏未进入拉弓");
                NVector2 held = game.Aim.DragPoint;
                Touch(new(90, 1150), true, 1); Drag(new(120, 1140), 1); Touch(new(120, 1140), false, 1);
                Assert(game.Aim.DragPoint == held && game.Aim.Dragging, "第二指干扰");
                Touch(p + new Vector2(0, 112), false);
                Mouse(p + new Vector2(0, 112), false, device: (int)InputEvent.DeviceIdEmulation);
                await Settle();
                Assert(game.World.Heroes[HeroKind.Standard].ShotsFired == 1 && !game.Aim.Dragging, "触屏未射击或重复发射");
            });
            await Test("触屏取消不发射", async () =>
            {
                game.ResetBattle(true);
                Vector2 p = Origin(); Touch(p, true); Drag(p + new Vector2(0, 112));
                Touch(p + new Vector2(0, 112), false, canceled: true); await Settle();
                Assert(!game.Aim.Dragging && game.World.Heroes[HeroKind.Standard].ShotsFired == 0, "系统取消仍发射");
            });
            await Test("暂停小菜单仅有三个按钮且冻结战斗和倒计时", async () =>
            {
                Pause(); await Settle();
                float time = game.World.Elapsed; float? next = game.World.NextArrivalIn;
                await Settle();
                Panel panel = Descendants(game).OfType<Panel>().Single(p => p.Name == "PausePanel");
                string[] items = Descendants(panel).OfType<Button>().Select(b => b.Text).ToArray();
                Assert(items.SequenceEqual(new[] { "继续游戏", "设置", "退出游戏" }), "暂停菜单不符");
                Assert(panel.Size.X * panel.Size.Y < 720 * 1280 * .15f, "暂停占屏过大");
                Assert(game.World.State == BattleState.Paused && game.World.Elapsed == time && game.World.NextArrivalIn == next, "暂停未冻结");
                await Screenshot("portrait-pause.png");
            });
            await Test("暂停中设置声音并返回，仍保持暂停", async () =>
            {
                float time = game.World.Elapsed;
                Click(Button("设置")); await Settle();
                Assert(game.CurrentMenu == MenuPage.Settings && game.World.State == BattleState.Paused, "设置状态错误");
                Click(Button("声音：关")); Assert(game.SoundEnabled, "声音开关无效");
                Click(Button("声音：开")); Assert(!game.SoundEnabled, "声音关闭无效");
                await Screenshot("portrait-settings.png");
                Click(Button("返回")); await Settle();
                Assert(game.CurrentMenu == MenuPage.Pause && game.World.Elapsed == time, "从设置返回自动继续了");
                Click(Button("继续游戏")); await Settle(); Assert(game.World.State == BattleState.Running, "无法继续");
            });
            await Test("退出当局返回主页并能重新开始", async () =>
            {
                Pause(); Click(Button("退出游戏")); await Settle();
                Assert(game.CurrentMenu == MenuPage.Home && game.World.State == BattleState.Ready && game.World.Enemies.Count == 0, "退出未回主页");
                Click(Button("开始游戏")); await Settle();
                Assert(game.World.State == BattleState.Running && game.World.Score == 0 && game.World.Heroes.Values.All(h => h.Ready), "新局状态残留");
            });
            await Test("手机返回通知优先取消瞄准，再控制暂停和设置返回", async () =>
            {
                Vector2 p = Origin(); Touch(p, true); Drag(p + new Vector2(0, 112));
                game.Notification((int)Node.NotificationWMGoBackRequest);
                Assert(!game.Aim.Dragging && game.World.State == BattleState.Running, "返回没有取消拉弓");
                game.Notification((int)Node.NotificationWMGoBackRequest); await Settle();
                Assert(game.CurrentMenu == MenuPage.Pause, "返回没有进入暂停");
                Click(Button("设置")); game.Notification((int)Node.NotificationWMGoBackRequest); await Settle();
                Assert(game.CurrentMenu == MenuPage.Pause, "返回没有关闭设置");
                Click(Button("继续游戏")); await Settle();
            });
            await Test("敌人和水晶保留生命，顶部次数仍指入侵波次", async () =>
            {
                var e = game.World.SpawnEnemy(new(360, 640)); game.World.DamageEnemy(e, 20, HeroKind.Standard);
                game.World.Core.RegisterIntrusion(-1); await Settle();
                string[] texts = Descendants(game).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text).ToArray();
                Assert(e.Health == 40 && game.World.Core.Lives == 7 && texts.Contains("第1波") && !texts.Any(t => t.Contains("水晶生命")), "生命/顶部次数混淆");
                await Screenshot("portrait-battle-health.png");
                while (game.World.WaveIndex == 0)
                {
                    game.World.Update(.05f);
                    foreach (var enemy in game.World.Enemies.ToArray()) game.World.DamageEnemy(enemy, 999, HeroKind.Standard);
                }
                await Settle();
                Assert(game.World.BetweenWaves && game.World.NextArrivalIn > 0 && Descendants(game).OfType<Label>()
                    .Any(l => l.IsVisibleInTree() && l.Text == "第1波"), "下一波尚未开始时提前增加入侵次数");
                game.ResetBattle(true); await Settle();
            });
            await Test("破门后反向拉弓拦截水晶前的敌人", async () =>
            {
                game.World.Barrier.TakeDamage(game.Rules.BarrierHealth);
                var e = game.World.SpawnEnemy(new(360, 1140)); KeyPress(Key.Key3);
                Vector2 p = Origin(); Mouse(p, true); Move(p + new Vector2(0, 112)); Move(p);
                Assert(!game.Aim.Valid, "原点无法收回");
                Move(p - new Vector2(0, 112)); Assert(game.Aim.Direction.Y > .99f, "无法向下瞄准");
                await Screenshot("portrait-breach-aim.png");
                Mouse(p - new Vector2(0, 112), false); await Settle();
                Assert(!e.Alive && game.World.Core.Lives == 8 && game.World.Kills == 1, "反向拦截失败");
                game.ResetBattle(true); await Settle();
            });
            await Test("拉弓松手经过英雄按钮也不会卡住", async () =>
            {
                Vector2 p = Origin(); Mouse(p, true); Move(new(200, 1160)); Mouse(new(200, 1160), false); await Settle();
                Assert(!game.Aim.Dragging && game.World.Heroes[HeroKind.Standard].ShotsFired == 1, "松手被控件拦截");
                game.ResetBattle(true); await Settle();
            });
            await Test("竖屏轻松档慢节奏普通射手仍可通关", async () =>
            {
                game.SetPhysicsProcess(false);
                int attempts = 0, plannedMisses = 0, maxEnemies = 0;
                float nextShot = .9f;
                try
                {
                    for (int step = 0; step < 5000 && game.World.State is BattleState.Running or BattleState.ChoosingUpgrade; step++)
                    {
                        PickReward();
                        if (game.World.Elapsed >= nextShot)
                        {
                            var target = game.World.Enemies.Where(e => e.Alive &&
                                NVector2.Distance(e.Position, GameRules.LaunchPoint) < game.World.Heroes[HeroKind.Standard].Definition.Range - 24)
                                .OrderByDescending(e => e.Position.Y).FirstOrDefault();
                            if (target != null)
                            {
                                attempts++;
                                NVector2 direction = target.Position - GameRules.LaunchPoint;
                                if (attempts % 4 == 0) { direction.X += 180; plannedMisses++; }
                                NVector2 pull = -NVector2.Normalize(direction) * 112;
                                Vector2 p = Origin();
                                Mouse(p, true); Move(p + new Vector2(pull.X, pull.Y)); Mouse(p + new Vector2(pull.X, pull.Y), false);
                            }
                            nextShot += .9f;
                        }
                        game.World.Update(.05f);
                        maxEnemies = Math.Max(maxEnemies, game.World.Enemies.Count);
                    }
                }
                finally { game.SetPhysicsProcess(true); }
                await Settle();
                Assert(game.World.State == BattleState.Victory && game.World.Kills == game.Rules.Waves.Sum(w => w.Count) && game.World.Core.Lives == 8,
                    $"慢节奏结果 {game.World.State}, kills={game.World.Kills}, core={game.World.Core.Lives}");
                Assert(game.World.Heroes[HeroKind.Standard].ShotsFired == attempts, "输入没有真正发射");
                pacedRun = new { attempts, plannedMisses, maxEnemies, kills = game.World.Kills, core = game.World.Core.Lives, seconds = game.World.Elapsed };
                GD.Print($"PACED_PLAYTHROUGH shots={attempts}; plannedMisses={plannedMisses}; maxEnemies={maxEnemies}; core={game.World.Core.Lives}; seconds={game.World.Elapsed:0.0}");
                await Screenshot("portrait-paced-victory.png");
                game.ResetBattle(true); await Settle();
            });
            await Test("失败结算与返回主页", async () =>
            {
                for (int i = 0; i < 15000 && game.World.State is BattleState.Running or BattleState.ChoosingUpgrade; i++) { PickReward(); game.World.Update(.02f); }
                await Settle();
                Assert(game.World.State == BattleState.Defeat && game.CurrentMenu == MenuPage.Result, "失败未结算");
                await Screenshot("portrait-defeat.png");
                Click(Button("返回主页")); await Settle(); Click(Button("开始游戏")); await Settle();
            });
            await Test("胜利结算与再次开始", async () =>
            {
                for (int i = 0; i < 15000 && game.World.State is BattleState.Running or BattleState.ChoosingUpgrade; i++)
                {
                    PickReward();
                    game.World.Update(.02f);
                    foreach (var e in game.World.Enemies.ToArray()) game.World.DamageEnemy(e, 999, HeroKind.Standard);
                }
                await Settle();
                Assert(game.World.State == BattleState.Victory && game.CurrentMenu == MenuPage.Result, "胜利未结算");
                Click(Button("再来一次")); await Settle();
                Assert(game.World.State == BattleState.Running && game.World.Score == 0, "重开未清理");
            });
            await Test("360乘640和540乘960竖屏预览下触屏输入仍有效", async () =>
            {
                foreach (Vector2I size in new[] { new Vector2I(360, 640), new Vector2I(540, 960) })
                {
                    game.GetWindow().Size = size; await Settle(); game.ResetBattle(true);
                    Vector2 p = Origin(); Touch(p, true); Drag(p + new Vector2(0, 112)); Touch(p + new Vector2(0, 112), false);
                    await Settle(); Assert(game.World.Heroes[HeroKind.Standard].ShotsFired == 1, "缩放触屏失败");
                }
                await Screenshot("portrait-battle-large.png");
                game.GetWindow().Size = new(450, 800); await Settle();
            });
            await Test("玩家资料保存并可重新读取，空名字被拒绝", async () =>
            {
                game.ResetBattle(false); await Settle();
                Click(Button("玩家资料")); await Settle();
                LineEdit input = Descendants(game).OfType<LineEdit>().Single(l => l.Name == "UsernameInput");
                input.Text = "   "; Click(Button("保存用户名"));
                Assert(game.Profile.Username == "果园守护者", "空名字覆盖玩家");
                input.Text = "测试守护者"; Click(Button("保存用户名")); await Settle();
                Assert(game.SaveStore.Load().Username == "测试守护者", "资料未写入本地");
                await Screenshot("functional-profile.png");
                Click(Button("返回主页")); await Settle();
            });
            await Test("英雄图鉴切换、设为主页与技能试用不写战绩", async () =>
            {
                int battles = game.Profile.TotalBattles, kills = game.Profile.TotalKills;
                Click(Button("英雄图鉴")); await Settle();
                Click(Button("蓝莓狙击")); Click(Button("设为主页英雄")); await Settle();
                Assert(game.Profile.DisplayHero == HeroKind.Sniper && game.SaveStore.Load().DisplayHero == HeroKind.Sniper, "主页英雄未保存");
                await Screenshot("functional-hero-codex.png");
                Click(Button("技能试用")); await Settle();
                Assert(game.IsTraining && game.Selected == HeroKind.Sniper && game.World.Enemies.Count == 3, "试用没有正确初始化");
                game.World.DamageEnemy(game.World.Enemies[0], 9999, HeroKind.Standard);
                Pause(); Click(Button("退出游戏")); await Settle();
                Assert(game.Profile.TotalBattles == battles && game.Profile.TotalKills == kills, "试用污染战绩");
            });
            await Test("真实长拉释放技能、冷却回退普攻和取消不耗技能", async () =>
            {
                Click(Button("英雄图鉴")); Click(Button("蜜橙快射")); Click(Button("技能试用")); await Settle();
                Vector2 p = Origin(); Mouse(p, true); Move(p + new Vector2(0, 112));
                await game.ToSignal(game.GetTree().CreateTimer(.85), SceneTreeTimer.SignalName.Timeout);
                Assert(game.ChargeProgress >= 1, "蓄力未完成");
                await Screenshot("functional-charged-aim.png");
                Mouse(p + new Vector2(0, 112), false); await Settle();
                Assert(game.World.Heroes[HeroKind.Standard].SkillsUsed == 1, "长拉没有释放技能");
                await Settle(); Shoot(); await Settle();
                Assert(game.World.Heroes[HeroKind.Standard].SkillsUsed == 1 && game.World.Heroes[HeroKind.Standard].ShotsFired == 2, "冷却时未回退普攻");
                KeyPress(Key.Key2); Mouse(p, true); Move(p + new Vector2(0, 112));
                await game.ToSignal(game.GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
                Mouse(p, true, MouseButton.Right); await Settle();
                Assert(!game.Aim.Dragging && game.World.Heroes[HeroKind.Sniper].SkillsUsed == 0, "取消消耗技能");
                Mouse(p, true); Move(p + new Vector2(0, 112));
                await game.ToSignal(game.GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
                Move(p); Move(p + new Vector2(0, 112)); Mouse(p + new Vector2(0, 112), false); await Settle();
                Assert(game.World.Heroes[HeroKind.Sniper].SkillsUsed == 0, "收回后仍带旧蓄力");
                Pause(); Click(Button("退出游戏")); await Settle();
            });
            await Test("敌人图鉴与BOSS试用、预警和真实弹药打断", async () =>
            {
                Click(Button("敌人图鉴")); Click(Button("果园掠夺者")); await Settle();
                await Screenshot("functional-enemy-codex.png");
                Click(Button("机制试用"));
                await game.ToSignal(game.GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
                var boss = game.World.Enemies.Single(e => e.Kind == EnemyKind.Boss);
                Assert(game.IsTraining && boss.WindupRemaining > 0, "首领没有进入重击预警");
                await Screenshot("functional-boss-windup.png");
                KeyPress(Key.Key3); Shoot(); KeyPress(Key.Key2); Shoot(); await Settle();
                Assert(boss.WindupRemaining <= 0 && boss.StaggerRemaining > 0 && game.World.Barrier.Health == game.Rules.BarrierHealth, "没有打断首领");
                await Screenshot("functional-boss-interrupted.png");
                Pause(); Click(Button("退出游戏")); await Settle();
            });
            await Test("记忆大厅模拟播放暂停切换循环并在战斗中停播", async () =>
            {
                Click(Button("记忆大厅")); await Settle();
                float before = game.MemoryPosition; await Settle();
                Assert(game.MemoryPosition > before, "模拟播放不前进");
                Click(Button("暂停演示")); before = game.MemoryPosition; await Settle();
                Assert(game.MemoryPosition == before, "暂停后仍前进");
                int track = game.Profile.MemoryTrack; Click(Button("切换片段")); await Settle();
                Assert(game.Profile.MemoryTrack != track && game.MemoryPosition == 0, "切换未复位");
                Click(Button("切换展示英雄")); Assert(game.Profile.DisplayHero != HeroKind.Sniper, "展示未切换");
                Click(Button("继续演示")); await Settle(); Assert(game.MemoryPosition > 0, "继续失败");
                for (int i = 0; i < 31; i++) game._Process(1);
                Assert(game.MemoryPosition is >= 0 and < 30, "没有循环");
                await Screenshot("functional-memory-hall.png");
                Click(Button("返回主页")); Click(Button("开始游戏")); await Settle();
                before = game.MemoryPosition; await Settle();
                Assert(game.MemoryPosition == before && !game.MemoryActive, "战斗仍播放大厅内容");
                Pause(); Click(Button("退出游戏")); await Settle();
            });
            await Test("选第三关真实战斗后保存成绩一次，返回不重复累计", async () =>
            {
                Click(Button("关卡选择")); Click(Button("3 · 首领来袭")); await Settle();
                Assert(game.SelectedLevel == 3 && game.Rules.Waves[^1].HasBoss && game.SaveStore.Load().SelectedLevel == 3, "选关未保存");
                await Screenshot("functional-level-select.png");
                int before = game.Profile.TotalBattles, peak = 0;
                bool sawBoss = false;
                Click(Button("进入关卡")); await Settle();
                game.SetPhysicsProcess(false);
                try
                {
                    for (int step = 0; step < 5000 && game.World.State is BattleState.Running or BattleState.ChoosingUpgrade; step++)
                    {
                        PickReward();
                        foreach (HeroKind kind in Enum.GetValues<HeroKind>())
                        {
                            var hero = game.World.Heroes[kind]; if (!hero.Ready) continue;
                            var target = game.World.Enemies.Where(e => e.Alive &&
                                NVector2.Distance(e.Position, GameRules.LaunchPoint) < hero.Definition.Range - 12)
                                .OrderByDescending(e => e.Position.Y).FirstOrDefault();
                            if (target == null) continue;
                            if (target.Kind == EnemyKind.Boss) sawBoss = true;
                            float flight = NVector2.Distance(target.Position, GameRules.LaunchPoint) / game.Rules.ProjectileSpeed;
                            NVector2 predicted = target.Position;
                            if (!target.AttackingBarrier && target.DashTime <= 0) predicted.Y += target.Speed * flight;
                            predicted.Y -= .5f * game.Rules.ProjectileGravity * flight * flight;
                            NVector2 pull = -NVector2.Normalize(predicted - GameRules.LaunchPoint) * 132;
                            KeyPress(kind == HeroKind.Standard ? Key.Key1 : kind == HeroKind.Sniper ? Key.Key2 : Key.Key3);
                            Vector2 p = Origin(); Mouse(p, true); Move(p + new Vector2(pull.X, pull.Y)); Mouse(p + new Vector2(pull.X, pull.Y), false);
                        }
                        game.World.Update(.05f);
                        peak = Math.Max(peak, game.World.Enemies.Count);
                    }
                }
                finally { game.SetPhysicsProcess(true); }
                await Settle();
                Assert(sawBoss && peak <= GameRules.MaxLivingEnemies && game.World.State == BattleState.Victory && game.World.Kills == game.Rules.Waves.Sum(w => w.Count) + 3, "第三关未正常通关");
                Assert(game.Profile.TotalBattles == before + 1 && game.Profile.Stages["3:Easy"].Stars > 0, "结算未记录");
                Click(Button("返回主页")); await Settle();
                Assert(game.Profile.TotalBattles == before + 1 && game.SaveStore.Load().TotalBattles == before + 1, "返回重复统计");
                Click(Button("战绩")); await Settle(); await Screenshot("functional-records.png");
                Click(Button("返回主页")); Click(Button("关卡选择")); Click(Button("1 · 果园初战")); Click(Button("返回主页")); await Settle();
            });
            await Test("三章九关可选且首领波次随关卡变化", async () =>
            {
                game.ResetBattle(false); Click(Button("关卡选择")); Click(Button("第2章")); await Settle();
                Assert(game.SelectedLevel == 4, "第二章没有选择首关");
                Click(Button("3 · 双首领突袭")); await Settle();
                Assert(game.SelectedLevel == 6 && game.Rules.Waves.Count(w => w.HasBoss) == 2, "双首领关无效");
                Click(Button("第3章")); Click(Button("3 · 终局围城")); await Settle();
                Assert(game.SelectedLevel == 9 && game.Rules.Waves.All(w => w.HasBoss), "终局不是三波首领");
                await Screenshot("chapter-select-nine-levels.png");
                Click(Button("第1章")); Click(Button("进入关卡")); await Settle();
            });
            await Test("清波掉落三张卡且计时冻结，点击只获得一张", async () =>
            {
                game.SetPhysicsProcess(false);
                try
                {
                    FastClearUntil(true); await Settle();
                    Assert(game.CurrentMenu == MenuPage.Reward && game.World.OfferedUpgrades.Count == 3, "清波没有显示卡片");
                    float time = game.World.Elapsed, next = game.World.NextWaveIn;
                    await Settle(); Assert(game.World.Elapsed == time && game.World.NextWaveIn == next, "选卡仍推进战斗");
                    await Screenshot("chapter-upgrade-drop.png");
                    PickReward(); await Settle();
                    Assert(game.CurrentMenu == MenuPage.None && game.World.Upgrades.Stacks.Values.Sum() == 1, "选择数量不正确");
                }
                finally { game.SetPhysicsProcess(true); }
            });
            await Test("下一关保留本章增益，重试和恢复检查点不刷卡", async () =>
            {
                game.SetPhysicsProcess(false);
                try { FastClearUntil(false); } finally { game.SetPhysicsProcess(true); }
                await Settle();
                Assert(game.World.State == BattleState.Victory && game.World.Upgrades.Stacks.Values.Sum() == 2, "首关未获得两张卡");
                Assert(game.World.Stars == 3 && game.Profile.Checkpoint?.LevelId == 2, "结算评分或下一关检查点错误");
                await Screenshot("chapter-three-star-result.png");
                Click(Button("下一关 · 保留增益")); await Settle();
                Assert(game.SelectedLevel == 2 && game.World.Upgrades.Stacks.Values.Sum() == 2
                    && game.World.Barrier.Health == game.Rules.BarrierHealth && game.World.Core.Lives == 8, "跨关状态错误");
                game.SetPhysicsProcess(false);
                try { FastClearUntil(true); PickReward(); } finally { game.SetPhysicsProcess(true); }
                Assert(game.World.Upgrades.Stacks.Values.Sum() == 3, "第二关新卡没累加");
                KeyPress(Key.R); await Settle();
                Assert(game.World.Upgrades.Stacks.Values.Sum() == 2, "重试保留了本关重复收益");
                Pause(); await Screenshot("chapter-carried-buffs.png");
                Click(Button("退出游戏")); await Settle();
                Assert(game.SaveStore.Load().Checkpoint?.Upgrades.Stacks.Values.Sum() == 2, "检查点存档错误");
                Click(Button("继续章节")); await Settle();
                Assert(game.SelectedLevel == 2 && game.World.Upgrades.Stacks.Values.Sum() == 2, "继续章节丢失增益");
                Pause(); Click(Button("退出游戏")); await Settle();
                Click(Button("关卡选择")); Click(Button("第2章")); Click(Button("进入关卡")); await Settle();
                Assert(game.SelectedLevel == 4 && game.World.Upgrades.Stacks.Count == 0, "换章节保留了旧加成");
                Pause(); Click(Button("退出游戏")); await Settle();
            });
            await Test("结算按防线显示三二一星，水晶归零失败零星", async () =>
            {
                Click(Button("关卡选择")); Click(Button("第1章")); Click(Button("进入关卡")); await Settle();
                foreach ((float damage, int expected) in new[] { (1f, 2), (game.Rules.BarrierHealth, 1) })
                {
                    game.World.Barrier.TakeDamage(damage);
                    game.SetPhysicsProcess(false);
                    try { FastClearUntil(false); } finally { game.SetPhysicsProcess(true); }
                    await Settle();
                    Assert(game.World.State == BattleState.Victory && game.World.Stars == expected && Descendants(game).OfType<Label>()
                        .Any(l => l.IsVisibleInTree() && l.Text.Contains(expected + " 星")), "结算没有显示正确星级");
                    await Screenshot($"chapter-{expected}-star-result.png");
                    Click(Button("再来一次")); await Settle();
                }
                for (int i = 0; i < 8; i++) game.World.Core.RegisterIntrusion(-100 - i);
                game.World.Update(.02f); await Settle();
                Assert(game.World.State == BattleState.Defeat && game.World.Stars == 0, "失败判定错误");
                await Screenshot("chapter-failure-zero-stars.png");
                Click(Button("返回主页")); await Settle();
            });
        }
        catch (Exception e) { failed++; results.Add(new { name = "验证运行异常", passed = false, error = e.ToString() }); GD.PrintErr(e); }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            total = results.Count, passed = results.Count - failed, failed, results, pacedRun,
            renderDriver = RenderingServer.GetCurrentRenderingDriverName(), viewport = game.GetViewport().GetVisibleRect().Size.ToString()
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"UI_RESULT {results.Count - failed}/{results.Count} passed");
        game.GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
