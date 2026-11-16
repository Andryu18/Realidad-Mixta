using System;
using System.Collections.Generic;
using UnityEngine;

namespace RallyMixto
{
    // gemelo digital del carro: traccion diferencial como el real
    public class Twin : MonoBehaviour
    {
        public const float Scale = 6f;
        public float maxSpeed = 6f;
        public float maxTurn = 130f;
        public float accel = 14f;

        public float Speed => v;
        public float TargetA { get; private set; }
        public float TargetB { get; private set; }
        public bool Braking;
        public bool Shaking;
        public int LedPattern;
        public bool Frozen;

        public event Action<float> Hit;
        public event Action<Collider> Touched;

        [SerializeField] Rigidbody rb;
        [SerializeField] Transform visual;
        [SerializeField] List<Transform> left = new List<Transform>();
        [SerializeField] List<Transform> right = new List<Transform>();
        [SerializeField] Renderer[] brakeLights;
        [SerializeField] Renderer led;
        [SerializeField] MaterialLibrary lib;
        [SerializeField] float wheelR = 0.2f;
        float v, w, stun;

        public static void Mix(int throttle, int turn, out int a, out int b)
        {
            int ma = Mathf.Clamp(throttle + turn, -1000, 1000);
            int mb = Mathf.Clamp(throttle - turn, -1000, 1000);
            a = Mathf.RoundToInt(ma * 255f / 1000f);
            b = Mathf.RoundToInt(mb * 255f / 1000f);
        }

        // mismo limite que el firmware (pwm minimo 70)
        public static int Limit(int pwm, int speed)
        {
            if (pwm == 0 || speed <= 0) return 0;
            int val = Mathf.Abs(pwm) * speed / 100;
            return (int)Mathf.Sign(pwm) * Mathf.Clamp(val, 70, 255);
        }

        public static Twin Build(MaterialLibrary lib, Transform parent)
        {
            var root = new GameObject("Gemelo_Carro");
            root.transform.SetParent(parent, false);
            var t = root.AddComponent<Twin>();
            t.lib = lib;

            var vis = new GameObject("Visual").transform;
            vis.SetParent(root.transform, false);
            t.visual = vis;
            var model = Instantiate(lib.carModel, vis);
            model.name = "Modelo";
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            model.transform.localScale = Vector3.one * Scale;

            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = Pick(lib, r.name, r.sharedMaterial ? r.sharedMaterial.name : "");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // apoyar sobre el piso
            var b = Bounds(model);
            model.transform.localPosition += Vector3.up * (-b.min.y);
            b = Bounds(model);

            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!r.name.Contains("rueda")) continue;
                var local = root.transform.InverseTransformPoint(r.bounds.center);
                (local.x < 0 ? t.left : t.right).Add(r.transform);
                t.wheelR = r.bounds.extents.y;
            }

            var col = root.AddComponent<BoxCollider>();
            col.center = root.transform.InverseTransformPoint(b.center);
            col.size = b.size * 0.96f;
            col.material = new PhysicsMaterial("carro") { dynamicFriction = 0, staticFriction = 0, frictionCombine = PhysicsMaterialCombine.Minimum, bounciness = 0.1f };

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 40;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
            t.rb = rb;

            // luces de freno
            t.brakeLights = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                var bl = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(bl.GetComponent<Collider>());
                bl.name = "Luz_Freno";
                bl.transform.SetParent(vis, false);
                bl.transform.localScale = new Vector3(0.18f, 0.06f, 0.02f);
                bl.transform.localPosition = new Vector3((i == 0 ? -1 : 1) * b.extents.x * 0.55f, b.size.y * 0.55f, b.min.z - root.transform.position.z - 0.01f);
                t.brakeLights[i] = bl.GetComponent<Renderer>();
                t.brakeLights[i].sharedMaterial = lib.dark;
            }

            // led del esp32 (gpio2) en el gemelo
            var pcb = FindChild(model.transform, "ESP32_pcb");
            if (pcb)
            {
                var r = pcb.GetComponent<Renderer>();
                var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(l.GetComponent<Collider>());
                l.name = "LED_GPIO2";
                l.transform.SetParent(vis, false);
                l.transform.position = r.bounds.center + Vector3.up * (r.bounds.extents.y + 0.03f);
                l.transform.localScale = Vector3.one * 0.07f;
                t.led = l.GetComponent<Renderer>();
                t.led.sharedMaterial = lib.dark;
            }
            return t;
        }

        static Material Pick(MaterialLibrary lib, string obj, string mat)
        {
            string s = (obj + " " + mat).ToLowerInvariant();
            if (s.Contains("chasis")) return lib.chassis;
            if (s.Contains("rueda") || s.Contains("hdr") || s.Contains("eje") || s.Contains("usbc") || s.Contains("shield")) return lib.rubber;
            if (s.Contains("motor")) return lib.motor;
            if (s.Contains("disipador")) return lib.metal;
            if (s.Contains("l298n")) return lib.driver;
            if (s.Contains("pcb")) return lib.pcb;
            if (s.Contains("portapilas") || s.Contains("bat")) return lib.battery;
            return lib.chassis;
        }

        static Transform FindChild(Transform t, string part)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>()) if (c.name.Contains(part)) return c;
            return null;
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        public void SetMotors(float a, float b)
        {
            TargetA = a;
            TargetB = b;
        }

        public void Place(Vector3 pos, float yaw)
        {
            rb.position = pos;
            rb.rotation = Quaternion.Euler(0, yaw, 0);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            rb.linearVelocity = Vector3.zero;
            v = w = 0;
            stun = 0;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float a = Frozen ? 0 : TargetA, b = Frozen ? 0 : TargetB;
            if (stun > 0) { stun -= dt; a = b = 0; }

            float tv = (a + b) * 0.5f / 255f * maxSpeed;
            float tw = (a - b) * 0.5f / 255f * maxTurn;
            float rate = (Braking ? 3f : 1f) * accel;
            if (stun <= 0) v = Mathf.MoveTowards(v, tv, rate * dt);
            else v = Mathf.MoveTowards(v, 0, accel * 0.5f * dt);
            w = Mathf.MoveTowards(w, tw, maxTurn * 6f * dt);

            rb.MoveRotation(rb.rotation * Quaternion.Euler(0, w * dt, 0));
            var vel = transform.forward * v;
            vel.y = 0;
            rb.linearVelocity = vel;
            rb.angularVelocity = Vector3.zero;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float baseSpin = v / wheelR * Mathf.Rad2Deg * dt;
            float turnSpin = w * Mathf.Deg2Rad * 0.5f / wheelR * Mathf.Rad2Deg * dt * 0.45f;
            foreach (var t in left) t.Rotate(transform.right, baseSpin + turnSpin, Space.World);
            foreach (var t in right) t.Rotate(transform.right, baseSpin - turnSpin, Space.World);

            // vibracion visual
            visual.localPosition = Shaking
                ? new Vector3(Mathf.Sin(Time.time * 70f) * 0.025f, Mathf.Abs(Mathf.Sin(Time.time * 55f)) * 0.03f, 0)
                : Vector3.zero;
            visual.localRotation = Quaternion.Euler(Mathf.Clamp(-(v - lastV) / Mathf.Max(dt, 1e-3f) * 0.25f, -4f, 4f), 0, Mathf.Clamp(-w * 0.012f, -3f, 3f));
            lastV = v;

            bool braking = Braking || (Mathf.Abs(v) > 0.3f && Mathf.Abs(TargetA + TargetB) < 20f);
            foreach (var r in brakeLights) r.sharedMaterial = braking ? lib.brakeLight : lib.dark;
            if (led) led.sharedMaterial = LedOn() ? lib.ledOn : lib.dark;
        }

        float lastV;

        bool LedOn()
        {
            float t = Time.unscaledTime;
            float c = (t % 1f) * 1000f;
            switch (LedPattern)
            {
                case 1: return true;
                case 2: return (int)(t / 0.5f) % 2 == 1;
                case 3: return (int)(t / 0.12f) % 2 == 1;
                case 4: return c < 80 || (c > 160 && c < 240);
                default: return false;
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (c.collider.isTrigger) return;
            float impact = Mathf.Abs(Vector3.Dot(c.relativeVelocity, c.GetContact(0).normal));
            if (impact < 0.8f && Mathf.Abs(v) < 1.2f) return;
            impact = Mathf.Max(impact, Mathf.Abs(v));
            v = -Mathf.Sign(v) * Mathf.Min(Mathf.Abs(v) * 0.35f, 2f);
            stun = 0.35f;
            Hit?.Invoke(impact);
        }

        void OnTriggerEnter(Collider c) { Touched?.Invoke(c); }
    }
}
