using Godot;
using FruitDefense.Core;
using FruitDefense.Progression;

namespace FruitDefense;

public partial class GameController
{
    private bool _chapterActive;
    private UpgradeState _levelStartUpgrades = new();
    private int _recordChapter = 1;
    private readonly List<Button> _chapterButtons = [], _stageButtons = [], _rewardButtons = [];
    private Button _continueChapter = null!;
    private Panel _rewardPanel = null!;
    private Panel _buffPanel = null!;
    private Label _buffDetails = null!;

    private void BuildChapterSelection(Control page)
    {
        for (int i = 1; i <= 3; i++)
        {
            int chapter = i;
            var button = ButtonAt(page, $"第{i}章", new(42 + (i - 1) * 218, 150), new(200, 60), () => SelectStage((chapter - 1) * 3 + 1));
            button.ToggleMode = true;
            _chapterButtons.Add(button);
        }
        for (int i = 1; i <= 3; i++)
        {
            int stage = i;
            var button = ButtonAt(page, "", new(42, 260 + (i - 1) * 100), new(636, 72),
                () => SelectStage((SelectedLevel - 1) / 3 * 3 + stage));
            button.ToggleMode = true;
            _stageButtons.Add(button);
        }
    }
    private void SelectStage(int level)
    {
        SelectedLevel = level;
        Rules = _baseRules.ForDifficulty(Difficulty).ForLevel(level);
        SaveProfile();
        UpdateInterface();
    }
    private void BuildChapterHomeButton()
    {
        _continueChapter = ButtonAt(_home, "继续章节", new(180, 1030), new(360, 64), ResumeChapter);
    }
    private void BuildRecordChapters(Control page)
    {
        for (int i = 1; i <= 3; i++)
        {
            int chapter = i;
            ButtonAt(page, $"第{i}章战绩", new(42 + (i - 1) * 218, 156), new(200, 60), () => { _recordChapter = chapter; UpdateInterface(); });
        }
    }
    private void UpdateChapterSelection()
    {
        for (int i = 0; i < _chapterButtons.Count; i++) _chapterButtons[i].SetPressedNoSignal(Rules.ChapterId == i + 1);
        for (int i = 0; i < _stageButtons.Count; i++)
        {
            int id = (Rules.ChapterId - 1) * 3 + i + 1;
            _stageButtons[i].Text = GameRules.StageButton(id);
            _stageButtons[i].SetPressedNoSignal(SelectedLevel == id);
        }
        _continueChapter.Visible = Profile.Checkpoint != null;
    }
    private void ResumeChapter()
    {
        ChapterCheckpoint? checkpoint = Profile.Checkpoint;
        if (checkpoint == null || !checkpoint.IsValid) return;
        SelectedLevel = checkpoint.LevelId;
        Difficulty = checkpoint.Difficulty;
        _levelStartUpgrades = checkpoint.Upgrades.Clone();
        _chapterActive = true;
        IsTraining = false;
        _trainingEnemy = null;
        Rules = _baseRules.ForDifficulty(Difficulty).ForLevel(SelectedLevel);
        ResetBattle(true);
    }
    private void ContinueNextStage()
    {
        if (World.State != BattleState.Victory || !_chapterActive || Rules.ChapterFinal) return;
        _levelStartUpgrades = World.Upgrades.Clone();
        SelectedLevel++;
        Rules = _baseRules.ForDifficulty(Difficulty).ForLevel(SelectedLevel);
        ResetBattle(true);
    }
    private void BuildUpgradeInterface()
    {
        _rewardPanel = PanelAt(new(70, 330), new(580, 600), "UpgradePanel");
        LabelAt(_rewardPanel, "本波掉落 · 选择一张增益卡", new(28, 24), new(524, 44), 28, true);
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            Button button = ButtonAt(_rewardPanel, "", new(28, 92 + i * 132), new(524, 112), () =>
            {
                if (index >= World.OfferedUpgrades.Count) return;
                if (World.SelectUpgrade(World.OfferedUpgrades[index]))
                {
                    CurrentMenu = MenuPage.None;
                    UpdateInterface();
                }
            });
            button.Name = $"UpgradeChoice{index}";
            button.AddThemeFontSizeOverride("font_size", 20);
            _rewardButtons.Add(button);
        }
        ButtonAt(_rewardPanel, "退出本章", new(140, 514), new(300, 56), () => ResetBattle(false));
        _buffPanel = PanelAt(new(70, 150), new(580, 126), "ChapterBuffPanel");
        _buffDetails = LabelAt(_buffPanel, "", new(20, 14), new(540, 100), 21);
        _buffDetails.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
    private void UpdateUpgradeInterface()
    {
        if (_rewardPanel == null) return;
        _rewardPanel.Visible = CurrentMenu == MenuPage.Reward;
        _buffPanel.Visible = CurrentMenu is MenuPage.Reward or MenuPage.Result;
        _buffDetails.Text = $"本章增益\n{World.Upgrades.Summary}";
        for (int i = 0; i < _rewardButtons.Count; i++)
        {
            _rewardButtons[i].Visible = i < World.OfferedUpgrades.Count;
            if (i >= World.OfferedUpgrades.Count) continue;
            UpgradeCard card = UpgradeState.Cards.Single(c => c.Kind == World.OfferedUpgrades[i]);
            string description = card.Description;
            if (description.Length > 25) description = description.Insert(25, "\n");
            _rewardButtons[i].Text = $"{card.Name} · 已有 {World.Upgrades.Count(card.Kind)} 张\n{description}";
        }
    }
}
