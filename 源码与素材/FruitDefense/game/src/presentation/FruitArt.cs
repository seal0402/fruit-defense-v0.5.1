using Godot;
using FruitDefense.Core;

namespace FruitDefense.Presentation;

// 原型用的代码绘制素材。以后可替换为Sprite2D，不影响战斗规则。
public static class FruitArt
{
    public static Color Accent(HeroKind hero) => hero switch
    {
        HeroKind.Standard => new Color("ffbf69"),
        HeroKind.Sniper => new Color("78dce8"),
        _ => new Color("b9dd7b")
    };

    public static void Hero(CanvasItem canvas, Vector2 position, HeroKind kind, float scale = 1, float time = 0)
    {
        canvas.DrawSetTransform(position, MathF.Sin(time * 2) * .025f, Vector2.One * scale);
        Color accent = Accent(kind);
        canvas.DrawCircle(new(0, 5), 33, new Color(0, 0, 0, .18f));
        if (kind == HeroKind.Shotgun)
        {
            var points = new Vector2[32];
            for (int i = 0; i < points.Length; i++)
                points[i] = Vector2.FromAngle(i * MathF.Tau / points.Length) * (i % 2 == 0 ? 37 : 29);
            canvas.DrawColoredPolygon(points, new Color("779c4d"));
            canvas.DrawCircle(Vector2.Zero, 28, accent);
            canvas.DrawCircle(new(6, -6), 18, new Color("cde798"));
        }
        else if (kind == HeroKind.Sniper)
        {
            canvas.DrawCircle(new(-15, 5), 20, new Color("7283c9"));
            canvas.DrawCircle(new(14, 5), 21, new Color("596fac"));
            canvas.DrawCircle(new(0, -10), 24, new Color("8695dc"));
            canvas.DrawArc(new(-3, -12), 14, 3.3f, 4.7f, 14, new Color("b9c6f7"), 3, true);
        }
        else
        {
            canvas.DrawCircle(Vector2.Zero, 32, new Color("dd8b3c"));
            canvas.DrawCircle(new(-2, -4), 29, accent);
            canvas.DrawArc(new(-2, -4), 22, 3.3f, 4.75f, 16, new Color("ffe0a5"), 4, true);
            canvas.DrawCircle(new(21, 8), 2, new Color("e5a154"));
        }
        canvas.DrawLine(new(0, -27), new(3, -43), new Color("9b764d"), 5, true);
        canvas.DrawColoredPolygon([new(2, -35), new(15, -49), new(28, -42), new(12, -31)], new Color("81b884"));
        canvas.DrawCircle(new(-9, 0), 5, new Color("172d32"));
        canvas.DrawCircle(new(10, 0), 5, new Color("172d32"));
        canvas.DrawCircle(new(-10, -2), 1.6f, Colors.White);
        canvas.DrawCircle(new(9, -2), 1.6f, Colors.White);
        canvas.DrawArc(new(0, 7), 5, 0, MathF.PI, 10, new Color("4f432d"), 2, true);
        canvas.DrawSetTransform(Vector2.Zero);
    }

    public static void Enemy(CanvasItem canvas, Vector2 position, float time, float flash, float scale = 1)
    {
        canvas.DrawSetTransform(position, MathF.Sin(time * 8) * .05f, Vector2.One * scale);
        Color body = new Color("c86574").Lerp(Colors.White, Math.Clamp(flash * 6, 0, .9f));
        canvas.DrawCircle(new(0, 7), 25, new Color("824451"));
        canvas.DrawCircle(new(0, -1), 25, body);
        canvas.DrawColoredPolygon([new(-20, -14), new(-22, -35), new(-5, -24)], new Color("a34c5e"));
        canvas.DrawColoredPolygon([new(20, -14), new(22, -35), new(5, -24)], new Color("a34c5e"));
        canvas.DrawRect(new Rect2(-23, -6, 46, 16), new Color("402d40"));
        canvas.DrawCircle(new(-9, 1), 5, new Color("ffdfad"));
        canvas.DrawCircle(new(9, 1), 5, new Color("ffdfad"));
        canvas.DrawCircle(new(-9, 1), 2, new Color("402d40"));
        canvas.DrawCircle(new(9, 1), 2, new Color("402d40"));
        canvas.DrawLine(new(-7, 17), new(7, 17), new Color("653143"), 3, true);
        canvas.DrawSetTransform(Vector2.Zero);
    }

    public static void Ellipse(CanvasItem canvas, Vector2 center, Vector2 radius, Color color)
    {
        canvas.DrawSetTransform(center, 0, radius);
        canvas.DrawCircle(Vector2.Zero, 1, color);
        canvas.DrawSetTransform(Vector2.Zero);
    }
}
