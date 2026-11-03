using Godot;
using FruitDefense.Core;
using FruitDefense.Entities;
using NVector2 = System.Numerics.Vector2;

namespace FruitDefense.Presentation;

public partial class BattleCanvas : Node2D
{
    public GameController Controller { get; set; } = null!;
    private readonly List<Particle> _particles = [];
    private readonly List<Ring> _rings = [];
    private readonly List<FloatingText> _texts = [];
    private readonly Random _random = new(42);
    private float _coreFlash, _barrierFlash;
    private BattleWorld World => Controller.World;
    private float TimeNow => Controller.AnimationTime;
    private static Vector2 G(NVector2 p) => new(p.X, p.Y);

    public void ClearEffects() { _particles.Clear(); _rings.Clear(); _texts.Clear(); _coreFlash = _barrierFlash = 0; }
    public void AdvanceEffects(float dt)
    {
        foreach (Particle p in _particles) { p.Life -= dt; p.Position += p.Velocity * dt; p.Velocity += new Vector2(0, 130) * dt; }
        foreach (Ring r in _rings) r.Life -= dt;
        foreach (FloatingText t in _texts) { t.Life -= dt; t.Position += new Vector2(0, -38) * dt; }
        _particles.RemoveAll(p => p.Life <= 0);
        _rings.RemoveAll(r => r.Life <= 0);
        _texts.RemoveAll(t => t.Life <= 0);
        _coreFlash = MathF.Max(0, _coreFlash - dt);
        _barrierFlash = MathF.Max(0, _barrierFlash - dt);
    }
    public void HandleEvent(CombatEvent e)
    {
        Vector2 point = G(e.Position);
        Color accent = FruitArt.Accent(e.Hero);
        switch (e.Kind)
        {
            case CombatEventKind.Skill:
            case CombatEventKind.UpgradeChosen:
                _rings.Add(new(point, 70, accent, .5f));
                _texts.Add(new(point + new Vector2(-60, -75), e.Text, accent, 1.1f));
                break;
            case CombatEventKind.UpgradeDropped:
                _rings.Add(new(point, 90, new Color("f1d991"), .6f));
                break;
            case CombatEventKind.Chain:
                _rings.Add(new(point, 35, new Color("88e8ff"), .28f));
                Burst(point, new Color("88e8ff"), 10, 180, .25f);
                break;
            case CombatEventKind.BossWindup:
            case CombatEventKind.BossInterrupted:
            case CombatEventKind.BossReinforcements:
            case CombatEventKind.RunnerTell:
                _texts.Add(new(point + new Vector2(-55, -70), e.Text, new Color("ffcb8b"), 1.4f));
                break;
            case CombatEventKind.Shot: Burst(point, accent, 6, 90, .25f); break;
            case CombatEventKind.Hit:
                Burst(point, accent, 7, 115, .28f);
                _texts.Add(new(point + new Vector2(18, -18), $"{e.Text}{e.Value:0}", e.Text == "暴击" ? new Color("ffe286") : accent, .65f));
                break;
            case CombatEventKind.Kill:
                Burst(point, new Color("d6887d"), 18, 155, .55f);
                _rings.Add(new(point, 42, new Color("eac182"), .35f));
                break;
            case CombatEventKind.Splash:
                Burst(point, new Color("bedb89"), 30, 260, .6f);
                _rings.Add(new(point, e.Value, new Color("c4df8d"), .45f));
                break;
            case CombatEventKind.BarrierHit: _barrierFlash = .17f; Burst(point, new Color("b5e8a6"), 4, 70, .3f); break;
            case CombatEventKind.BarrierBroken:
                for (int i = 0; i < 8; i++) Burst(new(64 + i * 82, GameRules.BarrierY), new Color("a2cc9d"), 5, 150, .65f);
                break;
            case CombatEventKind.CoreHit:
                _coreFlash = .5f;
                Burst(point, new Color("ef9d8d"), 24, 180, .6f);
                _texts.Add(new(point + new Vector2(45, 0), "−1", new Color("ffb09a"), .9f));
                break;
            case CombatEventKind.Victory:
                for (int i = 0; i < 4; i++) Burst(new(120 + i * 160, 440), new Color("d8e9a4"), 18, 200, 1);
                break;
        }
        if (_particles.Count > 600) _particles.RemoveRange(0, _particles.Count - 600);
    }
    private void Burst(Vector2 point, Color color, int count, float speed, float life)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (float)_random.NextDouble() * MathF.Tau;
            float velocity = speed * (.35f + (float)_random.NextDouble() * .65f);
            _particles.Add(new(point, Vector2.FromAngle(angle) * velocity, color, life, 2 + (float)_random.NextDouble() * 3));
        }
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 720, 1280), new Color("0c2428"));
        if (Controller.World == null || !Controller.BattleVisible) return;
        DrawBackground();
        DrawBarrier();
        DrawCore();
        DrawLauncher();
        foreach (Enemy enemy in World.Enemies) DrawEnemy(enemy);
        foreach (Projectile projectile in World.Projectiles) DrawProjectile(projectile);
        DrawEffects();
        DrawBossStatus();
    }
    private void DrawBackground()
    {
        RoundedRect(new(16, 126, 688, 880), new Color("23483a"), 18);
        DrawRect(new Rect2(32, 142, 656, 842), new Color("294e3d"));
        for (int y = 176; y < 980; y += 72)
        {
            DrawLine(new(45, y), new(675, y), new Color(.63f, .74f, .44f, .06f), 1);
            for (int x = 70; x < 670; x += 62)
                DrawCircle(new(x, y + 32), 1.5f, new Color(.74f, .82f, .5f, .10f));
        }
        for (int i = 0; i < 10; i++)
        {
            float y = 185 + i * 84;
            float sway = MathF.Sin(TimeNow * .7f + i) * 2;
            DrawCircle(new(29 + sway, y), 15, new Color("365e41"));
            DrawCircle(new(691 + sway, y + 22), 15, new Color("365e41"));
        }
        RoundedRect(new(16, 14, 688, 96), new Color("163335"), 18);
        DrawLine(new(24, 1241), new(696, 1241), new Color("2e5042"), 1);
    }
    private void DrawBarrier()
    {
        float y = GameRules.BarrierY;
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? 32 : 688;
            RoundedRect(new(x - 8, y - 24, 16, 48), new Color("729065"), 4);
        }
        if (World.Barrier.Broken)
        {
            DrawLine(new(42, y), new(80, y + 9), new Color("789570"), 3, true);
            DrawLine(new(639, y + 9), new(678, y), new Color("789570"), 3, true);
            return;
        }
        Color wall = new Color("a9cb91").Lerp(Colors.White, _barrierFlash * 4);
        DrawLine(new(42, y - 11), new(678, y - 11), wall, 2, true);
        DrawLine(new(42, y + 11), new(678, y + 11), wall, 2, true);
        for (int x = 44; x < 660; x += 22)
        {
            DrawLine(new(x, y - 10), new(x + 21, y + 10), new Color(wall.R, wall.G, wall.B, .6f), 1, true);
            DrawLine(new(x, y + 10), new(x + 21, y - 10), new Color(wall.R, wall.G, wall.B, .6f), 1, true);
        }
        DrawRect(new Rect2(48, y - 25, 624, 4), new Color("355845"));
        DrawRect(new Rect2(48, y - 25, 624 * World.Barrier.Health / World.Barrier.MaximumHealth, 4), new Color("c3dc99"));
    }
    private void DrawCore()
    {
        Vector2 center = G(GameRules.CorePoint);
        Color glow = _coreFlash > 0 ? new Color("f7a48e") : new Color("7bd9c2");
        DrawCircle(center, 40 + MathF.Sin(TimeNow * 2.5f) * 3, new Color(glow.R, glow.G, glow.B, .07f));
        FruitArt.Ellipse(this, center + new Vector2(0, 23), new(37, 8), new Color("102b2b"));
        Vector2 top = center + new Vector2(0, -29), left = center + new Vector2(-20, -7);
        Vector2 right = center + new Vector2(20, -7), bottom = center + new Vector2(0, 23);
        DrawColoredPolygon([top, left, bottom], new Color("6dc7b6"));
        DrawColoredPolygon([top, right, bottom], new Color("348d90"));
        DrawColoredPolygon([top, center + new Vector2(0, -4), left], new Color("c1f5d8"));
        DrawString(Controller.BoldFont, center + new Vector2(40, 8), $"{World.Core.Lives}/{World.Core.MaximumLives}",
            fontSize: 24, modulate: new Color("c8f1d8"));
    }
    private void DrawEnemy(Enemy enemy)
    {
        Vector2 point = G(enemy.Position);
        if (enemy.BurnRemaining > 0) DrawCircle(point, enemy.CollisionRadius + 9, new Color(1, .4f, .1f, .15f));
        FruitArt.Ellipse(this, point + new Vector2(0, 27), new(27, 7), new Color(0, 0, 0, .18f));
        FruitArt.Enemy(this, point + new Vector2(0, MathF.Sin(TimeNow * 7 + enemy.Id) * 2), TimeNow + enemy.Id, enemy.HurtFlash,
            enemy.Kind == EnemyKind.Boss ? 1.7f : 1);
        if (enemy.Kind != EnemyKind.Regular)
        {
            Color marker = enemy.Kind == EnemyKind.Runner ? new Color("79e5cd") : enemy.Kind == EnemyKind.Armored ? new Color("aebecf") : new Color("ffad78");
            DrawArc(point, enemy.CollisionRadius + 6, 0, MathF.Tau, 32, marker, enemy.Kind == EnemyKind.Armored ? 5 : 2, true);
            CenterText(enemy.Kind == EnemyKind.Runner ? "速" : enemy.Kind == EnemyKind.Armored ? "甲" : "BOSS", point + new Vector2(0, enemy.CollisionRadius + 22), 17, marker);
        }
        float barY = MathF.Max(154, point.Y - 43);
        RoundedRect(new(point.X - 31, barY - 24, 62, 20), new Color("193831"), 4);
        CenterText($"{enemy.Health:0}/{enemy.MaxHealth:0}", new(point.X, barY - 8), 15, new Color("ffe1ce"));
        DrawRect(new Rect2(point.X - 25, barY, 50, 4), new Color("5b4442"));
        DrawRect(new Rect2(point.X - 25, barY, 50 * enemy.Health / enemy.MaxHealth, 4), new Color("edae94"));
    }
    private void DrawProjectile(Projectile projectile)
    {
        Color accent = FruitArt.Accent(projectile.Definition.Kind);
        Vector2[] trail = projectile.Trail.Select(G).ToArray();
        for (int i = 1; i < trail.Length; i++)
            DrawLine(trail[i - 1], trail[i], new Color(accent.R, accent.G, accent.B, .32f * i / trail.Length), 3, true);
        Vector2 point = G(projectile.Position);
        if (projectile.Critical) DrawCircle(point, 13, new Color(1, .85f, .25f, .25f));
        if (projectile.Definition.Kind == HeroKind.Sniper)
            DrawLine(point, point - G(NVector2.Normalize(projectile.Velocity)) * 20, accent, 4, true);
        else DrawCircle(point, projectile.Definition.Kind == HeroKind.Shotgun ? 9 : 6, accent);
        DrawCircle(point, 3, new Color("fbf8dd"));
    }
    private void DrawLauncher()
    {
        Vector2 origin = G(GameRules.LaunchPoint);
        Color accent = FruitArt.Accent(Controller.Selected);
        FruitArt.Ellipse(this, origin + new Vector2(0, 58), new(58, 12), new Color("122e2a"));
        DrawLine(origin + new Vector2(-16, 45), origin + new Vector2(-28, -28), new Color("8d704e"), 10, true);
        DrawLine(origin + new Vector2(16, 45), origin + new Vector2(28, -28), new Color("8d704e"), 10, true);
        FruitArt.Hero(this, origin + new Vector2(0, 35), Controller.Selected, .75f, TimeNow);
        Vector2 ammo = Controller.Aim.Dragging ? G(Controller.Aim.DragPoint) : origin;
        DrawLine(origin + new Vector2(-28, -25), ammo, new Color("dec49c"), 2.5f, true);
        DrawLine(origin + new Vector2(28, -25), ammo, new Color("dec49c"), 2.5f, true);
        DrawCircle(ammo, 15, new Color(accent.R, accent.G, accent.B, .13f));
        DrawCircle(ammo, 9, World.Heroes[Controller.Selected].Ready ? accent : new Color("57736b"));
        if (Controller.Aim.Dragging && World.Heroes[Controller.Selected].SkillReady)
        {
            DrawArc(ammo, 22, -MathF.PI / 2, -MathF.PI / 2 + MathF.Max(.001f, Controller.ChargeProgress) * MathF.Tau, 40, accent, 4, true);
            if (Controller.ChargeProgress >= 1) DrawCircle(ammo, 28, new Color(accent.R, accent.G, accent.B, .2f));
        }
        if (!Controller.Aim.Valid) return;
        Vector2 direction = G(Controller.Aim.Direction);
        Vector2 arrow = origin + direction * 65;
        DrawLine(origin + direction * 30, arrow, accent, 3, true);
        DrawLine(arrow, arrow - direction.Rotated(.5f) * 14, accent, 3, true);
        DrawLine(arrow, arrow - direction.Rotated(-.5f) * 14, accent, 3, true);
        Vector2 p = origin;
        Vector2 velocity = direction * Controller.Rules.ProjectileSpeed * (.4f + .6f * Controller.Aim.Power);
        float travelled = 0, range = World.Heroes[Controller.Selected].Definition.Range;
        for (int i = 0; i < 32; i++)
        {
            const float step = .035f;
            Vector2 next = p + velocity * step + new Vector2(0, .5f * Controller.Rules.ProjectileGravity * step * step);
            travelled += p.DistanceTo(next);
            if (travelled > range || next.Y < 132 || next.Y > 1238 || next.X < 26 || next.X > 694) break;
            DrawCircle(next, MathF.Max(1.2f, 3 - i * .05f), new Color(accent.R, accent.G, accent.B, .7f - i * .017f));
            p = next;
            velocity += new Vector2(0, Controller.Rules.ProjectileGravity * step);
        }
    }
    private void DrawBossStatus()
    {
        Enemy? boss = World.Enemies.FirstOrDefault(e => e.Alive && e.Kind == EnemyKind.Boss);
        if (boss == null) return;
        RoundedRect(new(66, 138, 588, 88), new Color("193831"), 10);
        CenterText($"{boss.Name}  {boss.Health:0}/{boss.MaxHealth:0}", new(360, 166), 22, new Color("ffd2a2"));
        DrawRect(new Rect2(90, 180, 540, 8), new Color("503b38"));
        DrawRect(new Rect2(90, 180, 540 * boss.Health / boss.MaxHealth, 8), new Color("edaa7d"));
        string text = boss.StaggerRemaining > 0 ? "已打断 · 硬直" : boss.WindupRemaining > 0 ? $"重击 {boss.WindupRemaining:0.0}s · 打断 {boss.WindupDamage:0}/100" : "重击可打断";
        CenterText(text, new(360, 215), 18, new Color("d4e5ba"));
    }
    private void DrawEffects()
    {
        foreach (Ring ring in _rings)
        {
            float progress = 1 - ring.Life / ring.MaximumLife;
            DrawArc(ring.Position, 8 + ring.Radius * progress, 0, MathF.Tau, 48,
                new Color(ring.Color.R, ring.Color.G, ring.Color.B, (1 - progress) * .75f), 2, true);
        }
        foreach (Particle p in _particles)
            DrawCircle(p.Position, p.Size * (.4f + .6f * p.Life / p.MaximumLife),
                new Color(p.Color.R, p.Color.G, p.Color.B, p.Life / p.MaximumLife));
        foreach (FloatingText text in _texts)
            DrawString(Controller.BoldFont, text.Position, text.Text, fontSize: 22,
                modulate: new Color(text.Color.R, text.Color.G, text.Color.B, MathF.Min(1, text.Life * 3)));
    }
    private void CenterText(string text, Vector2 position, int size, Color color)
    {
        float width = Controller.Font.GetStringSize(text, fontSize: size).X;
        DrawString(Controller.Font, position - new Vector2(width / 2, 0), text, fontSize: size, modulate: color);
    }
    private void RoundedRect(Rect2 r, Color color, float radius)
    {
        DrawRect(new Rect2(r.Position.X + radius, r.Position.Y, r.Size.X - 2 * radius, r.Size.Y), color);
        DrawRect(new Rect2(r.Position.X, r.Position.Y + radius, r.Size.X, r.Size.Y - 2 * radius), color);
        foreach (Vector2 p in new[] { r.Position + new Vector2(radius, radius), r.Position + new Vector2(r.Size.X - radius, radius),
            r.Position + new Vector2(radius, r.Size.Y - radius), r.End - new Vector2(radius, radius) })
            DrawCircle(p, radius, color);
    }
    private sealed class Particle(Vector2 point, Vector2 velocity, Color color, float life, float size)
    {
        public Vector2 Position = point, Velocity = velocity;
        public Color Color = color;
        public float Life = life, MaximumLife = life, Size = size;
    }
    private sealed class Ring(Vector2 point, float radius, Color color, float life)
    {
        public Vector2 Position = point;
        public float Radius = radius, Life = life, MaximumLife = life;
        public Color Color = color;
    }
    private sealed class FloatingText(Vector2 point, string text, Color color, float life)
    {
        public Vector2 Position = point;
        public string Text = text;
        public Color Color = color;
        public float Life = life, MaximumLife = life;
    }
}
