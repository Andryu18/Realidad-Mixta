using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RallyMixto
{
    public enum LinkPhase { Idle, Opening, Handshake, Ready, Failed }

    // patrones de led (igual que el firmware)
    public static class Led
    {
        public const int Off = 0, On = 1, Slow = 2, Fast = 3, Flash = 4;
    }

    // estado del control y canal de retroalimentacion hacia el hardware
    public class Hub : MonoBehaviour
    {
        public static Hub I { get; private set; }

        public ILink Link { get; private set; }
        public LinkPhase Phase { get; private set; }
        public string FailReason { get; private set; }
        public PortKind Kind { get; private set; }
        public bool IsDemo => Link is SimLink;
        public string PortName => Link?.Label ?? "";
        public string Firmware { get; private set; }

        // datos del control
        public int JoyX, JoyY, Mode = 1, MotA, MotB, Step = 255, CarState, Speed;
        public bool CarLink;
        public float RxHz { get; private set; }
        public float LastData { get; private set; }

        public event Action<int> Button;
        public event Action<string, string> Feedback;

        public readonly List<string> RxLog = new List<string>();
        public readonly List<string> TxLog = new List<string>();
        public readonly List<string> SerialLog = new List<string>();
        public int LogVersion { get; private set; }
        public readonly Dictionary<string, int> Stats = new Dictionary<string, int>();

        float tOpen, tPing, tKeep, hzWindow;
        int hzCount;
        bool sawCar, sawLines;

        void Awake() { I = this; }

        public void Connect(PortInfo p)
        {
            Disconnect();
            Kind = p.kind;
            var s = new SerialLink(p.name);
            s.OpenAsync();
            Link = s;
            Phase = LinkPhase.Opening;
            FailReason = null;
            tOpen = Time.unscaledTime;
            sawCar = sawLines = false;
        }

        public void StartDemo()
        {
            Disconnect();
            Kind = PortKind.Unknown;
            Link = new SimLink();
            Phase = LinkPhase.Handshake;
            tOpen = Time.unscaledTime;
        }

        public void Disconnect()
        {
            if (Link != null && Phase == LinkPhase.Ready) { Send("S,100"); Send("L,0"); Send("C,0"); }
            Link?.Close();
            Link = null;
            Phase = LinkPhase.Idle;
            CarLink = false;
        }

        void Fail(string reason)
        {
            FailReason = reason;
            Link?.Close();
            Link = null;
            Phase = LinkPhase.Failed;
        }

        void Update()
        {
            if (Link == null) return;
            if (Link is SimLink sim) sim.Tick(Time.unscaledDeltaTime);
            float now = Time.unscaledTime;

            if (Phase == LinkPhase.Opening)
            {
                var s = (SerialLink)Link;
                if (s.Error != null) { Fail(s.Error == "busy" ? "busy" : "open"); return; }
                if (s.IsOpen) { Phase = LinkPhase.Handshake; tOpen = now; }
                else if (now - tOpen > 12f) { Fail("open"); return; }
            }

            while (Link != null && Link.TryRead(out var line)) Parse(line, now);
            if (Link == null) return;

            if (Link.Error != null && !(Link is SimLink)) { Fail("lost"); return; }

            if (Phase == LinkPhase.Handshake)
            {
                if (now - tPing > 0.4f) { tPing = now; Link.Send("?"); }
                if (now - tOpen > 6f) Fail(sawCar ? "car" : sawLines ? "unknown" : "silent");
            }
            else if (Phase == LinkPhase.Ready)
            {
                if (now - tKeep > 0.5f) { tKeep = now; Link.Send("P"); }
                if (now - LastData > 2.5f) Fail("lost");
            }

            hzWindow += Time.unscaledDeltaTime;
            if (hzWindow >= 1f) { RxHz = hzCount / hzWindow; hzCount = 0; hzWindow = 0; }
        }

        void Parse(string line, float now)
        {
            sawLines = true;
            if (line.StartsWith("Car MAC") || line.StartsWith("esperando") || line.StartsWith("Waiting")) sawCar = true;

            var p = line.Split(',');
            switch (p[0])
            {
                case "H":
                    Firmware = p.Length > 2 ? p[2] : "?";
                    if (Phase == LinkPhase.Handshake) { Phase = LinkPhase.Ready; LastData = now; }
                    break;
                case "D":
                    if (p.Length < 10) return;
                    JoyX = I0(p[1]); JoyY = I0(p[2]); Mode = I0(p[3]);
                    CarLink = p[4] == "1";
                    MotA = I0(p[5]); MotB = I0(p[6]); Step = I0(p[7]); CarState = I0(p[8]); Speed = I0(p[9]);
                    LastData = now;
                    hzCount++;
                    if (Phase == LinkPhase.Handshake) Phase = LinkPhase.Ready;
                    break;
                case "E":
                    if (p.Length > 2 && p[1] == "BTN") { Mode = I0(p[2]); Button?.Invoke(Mode); }
                    Log(RxLog, line);
                    return;
                default:
                    Log(RxLog, line);
                    return;
            }
            if (p[0] != "D" || hzCount % 10 == 0) Log(RxLog, line);
        }

        static int I0(string s) => int.TryParse(s, out var v) ? v : 0;

        void Log(List<string> list, string s)
        {
            var t = DateTime.Now.ToString("HH:mm:ss.f");
            list.Add(t + "  " + s);
            if (list.Count > 40) list.RemoveAt(0);
            // registro combinado para el hud
            SerialLog.Add(t + (list == TxLog ? "  > " : "  < ") + s);
            if (SerialLog.Count > 40) SerialLog.RemoveAt(0);
            LogVersion++;
        }

        void Send(string cmd)
        {
            if (Link == null) return;
            Link.Send(cmd);
            if (cmd != "P") Log(TxLog, cmd);
        }

        void Count(string kind, string label, string cmd)
        {
            Send(cmd);
            Stats.TryGetValue(kind, out var n);
            Stats[kind] = n + 1;
            Feedback?.Invoke(kind, label);
        }

        public bool Ready => Phase == LinkPhase.Ready;

        // retroalimentacion visual
        public void SetLed(int p, string label = null)
        {
            if (!Ready) return;
            Count("LED", label ?? "LED del control", "L," + p);
        }

        int carLed;
        float carFlashEnd;

        // patron que muestra el led del carro (para el gemelo)
        public int CarLedNow => Time.unscaledTime < carFlashEnd ? Led.Flash : carLed;

        public void SetCarLed(int p, string label = null)
        {
            carLed = p;
            if (!Ready) return;
            if (label == null) Send("C," + p);
            else Count("LED", label, "C," + p);
        }

        public void FlashAll(string label)
        {
            if (!Ready) return;
            Count("LED", label, "L,4");
            Send("C,4");
            carFlashEnd = Time.unscaledTime + 1f;
            StartCoroutine(RestoreCarLed());
        }

        IEnumerator RestoreCarLed()
        {
            yield return new WaitForSecondsRealtime(1.05f);
            if (Time.unscaledTime >= carFlashEnd && Ready) Send("C," + carLed);
        }

        // retroalimentacion sonora
        public void Tone(int freq, int ms, string label = null)
        {
            if (!Ready) return;
            Count("SONIDO", label ?? (freq + " Hz"), $"T,{freq},{ms}");
        }

        public void Melody(int[] notes, int ms, string label)
        {
            if (!Ready) return;
            Stats.TryGetValue("SONIDO", out var n);
            Stats["SONIDO"] = n + 1;
            Feedback?.Invoke("SONIDO", label);
            StartCoroutine(PlayMelody(notes, ms));
        }

        IEnumerator PlayMelody(int[] notes, int ms)
        {
            foreach (var f in notes)
            {
                Send($"T,{f},{ms - 15}");
                yield return new WaitForSecondsRealtime(ms / 1000f);
            }
        }

        // retroalimentacion fisica en el carro
        public void Brake(int ms, string label = null)
        {
            if (!Ready) return;
            Count("CARRO", label ?? $"Freno {ms} ms", "F," + ms);
        }

        public void Shake(int ms, string label = null)
        {
            if (!Ready) return;
            Count("CARRO", label ?? $"Vibracion {ms} ms", "V," + ms);
        }

        public void SetSpeed(int pct, string label = null)
        {
            if (!Ready) return;
            if (label == null) Send("S," + pct);
            else Count("CARRO", label, "S," + pct);
        }

        public void SetMode(int m)
        {
            Mode = m;
            if (Ready) Send("M," + m);
        }

        public void RestartAuto()
        {
            if (Ready) Send("R");
        }

        void OnApplicationQuit() { Disconnect(); }
    }
}
