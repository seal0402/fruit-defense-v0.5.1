using System.Text.Json;
using FruitDefense.Core;

namespace FruitDefense.Progression;

// 存档与引擎解耦；只存玩家数据，不存窗口、节点或素材路径。
public sealed class PlayerProfile
{
    public int Version { get; set; } = 1;
    public string Username { get; set; } = "果园守护者";
    public HeroKind DisplayHero { get; set; }
    public bool SoundEnabled { get; set; } = true;
    public ChapterCheckpoint? Checkpoint { get; set; }
    public int SelectedLevel { get; set; } = 1;
    public DifficultyKind Difficulty { get; set; }
    public int TotalBattles { get; set; }
    public int TotalKills { get; set; }
    public int TotalSkills { get; set; }
    public int MemoryTrack { get; set; }
    public bool MemoryPlaying { get; set; } = true;
    public HashSet<EnemyKind> SeenEnemies { get; set; } = [];
    public Dictionary<string, StageRecord> Stages { get; set; } = [];
    public bool SetUsername(string value)
    {
        string name = value.Trim();
        if (name.Length is < 1 or > 12 || name.Any(char.IsControl)) return false;
        Username = name;
        return true;
    }
    public void Record(BattleWorld world, DifficultyKind difficulty)
    {
        TotalBattles++;
        TotalKills += world.Kills;
        TotalSkills += world.Heroes.Values.Sum(h => h.SkillsUsed);
        string key = $"{world.Rules.LevelId}:{difficulty}";
        if (!Stages.TryGetValue(key, out StageRecord? best)) Stages[key] = best = new();
        best.BestScore = Math.Max(best.BestScore, world.Score);
        if (world.State == BattleState.Victory)
        {
            best.Clears++;
            if (best.RatingVersion != 2)
            {
                best.LegacyStars = best.Stars;
                best.Stars = 0;
                best.RatingVersion = 2;
            }
            best.Stars = Math.Max(best.Stars, world.Stars);
        }
    }
}
public sealed class StageRecord
{
    public int RatingVersion { get; set; }
    public int LegacyStars { get; set; }
    public int BestScore { get; set; }
    public int Stars { get; set; }
    public int Clears { get; set; }
}
public sealed class ProfileStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string Path { get; } = path;
    public string? Notice { get; private set; }
    public PlayerProfile Load()
    {
        if (!File.Exists(Path)) return new();
        try
        {
            PlayerProfile data = JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(Path)) ?? throw new JsonException();
            if (data.Version != 1 || !data.SetUsername(data.Username) || data.SelectedLevel is < 1 or > 9
                || !Enum.IsDefined(data.DisplayHero) || !Enum.IsDefined(data.Difficulty)
                || data.Checkpoint != null && !data.Checkpoint.IsValid
                || data.Stages == null || data.SeenEnemies == null || data.TotalBattles < 0
                || data.TotalKills < 0 || data.TotalSkills < 0 || data.MemoryTrack is < 0 or > 1
                || data.Stages.Values.Any(s => s == null || s.Stars is < 0 or > 3 || s.Clears < 0 || s.BestScore < 0))
                throw new JsonException("存档数据无效");
            return data;
        }
        catch (Exception e) when (e is JsonException or IOException or ArgumentException or NullReferenceException)
        {
            try { File.Copy(Path, Path + ".damaged-" + DateTime.UtcNow.Ticks, true); } catch (IOException) { }
            Notice = "原存档无法读取，已尝试备份并启用新档。";
            return new();
        }
    }
    public bool Save(PlayerProfile data)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            File.WriteAllText(Path + ".tmp", JsonSerializer.Serialize(data, Options));
            File.Move(Path + ".tmp", Path, true);
            Notice = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice = "存档写入失败，本次数据仍保留在内存中。";
            return false;
        }
    }
}
