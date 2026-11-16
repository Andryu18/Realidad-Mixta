using System;
using UnityEngine;

namespace RallyMixto
{
    // modo manual: el joystick mueve el carro real y su gemelo
    public class ManualMode : MonoBehaviour
    {
        public bool Active { get; private set; }
        public int Hits { get; private set; }
        public event Action Crash;

        Arena arena;
        Twin twin;
        CameraRig cam;
        Hub hub;
        float ledRestore;

        public void Init(Arena a, Twin t, CameraRig c)
        {
            arena = a;
            twin = t;
            cam = c;
            hub = Hub.I;
            twin.Hit += OnHit;
        }

        public void Enter()
        {
            Active = true;
            Hits = 0;
            arena.missionProps.SetActive(true);
            twin.maxSpeed = 6f;
            twin.maxTurn = 130f;
            twin.Place(arena.startPos, arena.startYaw);
            twin.Frozen = false;
            hub.SetMode(1);
            hub.SetSpeed(100);
            hub.SetLed(Led.On, "Modo manual");
            hub.SetCarLed(Led.On);
            cam.mode = CamMode.Chase;
            cam.Snap();
        }

        public void Exit()
        {
            if (!Active) return;
            Active = false;
            twin.Frozen = true;
            hub.SetSpeed(0);
            hub.SetLed(Led.Off);
            hub.SetCarLed(Led.Off);
            Sfx.I.engineLevel = 0;
        }

        void Update()
        {
            if (!Active) return;
            if (ledRestore > 0)
            {
                ledRestore -= Time.deltaTime;
                if (ledRestore <= 0) hub.SetLed(Led.On);
            }
            DriveTwin(hub, twin, 100);
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

        // choque en la pista: freno, led y sonido en el hardware
        void OnHit(float impact)
        {
            if (!Active) return;
            Hits++;
            arena.Burst(twin.transform.position + twin.transform.forward * 0.8f);
            cam.Shake(Mathf.Clamp(impact / 6f, 0.3f, 1f));
            Sfx.I.Play("hit", Mathf.Clamp01(impact / 5f) * 0.6f + 0.4f);
            hub.Brake(400, "Choque: freno 400 ms");
            hub.Tone(140, 220, "Choque");
            hub.SetLed(Led.Fast);
            ledRestore = 0.6f;
            Crash?.Invoke();
        }
    }
}
