using FruitDefense.Core;

namespace FruitDefense.Progression;

public enum UpgradeKind { Speed10, Speed20, DoubleAmmo, Damage20, FlatDamage5, Critical10, ChainLightning, BurningRounds }

public sealed record UpgradeCard(UpgradeKind Kind, string Name, string Description);

// 章节内的加成独立保存，避免直接修改共享英雄配置或把加成带到训练场。
public sealed class UpgradeState
{
    public static readonly UpgradeCard[] Cards =
    [
        new(UpgradeKind.Speed10, "敏捷装填", "全队攻速 +10%（可叠加）"),
        new(UpgradeKind.Speed20, "极速装填", "全队攻速 +20%（可叠加）"),
        new(UpgradeKind.DoubleAmmo, "双倍弹药", "每次弹药 ×2，可升至 2 / 4 / 8 发"),
        new(UpgradeKind.Damage20, "火力强化", "普通攻击及技能伤害 +20%（可叠加）"),
        new(UpgradeKind.FlatDamage5, "锋利弹头", "每发直击与溅射基础伤害 +5（可叠加）"),
        new(UpgradeKind.Critical10, "精准打击", "暴击率 +10%，暴击造成 2 倍伤害"),
        new(UpgradeKind.ChainLightning, "连锁闪电", "每发首次命中向附近另一个敌人传递 18 点伤害"),
        new(UpgradeKind.BurningRounds, "燃烧弹药", "命中附加灼烧，每秒 8 点，持续 3 秒；重复命中刷新")
    ];
    public Dictionary<UpgradeKind, int> Stacks { get; set; } = [];
    public int Count(UpgradeKind kind) => Stacks.GetValueOrDefault(kind);
    public float AttackSpeedBonus => Count(UpgradeKind.Speed10) * .1f + Count(UpgradeKind.Speed20) * .2f;
    public float DamageBonus => Count(UpgradeKind.Damage20) * .2f;
    public float FlatDamage => Count(UpgradeKind.FlatDamage5) * 5;
    public int AmmoCount => 1 << Math.Clamp(Count(UpgradeKind.DoubleAmmo), 0, 3);
    public float CriticalChance => Math.Clamp(Count(UpgradeKind.Critical10) * .1f, 0, 1);
    public bool CanAdd(UpgradeKind kind) => Enum.IsDefined(kind) &&
        (kind != UpgradeKind.DoubleAmmo || Count(kind) < 3) &&
        (kind != UpgradeKind.Critical10 || Count(kind) < 10) &&
        (kind is not (UpgradeKind.ChainLightning or UpgradeKind.BurningRounds) || Count(kind) == 0);
    public bool Add(UpgradeKind kind)
    {
        if (!CanAdd(kind)) return false;
        Stacks[kind] = Count(kind) + 1;
        return true;
    }
    public UpgradeState Clone() => new() { Stacks = new(Stacks) };
    public bool IsValid => Stacks != null && Stacks.All(p => Enum.IsDefined(p.Key) && p.Value is >= 1 and <= 100
        && (p.Key != UpgradeKind.DoubleAmmo || p.Value <= 3)
        && (p.Key != UpgradeKind.Critical10 || p.Value <= 10)
        && (p.Key is not (UpgradeKind.ChainLightning or UpgradeKind.BurningRounds) || p.Value == 1));
    public string Summary => $"攻速 +{AttackSpeedBonus:P0} · 伤害 +{DamageBonus:P0} · 弹头 +{FlatDamage:0}\n弹药 ×{AmmoCount} · 暴击 {CriticalChance:P0}" +
        (Count(UpgradeKind.ChainLightning) > 0 ? " · 闪电" : "") + (Count(UpgradeKind.BurningRounds) > 0 ? " · 灼烧" : "");
}

public sealed class ChapterCheckpoint
{
    public int LevelId { get; set; } = 1;
    public DifficultyKind Difficulty { get; set; }
    public UpgradeState Upgrades { get; set; } = new();
    public bool IsValid => LevelId is >= 1 and <= 9 && Enum.IsDefined(Difficulty) && Upgrades != null && Upgrades.IsValid;
}
