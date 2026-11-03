using Godot;
using FruitDefense.Core;

namespace FruitDefense.Presentation;

public partial class SoundEffects : Node
{
    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[6];
    private readonly Dictionary<CombatEventKind, AudioStreamWav> _sounds = [];
    private int _voice;
    private ulong _lastHit;

    public override void _Ready()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new AudioStreamPlayer { VolumeDb = -24 };
            AddChild(_voices[i]);
        }
        _sounds[CombatEventKind.Shot] = Tone(600, .075f, .12f, true);
        _sounds[CombatEventKind.Hit] = Tone(260, .065f, .45f, true);
        _sounds[CombatEventKind.Kill] = Tone(880, .11f, .06f, false);
        _sounds[CombatEventKind.Splash] = Tone(110, .22f, .7f, true);
        _sounds[CombatEventKind.CoreHit] = Tone(150, .28f, .12f, true);
        _sounds[CombatEventKind.BarrierBroken] = Tone(90, .35f, .65f, true);
        _sounds[CombatEventKind.WaveStarted] = Tone(520, .21f, 0, false);
        _sounds[CombatEventKind.Victory] = Tone(740, .5f, 0, false);
        _sounds[CombatEventKind.Defeat] = Tone(190, .5f, .05f, true);
    }

    public void PlayEvent(CombatEventKind kind)
    {
        if (!_sounds.TryGetValue(kind, out AudioStreamWav? sound)) return;
        ulong now = Time.GetTicksMsec();
        if (kind == CombatEventKind.Hit)
        {
            if (now - _lastHit < 45) return;
            _lastHit = now;
        }
        AudioStreamPlayer voice = _voices[_voice++ % _voices.Length];
        voice.Stream = sound;
        voice.Play();
    }

    public void StopAll()
    {
        foreach (AudioStreamPlayer? voice in _voices) voice?.Stop();
    }

    private static AudioStreamWav Tone(float frequency, float duration, float noise, bool falling)
    {
        const int rate = 22050;
        int samples = (int)(duration * rate);
        byte[] pcm = new byte[samples * 2];
        var random = new Random(71);
        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)samples;
            float pitch = frequency * (falling ? 1.3f - t * .8f : .85f + t * .4f);
            phase += Math.Tau * pitch / rate;
            // 短暂平滑起音后连续衰减，首尾均回到零，避免截断产生爆音。
            float envelope = MathF.Min(1, t / .04f) * MathF.Pow(1 - t, 1.6f);
            float value = ((float)Math.Sin(phase) * (1 - noise) + ((float)random.NextDouble() * 2 - 1) * noise) * envelope;
            short sample = (short)(Math.Clamp(value, -1, 1) * 18000);
            pcm[i * 2] = (byte)(sample & 255);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = pcm };
    }
}
