using System;
using System.Collections.Generic;
using UnityEngine;

namespace RallyMixto
{
    // mision manual: recoger celdas en 90 s
    public class Mission : MonoBehaviour
    {
        public enum State { Idle, Countdown, Running, Paused, Finished }

        public const float Duration = 90f;
        public const int SpeedNormal = 70, SpeedBoost = 100, SpeedOil = 35;
        const int CellsActive = 6;

        public State state { get; private set; }
        public float TimeLeft { get; private set; }
        public float Countdown { get; private set; }
        public int Score, Cells, Hits, Boosts, Oils, Best;
        public float BoostT, OilT;
        public bool NewBest;
        public int SpeedNow => state != State.Running ? 0 : BoostT > 0 ? SpeedBoost : OilT > 0 ? SpeedOil : SpeedNormal;

        public event Action<string, string> Popup;
        public event Action Finished;

        Arena arena;
        Twin twin;
        CameraRig cam;
        Hub hub;
        readonly List<GameObject> cells = new List<GameObject>();
        readonly HashSet<int> used = new HashSet<int>();
        int lastCount, lastSpeed = -1, lastTick;
        float ledRestore;

        public void Init(Arena a, Twin t, CameraRig c)
        {
            arena = a;
            twin = t;
            cam = c;
            hub = Hub.I;
            twin.Hit += OnHit;
            twin.Touched += OnTouched;
            Best = PlayerPrefs.GetInt("best", 0);
        }

        public void Begin()
        {
            Clear();
            Score = Cells = Hits = Boosts = Oils = 0;
            BoostT = OilT = 0;
            NewBest = false;
            TimeLeft = Duration;
            Countdown = 3.999f;
            lastCount = 4;
            lastTick = 11;
            lastSpeed = -1;
            arena.missionProps.SetActive(true);
            twin.maxSpeed = 6f;
            twin.maxTurn = 130f;
            twin.Place(arena.startPos + Vector3.up * 0.001f, arena.startYaw);
            for (int i = 0; i < CellsActive; i++) Spawn();
            hub.SetMode(1);
            hub.SetSpeed(0);
            hub.SetLed(Led.Slow, "Preparando");
            hub.SetCarLed(Led.Slow);
            state = State.Countdown;
            cam.mode = CamMode.Chase;
            cam.Snap();
            Time.timeScale = 1;
        }

        public void SetTimeLeft(float t) { TimeLeft = Mathf.Min(TimeLeft, t); }

        public void Pause(bool on)
        {
            if (on && state == State.Running)
            {
                state = State.Paused;
                Time.timeScale = 0;
                hub.SetSpeed(0, "Pausa: carro detenido");
                hub.SetLed(Led.Slow);
            }
            else if (!on && state == State.Paused)
            {
                state = State.Running;
                Time.timeScale = 1;
                hub.SetMode(1);
                lastSpeed = -1;
                hub.SetLed(Led.On);
            }
        }

        public void Abort()
        {
            Time.timeScale = 1;
            state = State.Idle;
            Clear();
            hub.SetSpeed(0);
            hub.SetLed(Led.Off);
            hub.SetCarLed(Led.Off);
            Sfx.I.engineLevel = 0;
        }

        void Clear()
        {
            foreach (var c in cells) if (c) Destroy(c);
            cells.Clear();
            used.Clear();
        }

        void Spawn()
        {
            var free = new List<int>();
            for (int i = 0; i < arena.cellSlots.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (twin && Vector3.Distance(arena.cellSlots[i], twin.transform.position) < 6f) continue;
                free.Add(i);
            }
            if (free.Count == 0) return;
            int slot = free[UnityEngine.Random.Range(0, free.Count)];
            used.Add(slot);
            cells.Add(arena.SpawnCell(arena.cellSlots[slot], slot));
        }

        void Update()
        {
            if (state == State.Idle || state == State.Finished || state == State.Paused) return;
            float dt = Time.deltaTime;

            // animacion de celdas
            foreach (var c in cells)
            {
                if (!c) continue;
                var body = c.transform.GetChild(0);
                body.localRotation = Quaternion.Euler(0, Time.time * 90f, 0);
                body.localPosition = Vector3.up * (0.25f + Mathf.Sin(Time.time * 2.4f + c.GetInstanceID()) * 0.15f);
            }

            if (state == State.Countdown)
            {
                twin.Frozen = true;
                Countdown -= dt;
                int n = Mathf.CeilToInt(Countdown);
                if (n != lastCount)
                {
                    lastCount = n;
                    if (n > 0)
                    {
                        Sfx.I.Play("beep");
                        hub.Tone(880, 120, "Cuenta regresiva " + n);
                        hub.FlashAll("Cuenta regresiva");
                    }
                    else
                    {
                        Sfx.I.Play("go");
                        hub.Tone(1760, 300, "¡Arranca!");
                        hub.SetLed(Led.On, "Misión en curso");
                        hub.SetCarLed(Led.On);
                        state = State.Running;
                        twin.Frozen = false;
                    }
                }
                return;
            }

            // running
            TimeLeft -= dt;
            if (BoostT > 0) { BoostT -= dt; if (BoostT <= 0) hub.SetLed(Led.On, "Fin del turbo"); }
            if (OilT > 0) { OilT -= dt; if (OilT <= 0) hub.SetLed(Led.On, "Fin del aceite"); }
            if (ledRestore > 0) { ledRestore -= dt; if (ledRestore <= 0) hub.SetLed(BoostT > 0 ? Led.Fast : Led.On); }

            int sp = SpeedNow;
            if (sp != lastSpeed)
            {
                if (lastSpeed >= 0) hub.SetSpeed(sp, "Velocidad " + sp + "%");
                else hub.SetSpeed(sp);
                lastSpeed = sp;
            }

            DriveTwin(hub, twin, sp);

            int secs = Mathf.CeilToInt(TimeLeft);
            if (secs <= 10 && secs < lastTick && secs > 0)
            {
                lastTick = secs;
                Sfx.I.Play("tick");
                hub.Tone(990, 60, "Últimos segundos");
            }

            if (TimeLeft <= 0) Finish();
        }

        // el gemelo usa la telemetria del carro o, sin enlace, la misma mezcla del firmware
        public static void DriveTwin(Hub hub, Twin twin, int speed)
        {
            if (hub.CarLink)
            {
                twin.SetMotors(hub.MotA, hub.MotB);
                twin.Braking = hub.CarState == 3;
                twin.Shaking = hub.CarState == 4;
            }
            else
            {
                Twin.Mix(-hub.JoyX, hub.JoyY, out var a, out var b);
                twin.SetMotors(Twin.Limit(a, speed), Twin.Limit(b, speed));
                twin.Braking = false;
            }
            twin.LedPattern = hub.CarLedNow;
            Sfx.I.engineLevel = Mathf.Abs(twin.Speed) / 6f + (Mathf.Abs(twin.TargetA) + Mathf.Abs(twin.TargetB)) / 1400f;
        }

        void Finish()
        {
            state = State.Finished;
            TimeLeft = 0;
            twin.SetMotors(0, 0);
            twin.Frozen = true;
            Sfx.I.engineLevel = 0;
            hub.SetSpeed(0, "Fin: carro detenido");
            hub.Melody(new[] { 523, 659, 784, 1046 }, 160, "Melodía de victoria");
            hub.SetLed(Led.Slow, "Misión terminada");
            hub.SetCarLed(Led.Slow);
            Sfx.I.Play("win");
            if (Score > Best)
            {
                Best = Score;
                NewBest = true;
                PlayerPrefs.SetInt("best", Best);
                PlayerPrefs.Save();
            }
            Finished?.Invoke();
        }

        void OnTouched(Collider c)
        {
            if (state != State.Running) return;
            var p = c.GetComponent<Pickup>();
            if (!p) return;

            switch (p.kind)
            {
                case PickKind.Cell:
                    Cells++;
                    int pts = 100 + (BoostT > 0 ? 50 : 0);
                    Score += pts;
                    arena.Burst(c.transform.position, true);
                    used.Remove(p.slot);
                    cells.Remove(c.gameObject);
                    Destroy(c.gameObject);
                    Spawn();
                    Sfx.I.Play("collect");
                    hub.FlashAll("Celda recogida");
                    hub.Tone(1760, 80, "Celda +" + pts);
                    Popup?.Invoke("+" + pts, "cyan");
                    break;

                case PickKind.Boost:
                    if (BoostT > 1f) return;
                    Boosts++;
                    BoostT = 4f;
                    OilT = 0;
                    Sfx.I.Play("boost");
                    hub.SetLed(Led.Fast, "Turbo: LED rápido");
                    hub.Tone(1200, 250, "Turbo");
                    cam.Shake(0.4f);
                    Popup?.Invoke("TURBO", "amber");
                    break;

                case PickKind.Oil:
                    if (OilT > 1f) return;
                    Oils++;
                    OilT = 3f;
                    BoostT = 0;
                    Sfx.I.Play("oil");
                    hub.Shake(450, "Aceite: vibración 450 ms");
                    hub.SetLed(Led.Slow, "Aceite: LED lento");
                    hub.Tone(220, 300, "Aceite");
                    Popup?.Invoke("ACEITE", "violet");
                    break;
            }
        }

        void OnHit(float impact)
        {
            if (state != State.Running) return;
            Hits++;
            Score = Mathf.Max(0, Score - 50);
            arena.Burst(twin.transform.position + twin.transform.forward * 0.8f, false);
            cam.Shake(Mathf.Clamp(impact / 6f, 0.3f, 1f));
            Sfx.I.Play("hit", Mathf.Clamp01(impact / 5f) * 0.6f + 0.4f);
            hub.Brake(400, "Choque: freno 400 ms");
            hub.Tone(140, 220, "Choque");
            hub.SetLed(Led.Fast);
            ledRestore = 0.6f;
            Popup?.Invoke("CHOQUE −50", "red");
        }
    }
}
