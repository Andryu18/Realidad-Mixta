using UnityEngine;

namespace RallyMixto
{
    // modo automatico: el gemelo replica la rutina del carro
    public class AutoMode : MonoBehaviour
    {
        public bool Active { get; private set; }
        public int Step { get; private set; } = 255;
        public float StepProgress { get; private set; }
        public bool Simulated { get; private set; }
        public bool Done { get; private set; }

        Arena arena;
        Twin twin;
        CameraRig cam;
        Hub hub;
        float simT, stepT;
        int simStep = 255, lastStep = 255;

        public void Init(Arena a, Twin t, CameraRig c)
        {
            arena = a;
            twin = t;
            cam = c;
            hub = Hub.I;
        }

        public void Enter()
        {
            Active = true;
            arena.missionProps.SetActive(false);
            twin.maxSpeed = 4f;
            twin.maxTurn = 72f;
            twin.Frozen = false;
            cam.mode = CamMode.Overview;
            Restart();
            cam.Snap();
        }

        public void Restart()
        {
            twin.Place(arena.autoPos, arena.autoYaw);
            hub.SetSpeed(100);
            if (hub.Mode == 0) hub.RestartAuto();
            hub.SetMode(0);
            hub.SetLed(Led.Slow, "Modo automático");
            hub.SetCarLed(Led.Slow);
            simStep = 0;
            simT = 0;
            stepT = 0;
            lastStep = 255;
            Done = false;
        }

        public void Exit()
        {
            Active = false;
            hub.SetMode(1);
            hub.SetSpeed(0);
            hub.SetLed(Led.Off);
            hub.SetCarLed(Led.Off);
        }

        void Update()
        {
            if (!Active) return;
            float dt = Time.deltaTime;

            // paso local de respaldo (si el carro no responde)
            if (simStep != 255)
            {
                simT += dt;
                if (simT >= AutoRoutine.Steps[simStep].duration)
                {
                    simT = 0;
                    simStep++;
                    if (simStep >= AutoRoutine.Steps.Length) simStep = 255;
                }
            }

            Simulated = !hub.CarLink;
            int step = Simulated ? simStep : hub.Step;
            if (hub.Mode != 0) step = 255;

            if (step != lastStep)
            {
                if (step != 255)
                {
                    stepT = 0;
                    hub.Tone(660, 90, "Paso " + (step + 1) + ": " + AutoRoutine.Steps[step].name);
                    hub.FlashAll("Paso " + (step + 1));
                }
                else if (lastStep == AutoRoutine.Steps.Length - 1)
                {
                    Done = true;
                    hub.Melody(new[] { 784, 1046 }, 180, "Rutina completa");
                    hub.SetLed(Led.On, "Rutina completa");
                    hub.SetCarLed(Led.On);
                }
                lastStep = step;
            }
            Step = step;
            stepT += dt;
            StepProgress = step == 255 ? 0 : Mathf.Clamp01(stepT / AutoRoutine.Steps[step].duration);

            if (Simulated)
            {
                var s = step == 255 ? new AutoStep() : AutoRoutine.Steps[step];
                twin.SetMotors(s.a, s.b);
                twin.Braking = false;
                twin.Shaking = false;
                twin.LedPattern = hub.CarLedNow;
            }
            else ManualMode.DriveTwin(hub, twin, 100);
        }
    }
}
