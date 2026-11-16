using System.Collections.Generic;
using UnityEngine;

namespace RallyMixto
{
    // control y carro simulados (modo demostracion, teclado)
    public class SimLink : ILink
    {
        readonly Queue<string> rx = new Queue<string>();
        float tSend, tFx, tStep;
        int mode = 0, speed = 100, step = 255, state = 0;
        float jx, jy;
        public Vector2? forcedStick;

        public bool IsOpen => true;
        public string Label => "DEMO";
        public string Error => null;

        public SimLink() { rx.Enqueue("H,CarroRM,sim"); }

        public bool TryRead(out string line)
        {
            if (rx.Count > 0) { line = rx.Dequeue(); return true; }
            line = null;
            return false;
        }

        public void Send(string line)
        {
            var p = line.Split(',');
            int a = p.Length > 1 && int.TryParse(p[1], out var v) ? v : 0;
            switch (p[0])
            {
                case "?": rx.Enqueue("H,CarroRM,sim"); break;
                case "M": SetMode(a); break;
                case "S": speed = Mathf.Clamp(a, 0, 100); break;
                case "F": state = 3; tFx = a / 1000f; break;
                case "V": state = 4; tFx = a / 1000f; break;
                case "R": if (mode == 0) StartAuto(); break;
            }
        }

        void SetMode(int m)
        {
            if (m == mode) return;
            mode = m;
            if (mode == 0) StartAuto(); else step = 255;
        }

        void StartAuto() { step = 0; tStep = 0; }

        public void Tick(float dt)
        {
            var target = forcedStick ?? KeyStick();
            jx = Mathf.MoveTowards(jx, target.x, dt * 6000f);
            jy = Mathf.MoveTowards(jy, target.y, dt * 6000f);

            if (forcedStick == null && Input.GetKeyDown(KeyCode.Space))
            {
                SetMode(mode == 1 ? 0 : 1);
                rx.Enqueue("E,BTN," + mode);
            }

            if (tFx > 0) { tFx -= dt; if (tFx <= 0) state = 0; }
            else if (mode == 0 && step != 255)
            {
                tStep += dt;
                if (tStep >= AutoRoutine.Steps[step].duration)
                {
                    tStep = 0;
                    step++;
                    if (step >= AutoRoutine.Steps.Length) step = 255;
                }
            }

            tSend += dt;
            if (tSend < 0.05f) return;
            tSend = 0;

            int a = 0, b = 0;
            if (tFx > 0) { a = b = 0; }
            else if (mode == 1)
            {
                Twin.Mix((int)-jx, (int)jy, out a, out b);
                a = Limit(a); b = Limit(b);
            }
            else if (step != 255)
            {
                var s = AutoRoutine.Steps[step];
                a = Limit(s.a); b = Limit(s.b);
            }
            int st = tFx > 0 ? state : (step != 255 ? 1 : 0);
            rx.Enqueue($"D,{(int)jx},{(int)jy},{mode},1,{a},{b},{step},{st},{speed}");
        }

        int Limit(int pwm)
        {
            if (pwm == 0 || speed == 0) return 0;
            int v = Mathf.Abs(pwm) * speed / 100;
            return (int)Mathf.Sign(pwm) * Mathf.Clamp(v, 70, 255);
        }

        static Vector2 KeyStick()
        {
            float f = 0, t = 0;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) f += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) f -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) t += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) t -= 1;
            return new Vector2(-f * 1000f, t * 1000f);
        }

        public void Close() { }
    }
}
