using Godot;
using FruitDefense.Core;
using FruitDefense.Progression;
using FruitDefense.Presentation;

namespace FruitDefense;

// 导航和战斗分开：此处全部使用占位绘制，后续只替换展示资源。
public partial class GameController
{
    private readonly Dictionary<MenuPage, Control> _hubPages = [];
    private Label _username = null!, _homeStage = null!, _heroInfo = null!, _enemyInfo = null!, _recordsInfo = null!;
    private Label _levelInfo = null!, _profileHint = null!, _memoryInfo = null!;
    private LineEdit _usernameInput = null!;
    private Button _memoryPlay = null!;
    private HeroKind _catalogHero;
    private EnemyKind _catalogEnemy;
    private PreviewPortrait _homePortrait = null!, _heroPortrait = null!, _enemyPortrait = null!, _memoryPortrait = null!;
    public float MemoryPosition { get; private set; }
    public bool MemoryActive => Profile.MemoryPlaying && World.State == BattleState.Ready && CurrentMenu is MenuPage.Home or MenuPage.Memory;

    private void BuildHomeScreens()
    {
        _username = LabelAt(_home, "", new(42, 44), new(480, 40), 28, true);
        ButtonAt(_home, "玩家资料", new(526, 42), new(152, 52), () => OpenHub(MenuPage.Profile));
        LabelAt(_home, "保卫水果", new(220, 145), new(350, 72), 48, true);
        _homePortrait = Portrait(_home, new(220, 252), new(280, 210));
        _homeStage = LabelAt(_home, "", new(80, 460), new(560, 44), 24);
        _homeStage.HorizontalAlignment = HorizontalAlignment.Center;
        ButtonAt(_home, "开始游戏", new(180, 522), new(360, 76), StartCampaign);
        ButtonAt(_home, "关卡选择", new(42, 638), new(302, 68), () => OpenHub(MenuPage.Levels));
        ButtonAt(_home, "英雄图鉴", new(376, 638), new(302, 68), () => OpenHub(MenuPage.Heroes));
        ButtonAt(_home, "敌人图鉴", new(42, 732), new(302, 68), () => OpenHub(MenuPage.Enemies));
        ButtonAt(_home, "记忆大厅", new(376, 732), new(302, 68), () => OpenHub(MenuPage.Memory));
        ButtonAt(_home, "战绩", new(42, 826), new(302, 68), () => OpenHub(MenuPage.Records));
        ButtonAt(_home, "设置", new(376, 826), new(302, 68), OpenSettings);
        LabelAt(_home, "难度", new(42, 944), new(150, 36), 22);
        for (int i = 0; i < _baseRules.Difficulties.Count; i++)
        {
            DifficultyDefinition d = _baseRules.Difficulties[i];
            Button b = ButtonAt(_home, d.Name, new(180 + i * 166, 936), new(148, 58), () => ChooseDifficulty(d.Kind));
            b.ToggleMode = true;
            _difficultyButtons.Add(b);
        }
        LabelAt(_home, "功能试作 · 角色、场景与音乐等待素材替换", new(42, 1164), new(640, 40), 21);

        Control page = Page(MenuPage.Profile, "玩家资料");
        LabelAt(page, "用户名（1—12 个字符）", new(42, 190), new(600, 44), 26);
        _usernameInput = new LineEdit { Name = "UsernameInput", Position = new(42, 260), Size = new(636, 70), MaxLength = 12 };
        page.AddChild(_usernameInput);
        ButtonAt(page, "保存用户名", new(42, 366), new(636, 70), () =>
        {
            if (!Profile.SetUsername(_usernameInput.Text)) _profileHint.Text = "请输入 1—12 个字符，不能全部为空格。";
            else { SaveProfile(); _profileHint.Text = SaveStore.Notice ?? "用户名已保存。"; }
            UpdateInterface();
        });
        _profileHint = LabelAt(page, "", new(42, 470), new(636, 160), 24);
        _profileHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        page = Page(MenuPage.Levels, "关卡选择");
        BuildChapterSelection(page);
        _levelInfo = LabelAt(page, "", new(42, 580), new(636, 245), 26);
        _levelInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ButtonAt(page, "进入关卡", new(180, 850), new(360, 76), StartCampaign);
        LabelAt(page, "同章连续闯关保留增益；每关防线与水晶恢复。", new(42, 1050), new(636, 70), 23);

        BuildChapterHomeButton();

        page = Page(MenuPage.Heroes, "英雄图鉴");
        for (int i = 0; i < Rules.Heroes.Count; i++)
        {
            HeroDefinition hero = Rules.Heroes[i];
            ButtonAt(page, hero.Name, new(34 + i * 232, 160), new(220, 64), () => { _catalogHero = hero.Kind; UpdateInterface(); });
        }
        _heroPortrait = Portrait(page, new(240, 272), new(240, 170));
        _heroInfo = LabelAt(page, "", new(42, 480), new(636, 490), 25);
        _heroInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ButtonAt(page, "设为主页英雄", new(42, 1040), new(302, 72), () => { Profile.DisplayHero = _catalogHero; SaveProfile(); UpdateInterface(); });
        ButtonAt(page, "技能试用", new(376, 1040), new(302, 72), () => BeginTraining(_catalogHero));

        page = Page(MenuPage.Enemies, "敌人图鉴");
        foreach (EnemyKind kind in Enum.GetValues<EnemyKind>())
        {
            int i = (int)kind;
            ButtonAt(page, _baseRules.EnemyFor(kind).Name, new(34 + i % 2 * 350, 160 + i / 2 * 80), new(332, 64), () => { _catalogEnemy = kind; UpdateInterface(); });
        }
        _enemyPortrait = Portrait(page, new(240, 360), new(240, 180));
        _enemyInfo = LabelAt(page, "", new(42, 580), new(636, 390), 25);
        _enemyInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ButtonAt(page, "机制试用", new(180, 1040), new(360, 72), () => BeginTraining(HeroKind.Standard, _catalogEnemy));

        page = Page(MenuPage.Records, "战绩");
        _recordsInfo = LabelAt(page, "", new(42, 270), new(636, 900), 25);
        _recordsInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        BuildRecordChapters(page);

        page = Page(MenuPage.Memory, "记忆大厅");
        _memoryPortrait = Portrait(page, new(180, 240), new(360, 310));
        _memoryInfo = LabelAt(page, "", new(42, 630), new(636, 150), 27);
        _memoryPlay = ButtonAt(page, "暂停演示", new(42, 830), new(302, 72), () => { Profile.MemoryPlaying = !Profile.MemoryPlaying; SaveProfile(); UpdateInterface(); });
        ButtonAt(page, "切换片段", new(376, 830), new(302, 72), () => { Profile.MemoryTrack = 1 - Profile.MemoryTrack; MemoryPosition = 0; SaveProfile(); });
        ButtonAt(page, "切换展示英雄", new(180, 950), new(360, 72), () => { Profile.DisplayHero = (HeroKind)(((int)Profile.DisplayHero + 1) % 3); SaveProfile(); });
        LabelAt(page, "此处模拟播放、暂停、切换与循环状态。\n视频和背景音乐将在素材提供后接入。", new(42, 1080), new(636, 110), 23);
    }
    private Control Page(MenuPage id, string title)
    {
        var page = new Control { Name = id + "Page", Size = new(720, 1280), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _ui.AddChild(page);
        page.AddChild(new ColorRect { Size = page.Size, Color = new("10292b"), MouseFilter = Control.MouseFilterEnum.Ignore });
        LabelAt(page, title, new(42, 52), new(480, 64), 38, true);
        ButtonAt(page, "返回主页", new(510, 46), new(168, 64), () => OpenHub(MenuPage.Home));
        _hubPages[id] = page;
        return page;
    }
    private PreviewPortrait Portrait(Control parent, Vector2 position, Vector2 size)
    {
        var preview = new PreviewPortrait { Controller = this, Position = position, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(preview);
        return preview;
    }
    public void OpenHub(MenuPage page)
    {
        if (World.State != BattleState.Ready) return;
        CurrentMenu = page;
        if (page == MenuPage.Profile) { _usernameInput.Text = Profile.Username; _profileHint.Text = SaveStore.Notice ?? "用户名、展示英雄和战绩保存在本机。"; }
        UpdateInterface();
    }
    private void StartCampaign()
    {
        IsTraining = false;
        _trainingEnemy = null;
        _chapterActive = true;
        _levelStartUpgrades = new();
        Rules = _baseRules.ForDifficulty(Difficulty).ForLevel(SelectedLevel);
        ResetBattle(true);
    }
    private void BeginTraining(HeroKind kind, EnemyKind? enemy = null)
    {
        _chapterActive = false;
        IsTraining = true;
        _trainingEnemy = enemy;
        Rules = _baseRules.ForDifficulty(DifficultyKind.Easy);
        Rules.Waves.Clear();
        Rules.Enemy.Health = enemy == null ? 3000 : 60;
        Rules.Enemy.Speed = enemy == null ? .001f : 32;
        ResetBattle(true);
        SelectHero(kind);
        SpawnTrainingTargets();
    }
    private void SpawnTrainingTargets()
    {
        if (_trainingEnemy is EnemyKind enemy)
            World.SpawnEnemy(new(360, enemy == EnemyKind.Boss ? 958 : 730), kind: enemy);
        else
            foreach (float x in new float[] { 280, 360, 440 }) World.SpawnEnemy(new(x, 900));
    }
    private void RecordBattle()
    {
        if (Profile == null || World == null || IsTraining || _battleRecorded || World.State == BattleState.Ready) return;
        Profile.Record(World, Difficulty);
        if (_chapterActive && World.State == BattleState.Victory)
            Profile.Checkpoint = Rules.ChapterFinal ? null : new()
            {
                LevelId = SelectedLevel + 1, Difficulty = Difficulty, Upgrades = World.Upgrades.Clone()
            };
        _battleRecorded = true;
        SaveProfile();
    }
    private void SaveProfile()
    {
        Profile.SoundEnabled = SoundEnabled;
        Profile.SelectedLevel = SelectedLevel;
        Profile.Difficulty = Difficulty;
        SaveStore.Save(Profile);
    }
    private void AdvanceMemory(float delta)
    {
        if (MemoryActive) MemoryPosition = (MemoryPosition + delta) % 30;
    }
    private void UpdateHomeScreens()
    {
        foreach (var (page, control) in _hubPages) control.Visible = CurrentMenu == page;
        _username.Text = Profile.Username;
        _homeStage.Text = $"第{Rules.ChapterId}章 {Rules.StageInChapter} · {Rules.LevelName} / {Rules.DifficultyName}";
        _homePortrait.Hero = _memoryPortrait.Hero = Profile.DisplayHero;
        _homePortrait.UseMemoryClock = _memoryPortrait.UseMemoryClock = true;
        _heroPortrait.Hero = _catalogHero;
        _enemyPortrait.Enemy = _catalogEnemy;
        HeroDefinition h = Rules.Heroes.Single(h => h.Kind == _catalogHero);
        string skill = h.Kind switch { HeroKind.Standard => "连续发射 3 发，每发伤害 20。", HeroKind.Sniper => "伤害 80；整段射程贯穿，沿途各命中一次。", _ => "直击 100，其他敌人溅射 35；范围 1.5 格。" };
        _heroInfo.Text = $"{h.Name}\n\n普通伤害 {h.Damage:0}" + (h.SplashDamage > 0 ? $" + 溅射 {h.SplashDamage:0}" : "") +
            $"\n射程 {h.RangeRatio:0.##} L · 发射间隔 {h.Cooldown:0.##} 秒\n\n{h.SkillName} · 冷却 {h.SkillCooldown:0} 秒\n{skill}\n\n长拉至 80% 以上并保持 0.7 秒，光圈充满后松手。技能冷却时松手仍发普通弹药。";
        EnemyDefinition e = _baseRules.EnemyFor(_catalogEnemy);
        string mechanism = _catalogEnemy switch
        {
            EnemyKind.Regular => "持续前进，在防线前周期攻击。",
            EnemyKind.Runner => "出现 5 秒后预警 0.6 秒，再进行一次短距离冲刺。",
            EnemyKind.Armored => "每次伤害减免 5 点，最低仍受到 1 点伤害。",
            _ => "防线前蓄力 2.5 秒发动重击；蓄力期间受到累计 100 点伤害可打断并硬直 1 秒。半血时仅召唤一次、共 2 名普通兵。"
        };
        _enemyInfo.Text = $"{e.Name} · {(Profile.SeenEnemies.Contains(_catalogEnemy) ? "已遭遇" : "尚未遭遇，可预览")}\n\n生命 {e.Health:0} · 防线伤害 {e.AttackDamage:0}\n基础速度 {e.Speed:0} · 击杀积分 {e.Reward}\n\n{mechanism}\n\n触碰水晶只扣 1 点生命；试用不计战绩。";
        UpdateChapterSelection();
        _levelInfo.Text = $"第{Rules.ChapterId}章 · {GameRules.ChapterName(Rules.ChapterId)}\n当前 {Rules.ChapterId}-{Rules.StageInChapter} · {Rules.LevelName}\n难度：{Rules.DifficultyName} · 本关 {GameRules.BossWaves(SelectedLevel)} 波首领\n\n清波三选一，增益可叠加并延续本章；单独开始本关从零累计。";
        _recordsInfo.Text = $"玩家：{Profile.Username}\n累计出战 {Profile.TotalBattles} · 击杀 {Profile.TotalKills}\n技能释放 {Profile.TotalSkills}\n第{_recordChapter}章 · {GameRules.ChapterName(_recordChapter)}\n\n";
        for (int i = (_recordChapter - 1) * 3 + 1; i <= _recordChapter * 3; i++)
            foreach (DifficultyKind d in Enum.GetValues<DifficultyKind>())
            {
                Profile.Stages.TryGetValue($"{i}:{d}", out StageRecord? r);
                string stars = r == null ? "0 星" : r.RatingVersion == 2 ? $"{r.Stars} 星" : "待重评";
                _recordsInfo.Text += $"{_recordChapter}-{(i - 1) % 3 + 1} / {_baseRules.Difficulties.Single(p => p.Kind == d).Name}：{stars} · {r?.BestScore ?? 0} 分\n";
            }
        _recordsInfo.Text += "\n网未受伤 3 星；受伤未破 2 星；破网守住 1 星。\n水晶归零失败；退出当局保存已获积分，不授星。";
        _memoryInfo.Text = $"模拟片段 {Profile.MemoryTrack + 1} / 2 · {(Profile.MemoryPlaying ? "播放中" : "已暂停")}\n{MemoryPosition:00.0} / 30.0 秒 · 循环播放\n展示：{Rules.Heroes.Single(h => h.Kind == Profile.DisplayHero).Name}";
        _memoryPlay.Text = Profile.MemoryPlaying ? "暂停演示" : "继续演示";
    }
}
