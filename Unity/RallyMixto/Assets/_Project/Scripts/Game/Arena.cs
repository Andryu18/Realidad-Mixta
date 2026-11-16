using System.Collections.Generic;
using UnityEngine;

namespace RallyMixto
{
    // arena construida por codigo
    public class Arena : MonoBehaviour
    {
        public const float W = 52f, D = 36f;
        public GameObject missionProps;
        public Vector3 startPos = new Vector3(-8f, 0, -13.5f);
        public float startYaw = 90f;
        public Vector3 autoPos = new Vector3(-10f, 0, -6f);
        public float autoYaw = 90f;
        public Vector3 showcasePos = new Vector3(0, 0, 0);

        [SerializeField] MaterialLibrary lib;
        [SerializeField] ParticleSystem sparks;

        public static Arena Build(MaterialLibrary lib)
        {
            var go = new GameObject("Arena");
            var a = go.AddComponent<Arena>();
            a.lib = lib;
            a.Make();
            return a;
        }

        GameObject Box(string name, Vector3 pos, Vector3 size, Material m, Transform parent, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        void Make()
        {
            var t = transform;

            // piso
            var floorTex = ProcTex.Floor();
            var floorMat = new Material(lib.floor);
            floorMat.SetTexture("_BaseMap", floorTex);
            floorMat.SetTextureScale("_BaseMap", new Vector2(20, 20));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Piso";
            floor.transform.SetParent(t, false);
            floor.transform.localScale = new Vector3(16, 1, 16);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            floor.GetComponent<Renderer>().receiveShadows = true;
            DestroyImmediate(floor.GetComponent<Collider>());

            // reflejos de neones en piso y carro
            var probe = new GameObject("Reflejos").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(t, false);
            probe.transform.localPosition = new Vector3(0, 2.5f, 0);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(W + 20, 14, D + 20);
            probe.resolution = 256;
            probe.boxProjection = true;
            this.probe = probe;

            // muros con luz superior
            var walls = new GameObject("Muros").transform;
            walls.SetParent(t, false);
            float th = 0.7f, h = 0.9f;
            Wall(walls, new Vector3(0, h / 2, D / 2 + th / 2), new Vector3(W + th * 2, h, th));
            Wall(walls, new Vector3(0, h / 2, -D / 2 - th / 2), new Vector3(W + th * 2, h, th));
            Wall(walls, new Vector3(W / 2 + th / 2, h / 2, 0), new Vector3(th, h, D));
            Wall(walls, new Vector3(-W / 2 - th / 2, h / 2, 0), new Vector3(th, h, D));

            // torres de esquina
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1 : 1, sz = i < 2 ? -1 : 1;
                var p = new Vector3(sx * (W / 2 + 1.6f), 0, sz * (D / 2 + 1.6f));
                Box("Torre", p + Vector3.up * 3f, new Vector3(0.8f, 6f, 0.8f), lib.wall, t);
                Box("Torre_Luz", p + Vector3.up * 6.15f, new Vector3(1.0f, 0.3f, 1.0f), lib.neonAmber, t, false);
                var l = new GameObject("Luz_Torre").AddComponent<Light>();
                l.transform.SetParent(t, false);
                l.transform.localPosition = p + Vector3.up * 5.5f;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.72f, 0.32f);
                l.range = 22f;
                l.intensity = 9f;
            }

            missionProps = new GameObject("Obstaculos");
            missionProps.transform.SetParent(t, false);
            var mp = missionProps.transform;

            // isla central
            Wall(mp, new Vector3(0, h / 2, 0), new Vector3(24f, h, 10f), true);
            Box("Isla_Luz", new Vector3(0, h + 0.01f, 0), new Vector3(22.6f, 0.04f, 8.6f), lib.dark, mp, false);
            Box("Isla_Linea", new Vector3(0, h + 0.03f, 0), new Vector3(20f, 0.03f, 0.12f), lib.neonAmber, mp, false);

            // columnas en los carriles
            Pillar(mp, new Vector3(-18f, 0, 9f));
            Pillar(mp, new Vector3(18f, 0, -9f));
            Pillar(mp, new Vector3(20f, 0, 9.5f));

            // arco de salida
            Gate(t, new Vector3(-4f, 0, -13f));

            // linea de salida
            var chk = new Material(lib.checker);
            chk.SetTexture("_BaseMap", ProcTex.Checker());
            chk.SetTextureScale("_BaseMap", new Vector2(1, 6));
            Box("Linea_Salida", new Vector3(-4f, 0.02f, -13f), new Vector3(1.2f, 0.02f, 8f), chk, mp, false);

            sparks = Particles("Chispas_Choque", new Color(1f, 0.6f, 0.2f), 35, 5f);
        }

        void Wall(Transform p, Vector3 pos, Vector3 size, bool island = false)
        {
            var w = Box(island ? "Isla" : "Muro", pos, size, lib.wall, p);
            w.GetComponent<Collider>().material = new PhysicsMaterial("muro") { dynamicFriction = 0, staticFriction = 0, frictionCombine = PhysicsMaterialCombine.Minimum };
            if (!island)
                Box("Neon", new Vector3(pos.x, size.y + 0.03f, pos.z), new Vector3(size.x + 0.02f, 0.06f, size.z + 0.02f), lib.wallTop, p, false);
            else
            {
                // solo el contorno de la isla
                float y = size.y + 0.03f, e = 0.12f;
                Box("Neon", new Vector3(pos.x, y, pos.z + size.z / 2 - e / 2), new Vector3(size.x, 0.06f, e), lib.wallTop, p, false);
                Box("Neon", new Vector3(pos.x, y, pos.z - size.z / 2 + e / 2), new Vector3(size.x, 0.06f, e), lib.wallTop, p, false);
                Box("Neon", new Vector3(pos.x + size.x / 2 - e / 2, y, pos.z), new Vector3(e, 0.06f, size.z), lib.wallTop, p, false);
                Box("Neon", new Vector3(pos.x - size.x / 2 + e / 2, y, pos.z), new Vector3(e, 0.06f, size.z), lib.wallTop, p, false);
            }
            Box("Neon_Lateral", new Vector3(pos.x, size.y * 0.62f, pos.z), new Vector3(size.x + 0.04f, 0.07f, size.z + 0.04f), lib.neonCyan, p, false);
        }

        void Pillar(Transform p, Vector3 pos)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = "Columna";
            g.transform.SetParent(p, false);
            g.transform.localPosition = pos + Vector3.up * 1.1f;
            g.transform.localScale = new Vector3(1.4f, 1.1f, 1.4f);
            g.GetComponent<Renderer>().sharedMaterial = lib.wall;
            DestroyImmediate(g.GetComponent<Collider>());
            var c = g.AddComponent<CapsuleCollider>();
            c.radius = 0.5f;
            c.height = 2f;
            Box("Anillo", pos + Vector3.up * 1.6f, new Vector3(1.5f, 0.08f, 1.5f), lib.neonRed, p, false);
        }

        void Gate(Transform p, Vector3 pos)
        {
            var g = new GameObject("Arco_Salida").transform;
            g.SetParent(p, false);
            g.localPosition = pos;
            for (int s = -1; s <= 1; s += 2)
            {
                Box("Poste", new Vector3(0, 2.2f, s * 4.6f), new Vector3(0.5f, 4.4f, 0.5f), lib.wall, g);
                Box("Poste_Luz", new Vector3(-0.27f, 2.2f, s * 4.6f), new Vector3(0.04f, 4f, 0.12f), lib.neonCyan, g, false);
            }
            Box("Viga", new Vector3(0, 4.55f, 0), new Vector3(0.7f, 0.5f, 9.7f), lib.wall, g, false);
            Box("Viga_Luz", new Vector3(-0.36f, 4.55f, 0), new Vector3(0.04f, 0.18f, 9f), lib.neonAmber, g, false);
            var l = new GameObject("Luz_Arco").AddComponent<Light>();
            l.transform.SetParent(g, false);
            l.transform.localPosition = new Vector3(-1.5f, 3.5f, 0);
            l.type = LightType.Point;
            l.color = new Color(0.4f, 0.85f, 1f);
            l.range = 14f;
            l.intensity = 6f;
        }

        [SerializeField] ReflectionProbe probe;
        int probeFrames;

        void LateUpdate()
        {
            if (probe && probeFrames < 3 && ++probeFrames == 3) probe.RenderProbe();
        }

        public void Burst(Vector3 pos)
        {
            sparks.transform.position = pos + Vector3.up * 0.6f;
            sparks.Emit(35);
        }

        ParticleSystem Particles(string name, Color c, int max, float speed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            main.startColor = c;
            main.gravityModifier = 0.6f;
            main.maxParticles = max * 3;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.3f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 0.3f), new GradientColorKey(c, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = lib.particle;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1.5f;
            return ps;
        }
    }
}
