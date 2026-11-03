using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using FruitDefense.Core;
using FruitDefense.Inputs;
using FruitDefense.Presentation;
using FruitDefense.UI;
using FruitDefense.Progression;
using NVector2 = System.Numerics.Vector2;

namespace FruitDefense;

public enum MenuPage { Home, None, Pause, Settings, Result, Profile, Heroes, Enemies, Levels, Records, Memory, Reward }

// 游戏入口连接战斗与触屏输入；主页功能放在 HomeScreens.cs，正式美术待接入。
public partial class GameController : Node2D
{
    public GameRules Rules { get; private set; } = null!;
    public BattleWorld World { get; private set; } = null!;
    public DifficultyKind Difficulty { get; private set; } = DifficultyKind.Easy;
    public HeroKind Selected { get; private set; } = HeroKind.Standard;
    public MenuPage CurrentMenu { get; private set; } = MenuPage.Home;
    public AimController Aim { get; } = new();
    public Font Font { get; private set; } = null!;
    public Font BoldFont { get; private set; } = null!;
    public float AnimationTime { get; private set; }
    public bool SoundEnabled { get; private set; } = true;
    public bool BattleVisible => World.State != BattleState.Ready;
    private GameRules _baseRules = null!;
    private BattleCanvas _canvas = null!;
    private SoundEffects _sounds = null!;
    private Control _ui = null!, _hud = null!, _home = null!, _modal = null!;
    private Panel _pausePanel = null!, _settingsPanel = null!, _resultPanel = null!;
    private Label _score = null!, _invasion = null!, _wave = null!, _next = null!;
    private Label _resultDetails = null!;
    private Button _nextStage = null!;
    private Label _selectedName = null!, _kills = null!, _time = null!, _resultTitle = null!;
    private Button _sound = null!;
    private readonly List<HeroCard> _cards = [];
    private readonly List<Button> _difficultyButtons = [];
    private MenuPage _settingsReturn;
    private float _uiTimer;
    private int _aimFinger = -1;
    private string? _capturePath;
    private float _captureTimer;
    private bool _preview;
    public PlayerProfile Profile { get; private set; } = null!;
    public ProfileStore SaveStore { get; private set; } = null!;
    public int SelectedLevel { get; private set; } = 1;
    public bool IsTraining { get; private set; }
    public float ChargeProgress => Math.Clamp(_chargeTime / .7f, 0, 1);
    private float _chargeTime;
    private bool _battleRecorded;
    private EnemyKind? _trainingEnemy;

    public override void _Ready()
    {
        GetTree().QuitOnGoBack = false;
        string? integrationReport = null;
        string savePath = ProjectSettings.GlobalizePath("user://player-profile.json");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture=")) _capturePath = arg[10..];
            if (arg == "--preview") _preview = true;
            if (arg.StartsWith("--integration=")) integrationReport = arg[14..];
        }
        if (integrationReport != null)
            savePath = Path.Combine(Path.GetDirectoryName(integrationReport)!, "test-profile-" + Guid.NewGuid().ToString("N") + ".json");
        SaveStore = new(savePath);
        Profile = SaveStore.Load();
        SoundEnabled = Profile.SoundEnabled;
        Difficulty = Profile.Difficulty;
        SelectedLevel = Profile.SelectedLevel;
        using var file = Godot.FileAccess.Open("res://config/prototype.json", Godot.FileAccess.ModeFlags.Read);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        _baseRules = JsonSerializer.Deserialize<GameRules>(file.GetAsText(), options)!;
        _baseRules.Validate();
        Rules = _baseRules.ForDifficulty(Difficulty).ForLevel(SelectedLevel);
        Font = new SystemFont { FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC"] };
        BoldFont = new SystemFont { FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC"], FontWeight = 700 };
        World = new(Rules);
        _canvas = new BattleCanvas { Controller = this };
        AddChild(_canvas);
        _sounds = new SoundEffects();
        AddChild(_sounds);
        BuildInterface();
        ResetBattle(false);
        if (_preview)
        {
            ResetBattle(true);
            foreach (var p in new NVector2[] { new(200, 480), new(360, 610), new(520, 740) })
                World.SpawnEnemy(p);
            World.DamageEnemy(World.Enemies[1], 20, HeroKind.Standard);
            SelectHero(HeroKind.Sniper);
            Aim.Begin(GameRules.LaunchPoint);
            Aim.Move(GameRules.LaunchPoint + new NVector2(18, 110));
        }
        if (integrationReport != null)
        {
            _capturePath = null;
            _ = PrototypeChecks.Run(this, integrationReport);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        World.Update((float)delta);
        if (CanAim && Aim.Valid && Aim.Power >= .8f && World.Heroes[Selected].SkillReady)
            _chargeTime += (float)delta;
        else _chargeTime = 0;
        if (IsTraining && World.State == BattleState.Running && World.Enemies.Count == 0) SpawnTrainingTargets();
    }
    public override void _ExitTree() => RecordBattle();
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (World.State != BattleState.Paused)
        {
            AnimationTime += dt;
            _canvas.AdvanceEffects(dt);
        }
        AdvanceMemory(dt);
        _canvas.QueueRedraw();
        _uiTimer -= dt;
        if (_uiTimer <= 0) { UpdateInterface(); _uiTimer = .05f; }
        if (_capturePath != null && (_captureTimer += dt) > 2.4f)
        {
            string path = _capturePath;
            _capturePath = null;
            _ = CaptureFrame(path);
        }
    }

    private bool CanAim => CurrentMenu == MenuPage.None && World.State == BattleState.Running;
    private bool BeginAim(Vector2 point)
    {
        if (!CanAim || !World.Heroes[Selected].Ready) return false;
        return Aim.Begin(new(point.X, point.Y));
    }
    private void CancelAim() { Aim.Cancel(); _aimFinger = -1; _chargeTime = 0; }
    private void MoveAim(NVector2 point)
    {
        Aim.Move(point);
        if (Aim.Power < .8f) _chargeTime = 0;
    }
    private void ReleaseAim(Vector2 point)
    {
        MoveAim(new(point.X, point.Y));
        if (Aim.Valid && CanAim) World.TryShoot(Selected, Aim.Direction, Aim.Power, ChargeProgress >= 1 && Aim.Power >= .8f);
        CancelAim();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_preview) return;
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode is Key.Space or Key.Escape)
            {
                if (Aim.Dragging) CancelAim();
                else if (CurrentMenu == MenuPage.Settings) CloseSettings();
                else if (CurrentMenu is MenuPage.None or MenuPage.Pause) TogglePause();
                else if (CurrentMenu != MenuPage.Home) OpenHub(MenuPage.Home);
            }
            else if (CanAim)
            {
                if (key.Keycode == Key.Key1) SelectHero(HeroKind.Standard);
                if (key.Keycode == Key.Key2) SelectHero(HeroKind.Sniper);
                if (key.Keycode == Key.Key3) SelectHero(HeroKind.Shotgun);
                if (key.Keycode == Key.R) ResetBattle(true);
            }
        }
        if (!CanAim) return;
        if (input is InputEventScreenTouch touch && touch.Pressed && !touch.Canceled)
        {
            if (BeginAim(touch.Position))
            {
                _aimFinger = touch.Index;
                GetViewport().SetInputAsHandled();
            }
        }
        else if (input is InputEventMouseButton mouse && mouse.Device != InputEvent.DeviceIdEmulation)
        {
            if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed) BeginAim(mouse.Position);
            else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed) CancelAim();
        }
    }

    // 捕获正在进行的拖拽；松手经过其他控件、第二根手指介入都不会造成误发。
    public override void _Input(InputEvent input)
    {
        if (_preview || !Aim.Dragging) return;
        if (!CanAim) { CancelAim(); return; }
        if (input is InputEventScreenDrag drag && drag.Index == _aimFinger)
            MoveAim(new(drag.Position.X, drag.Position.Y));
        else if (input is InputEventScreenTouch touch)
        {
            if (touch.Index != _aimFinger) { GetViewport().SetInputAsHandled(); return; }
            if (touch.Canceled) CancelAim();
            else if (!touch.Pressed) ReleaseAim(touch.Position);
        }
        else if (_aimFinger >= 0) { GetViewport().SetInputAsHandled(); return; }
        else if (input is InputEventMouseMotion motion)
            MoveAim(new(motion.Position.X, motion.Position.Y));
        else if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed) CancelAim();
            else if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed) ReleaseAim(mouse.Position);
            else return;
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest && World != null)
        {
            if (Aim.Dragging) CancelAim();
            else if (CurrentMenu == MenuPage.Settings) CloseSettings();
            else if (CurrentMenu is MenuPage.None or MenuPage.Pause) TogglePause();
            else if (CurrentMenu != MenuPage.Home) OpenHub(MenuPage.Home);
        }
        if (what == NotificationWMWindowFocusOut) CancelAim();
        if (what == NotificationApplicationPaused && World != null && World.State == BattleState.Running)
            TogglePause();
    }

    public void SelectHero(HeroKind kind)
    {
        CancelAim();
        Selected = kind;
        UpdateInterface();
    }
    public void ResetBattle(bool start)
    {
        RecordBattle();
        CancelAim();
        if (!start)
        {
            IsTraining = false;
            _trainingEnemy = null;
            _chapterActive = false;
            _levelStartUpgrades = new();
            Rules = _baseRules.ForDifficulty(Difficulty).ForLevel(SelectedLevel);
        }
        _battleRecorded = false;
        World = new(Rules, start && _chapterActive && !IsTraining ? _levelStartUpgrades : null,
            start && _chapterActive && !IsTraining);
        if (start && _chapterActive && !IsTraining)
        {
            Profile.Checkpoint = new() { LevelId = SelectedLevel, Difficulty = Difficulty, Upgrades = _levelStartUpgrades.Clone() };
            SaveProfile();
        }
        World.EventRaised += OnCombatEvent;
        Selected = HeroKind.Standard;
        _canvas.ClearEffects();
        _sounds.StopAll();
        foreach (HeroCard card in _cards) card.Hero = World.Heroes[card.Hero.Definition.Kind];
        CurrentMenu = start ? MenuPage.None : MenuPage.Home;
        if (start) World.Start();
        UpdateInterface();
    }
    private void OnCombatEvent(CombatEvent e)
    {
        if (e.Kind == CombatEventKind.EnemySpawn && !IsTraining) Profile.SeenEnemies.Add(e.Enemy);
        _canvas.HandleEvent(e);
        if (SoundEnabled) _sounds.PlayEvent(e.Kind);
        if (e.Kind == CombatEventKind.UpgradeDropped)
        {
            CancelAim();
            CurrentMenu = MenuPage.Reward;
            UpdateInterface();
        }
        if (e.Kind is CombatEventKind.Victory or CombatEventKind.Defeat)
        {
            CancelAim();
            RecordBattle();
            CurrentMenu = MenuPage.Result;
            UpdateInterface();
        }
    }
    private void TogglePause()
    {
        if (CurrentMenu is not (MenuPage.None or MenuPage.Pause)) return;
        CancelAim();
        World.TogglePause();
        CurrentMenu = World.State == BattleState.Paused ? MenuPage.Pause : MenuPage.None;
        UpdateInterface();
    }
    private void OpenSettings()
    {
        _settingsReturn = CurrentMenu;
        CurrentMenu = MenuPage.Settings;
        CancelAim();
        UpdateInterface();
    }
    private void CloseSettings() { CurrentMenu = _settingsReturn; UpdateInterface(); }
    private void ChooseDifficulty(DifficultyKind kind)
    {
        if (World.State != BattleState.Ready) return;
        Difficulty = kind;
        Rules = _baseRules.ForDifficulty(kind).ForLevel(SelectedLevel);
        SaveProfile();
        ResetBattle(false);
    }

    private void BuildInterface()
    {
        _ui = new Control { Size = new(720, 1280), MouseFilter = Control.MouseFilterEnum.Ignore,
            Theme = new Theme { DefaultFont = Font, DefaultFontSize = 24 } };
        AddChild(_ui);
        _hud = new Control { Size = _ui.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        _ui.AddChild(_hud);
        _score = Metric("积分", 32, 142);
        _invasion = Metric("次数", 190, 128);
        _wave = Metric("波数", 338, 120);
        _next = Metric("下次", 472, 140);
        var pause = new PauseIconButton { Name = "PauseButton", Position = new(628, 25), Size = new(64, 64),
            TooltipText = "暂停", FocusMode = Control.FocusModeEnum.None };
        pause.Pressed += TogglePause;
        _hud.AddChild(pause);
        _selectedName = LabelAt(_hud, "", new(248, 1144), new(224, 30), 21, true);
        _selectedName.HorizontalAlignment = HorizontalAlignment.Center;
        _kills = LabelAt(_hud, "", new(30, 1248), new(300, 30), 20);
        _time = LabelAt(_hud, "", new(390, 1248), new(300, 30), 20);
        _time.HorizontalAlignment = HorizontalAlignment.Right;
        Vector2[] positions = [new(36, 1116), new(150, 1116), new(580, 1116)];
        for (int i = 0; i < Rules.Heroes.Count; i++)
        {
            HeroKind kind = Rules.Heroes[i].Kind;
            var card = new HeroCard { Position = positions[i], Size = new(96, 104),
                Hero = World.Heroes[kind] };
            card.Pressed += () => { if (CanAim) SelectHero(kind); };
            _hud.AddChild(card);
            _cards.Add(card);
        }

        _home = new Control { Size = _ui.Size, MouseFilter = Control.MouseFilterEnum.Stop };
        _ui.AddChild(_home);
        _home.AddChild(new ColorRect { Size = _ui.Size, Color = new Color("10292b"), MouseFilter = Control.MouseFilterEnum.Ignore });
        BuildHomeScreens();

        // 输入阻挡层透明，不盖住战场；可见内容只有中央小菜单。
        _modal = new Control { Size = _ui.Size, MouseFilter = Control.MouseFilterEnum.Stop };
        _ui.AddChild(_modal);
        _pausePanel = PanelAt(new(180, 485), new(360, 310), "PausePanel");
        ButtonAt(_pausePanel, "继续游戏", new(30, 30), new(300, 66), TogglePause);
        ButtonAt(_pausePanel, "设置", new(30, 122), new(300, 66), OpenSettings);
        ButtonAt(_pausePanel, "退出游戏", new(30, 214), new(300, 66), () => ResetBattle(false));
        _settingsPanel = PanelAt(new(140, 485), new(440, 310), "SettingsPanel");
        LabelAt(_settingsPanel, "设置", new(34, 22), new(372, 42), 30, true);
        _sound = ButtonAt(_settingsPanel, "", new(34, 95), new(372, 66), () =>
        {
            SoundEnabled = !SoundEnabled;
            if (!SoundEnabled) _sounds.StopAll();
            SaveProfile();
            UpdateInterface();
        });
        ButtonAt(_settingsPanel, "返回", new(34, 210), new(372, 66), CloseSettings);
        BuildUpgradeInterface();
        _resultPanel = PanelAt(new(130, 400), new(460, 440), "ResultPanel");
        _resultTitle = LabelAt(_resultPanel, "", new(35, 24), new(390, 54), 36, true);
        _resultTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _resultDetails = LabelAt(_resultPanel, "", new(35, 85), new(390, 90), 24);
        _resultDetails.HorizontalAlignment = HorizontalAlignment.Center;
        _nextStage = ButtonAt(_resultPanel, "下一关 · 保留增益", new(45, 182), new(370, 66), ContinueNextStage);
        ButtonAt(_resultPanel, "再来一次", new(45, 266), new(370, 66), () => ResetBattle(true));
        ButtonAt(_resultPanel, "返回主页", new(45, 350), new(370, 66), () => ResetBattle(false));
    }
    private Label Metric(string caption, float x, float width)
    {
        LabelAt(_hud, caption, new(x, 23), new(width, 25), 18);
        return LabelAt(_hud, "", new(x, 51), new(width, 42), 28, true);
    }
    private void UpdateInterface()
    {
        if (_ui == null) return;
        _hud.Visible = BattleVisible;
        _home.Visible = CurrentMenu == MenuPage.Home || CurrentMenu == MenuPage.Settings && _settingsReturn == MenuPage.Home;
        _modal.Visible = CurrentMenu is MenuPage.Pause or MenuPage.Settings or MenuPage.Result or MenuPage.Reward;
        _pausePanel.Visible = CurrentMenu == MenuPage.Pause;
        _settingsPanel.Visible = CurrentMenu == MenuPage.Settings;
        _resultPanel.Visible = CurrentMenu == MenuPage.Result;
        UpdateHomeScreens();
        UpdateUpgradeInterface();
        _score.Text = $"{World.Score:0000}";
        int invasion = World.BetweenWaves ? World.WaveIndex : World.WaveIndex + 1;
        _invasion.Text = IsTraining ? "试用" : $"第{invasion}波";
        _wave.Text = IsTraining ? "不计战绩" : $"共{Rules.Waves.Count}波";
        _next.Text = World.NextArrivalIn is float seconds ? $"{seconds:0.0}s" : "—";
        _selectedName.Text = World.Heroes[Selected].Definition.Name;
        _selectedName.Modulate = FruitArt.Accent(Selected);
        _kills.Text = $"击杀 {World.Kills}";
        _time.Text = $"战斗 {(int)World.Elapsed / 60:00}:{(int)World.Elapsed % 60:00}";
        _sound.Text = SoundEnabled ? "声音：开" : "声音：关";
        bool won = World.State == BattleState.Victory;
        _resultTitle.Text = won ? Rules.ChapterFinal ? "本章完成！" : "守住了！" : "水晶失守";
        _resultDetails.Text = won ? $"{new string('★', World.Stars)}{new string('☆', 3 - World.Stars)} · {World.Stars} 星\n积分 {World.Score}" : "挑战失败 · 0 星\n可重新挑战本关";
        _nextStage.Visible = won && _chapterActive && !Rules.ChapterFinal;
        foreach (HeroCard card in _cards) card.Selected = card.Hero.Definition.Kind == Selected;
        for (int i = 0; i < _difficultyButtons.Count; i++)
            _difficultyButtons[i].SetPressedNoSignal(_baseRules.Difficulties[i].Kind == Difficulty);
    }
    private Panel PanelAt(Vector2 position, Vector2 size, string name)
    {
        var panel = new Panel { Name = name, Position = position, Size = size, MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Box(new Color("173739"), new Color("6c9480"), 22));
        _modal.AddChild(panel);
        return panel;
    }
    private Label LabelAt(Control parent, string text, Vector2 position, Vector2 size, int fontSize, bool bold = false)
    {
        var label = new Label { Text = text, Position = position, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", bold ? BoldFont : Font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(bold ? "eceddc" : "9fbeb0"));
        parent.AddChild(label);
        return label;
    }
    private Button ButtonAt(Control parent, string text, Vector2 position, Vector2 size, Action action)
    {
        var button = new Button { Text = text, Position = position, Size = size,
            FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.AddThemeFontSizeOverride("font_size", 24);
        button.AddThemeColorOverride("font_color", new Color("e8eedc"));
        button.AddThemeStyleboxOverride("normal", Box(new Color("254740"), new Color("557b67"), 12));
        button.AddThemeStyleboxOverride("hover", Box(new Color("355d4e"), new Color("a7c390"), 12));
        button.AddThemeStyleboxOverride("pressed", Box(new Color("3e644d"), new Color("c4da9a"), 12));
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }
    private static StyleBoxFlat Box(Color fill, Color border, int radius) => new()
    {
        BgColor = fill, BorderColor = border, BorderWidthLeft = 2, BorderWidthRight = 2,
        BorderWidthTop = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius
    };
    private async Task CaptureFrame(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error result = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"CAPTURE_RESULT={result}; STATE={World.State}; PATH={path}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
