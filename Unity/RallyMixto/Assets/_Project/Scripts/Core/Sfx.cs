using System;
using System.Collections.Generic;
using UnityEngine;

namespace RallyMixto
{
    // sonidos sintetizados al iniciar (sin archivos de audio)
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }
        const int Rate = 44100;

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource[] pool;
        AudioSource engine, music;
        int next;
        public float engineLevel;

        void Awake()
        {
            I = this;
            pool = new AudioSource[8];
            for (int i = 0; i < pool.Length; i++) pool[i] = gameObject.AddComponent<AudioSource>();

            clips["click"] = Make(0.05f, (t, d) => Sin(1300, t) * Env(t, d, 0.002f) * 0.5f);
            clips["hover"] = Make(0.03f, (t, d) => Sin(2100, t) * Env(t, d, 0.001f) * 0.18f);
            clips["back"] = Make(0.08f, (t, d) => Sin(Mathf.Lerp(900, 500, t / d), t) * Env(t, d, 0.003f) * 0.45f);
            clips["collect"] = Make(0.26f, (t, d) => { float f = t < 0.07f ? 880 : t < 0.14f ? 1320 : 1760; return (Sin(f, t) * 0.6f + Sq(f, t) * 0.12f) * Env(t, d, 0.003f) * 0.55f; });
            clips["boost"] = Make(0.6f, (t, d) => (Saw(Mathf.Lerp(180, 900, t / d), t) * 0.35f + Noise() * 0.15f) * Env(t, d, 0.02f) * 0.5f);
            clips["oil"] = Make(0.5f, (t, d) => Sin(150 + Mathf.Sin(t * 40) * 40, t) * Env(t, d, 0.01f) * 0.6f);
            clips["hit"] = Make(0.35f, (t, d) => (Sin(Mathf.Lerp(120, 45, t / d), t) * 0.8f + Noise() * Mathf.Exp(-t * 30) * 0.6f) * Env(t, d, 0.001f) * 0.8f);
            clips["tick"] = Make(0.06f, (t, d) => Sin(990, t) * Env(t, d, 0.002f) * 0.4f);
            clips["beep"] = Make(0.16f, (t, d) => (Sin(880, t) * 0.7f + Sq(880, t) * 0.1f) * Env(t, d, 0.004f) * 0.5f);
            clips["go"] = Make(0.45f, (t, d) => (Sin(1760, t) * 0.6f + Sin(880, t) * 0.3f) * Env(t, d, 0.004f) * 0.55f);
            clips["win"] = Make(0.9f, (t, d) => { int k = Mathf.Min(3, (int)(t / 0.15f)); float f = new[] { 523f, 659f, 784f, 1046f }[k]; return (Sin(f, t) * 0.5f + Sq(f, t) * 0.08f) * Env(t, d, 0.004f) * 0.55f; });
            clips["step"] = Make(0.12f, (t, d) => Sin(660, t) * Env(t, d, 0.003f) * 0.4f);
            clips["error"] = Make(0.3f, (t, d) => Sq(t < 0.14f ? 330 : 220, t) * Env(t, d, 0.004f) * 0.18f);
            clips["connect"] = Make(0.4f, (t, d) => { float f = t < 0.12f ? 660 : t < 0.24f ? 990 : 1320; return Sin(f, t) * Env(t, d, 0.003f) * 0.45f; });

            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = MakeLoop(1f, t => Saw(55, t) * 0.35f + Saw(110, t) * 0.15f + Sin(27.5f, t) * 0.3f);
            engine.loop = true;
            engine.volume = 0;
            engine.Play();

            music = gameObject.AddComponent<AudioSource>();
            music.clip = MakeLoop(8f, Pad);
            music.loop = true;
            music.volume = 0.22f;
            music.Play();
        }

        public void Play(string name, float vol = 1f, float pitch = 1f)
        {
            if (!clips.TryGetValue(name, out var c)) return;
            var s = pool[next++ % pool.Length];
            s.pitch = pitch;
            s.PlayOneShot(c, vol);
        }

        public void Music(float target) { musicTarget = target; }
        float musicTarget = 0.22f;

        void Update()
        {
            float lvl = Mathf.Clamp01(engineLevel);
            engine.volume = Mathf.Lerp(engine.volume, lvl > 0.01f ? 0.08f + lvl * 0.22f : 0f, Time.unscaledDeltaTime * 6f);
            engine.pitch = Mathf.Lerp(engine.pitch, 0.7f + lvl * 1.1f, Time.unscaledDeltaTime * 5f);
            music.volume = Mathf.MoveTowards(music.volume, musicTarget, Time.unscaledDeltaTime * 0.3f);
        }

        static float Sin(float f, float t) => Mathf.Sin(2 * Mathf.PI * f * t);
        static float Sq(float f, float t) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t));
        static float Saw(float f, float t) => 2f * (t * f - Mathf.Floor(t * f + 0.5f));
        static readonly System.Random rng = new System.Random(7);
        static float Noise() => (float)(rng.NextDouble() * 2 - 1);
        static float Env(float t, float d, float a) => Mathf.Clamp01(t / a) * Mathf.Pow(1 - Mathf.Clamp01(t / d), 2);

        // acorde suave de fondo (la menor)
        static float Pad(float t)
        {
            float lfo = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * t / 8f);
            float chordA = Sin(110, t) + Sin(164.8f, t) * 0.7f + Sin(220, t) * 0.5f + Sin(261.6f, t) * 0.35f;
            float chordB = Sin(98, t) + Sin(146.8f, t) * 0.7f + Sin(196, t) * 0.5f + Sin(246.9f, t) * 0.35f;
            float mix = Mathf.SmoothStep(0, 1, Mathf.PingPong(t / 4f, 1));
            return Mathf.Lerp(chordA, chordB, mix) * 0.12f * lfo;
        }

        static AudioClip Make(float dur, Func<float, float, float> f)
        {
            int n = (int)(dur * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, dur), -1, 1);
            var c = AudioClip.Create("sfx", n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip MakeLoop(float dur, Func<float, float> f)
        {
            int n = (int)(dur * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1, 1);
            var c = AudioClip.Create("loop", n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }
    }
}
