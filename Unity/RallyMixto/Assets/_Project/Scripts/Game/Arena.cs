using System.Collections.Generic;
using UnityEngine;

namespace RallyMixto
{
    public enum PickKind { Cell, Boost, Oil }

    public class Pickup : MonoBehaviour
    {
        public PickKind kind;
        public float cooldown;
        public int slot;
    }

    // arena construida por codigo
    public class Arena : MonoBehaviour
    {
        public const float W = 52f, D = 36f;
        public readonly List<Vector3> cellSlots = new List<Vector3>();
        public GameObject missionProps;
        public Vector3 startPos = new Vector3(-8f, 0, -13.5f);
        public float startYaw = 90f;
        public Vector3 autoPos = new Vector3(-10f, 0, -6f);
        public float autoYaw = 90f;
        public Vector3 showcasePos = new Vector3(0, 0, 0);

        MaterialLibrary lib;
        readonly List<Material> scrolling = new List<Material>();
        ParticleSystem burst, sparks;
        Mesh crystal;

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
            if (!collider) Destroy(g.GetComponent<Collider>());
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
            Destroy(floor.GetComponent<Collider>());

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

            // turbos y aceite
            Boost(mp, new Vector3(6f, 0, 13f), 270f);
            Boost(mp, new Vector3(-21.5f, 0, -2f), 0f);
            Boost(mp, new Vector3(21.5f, 0, 2f), 180f);
            Oil(mp, new Vector3(-12f, 0, 13.5f));
            Oil(mp, new Vector3(14f, 0, -13f));
            Oil(mp, new Vector3(21f, 0, -11f));

            // puntos donde aparecen las celdas (carril alrededor de la isla)
            for (int i = 0; i < 22; i++)
            {
                float s = i / 22f * Mathf.PI * 2f;
                float x = Mathf.Cos(s) * 19.5f, z = Mathf.Sin(s) * 12.5f;
                x = Mathf.Clamp(x * 1.15f, -22f, 22f);
                z = Mathf.Clamp(z * 1.12f, -14.5f, 14.5f);
                cellSlots.Add(new Vector3(x, 0, z));
            }

            crystal = Crystal();
            burst = Particles("Chispas_Celda", new Color(0.35f, 0.95f, 1f), 50, 6f);
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
            Destroy(g.GetComponent<Collider>());
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

        void Boost(Transform p, Vector3 pos, float yaw)
        {
            var m = new Material(lib.boost);
            var tex = ProcTex.Chevrons();
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_EmissionMap", tex);
            m.SetTextureScale("_BaseMap", new Vector2(1, 1.5f));
            scrolling.Add(m);
            var g = Box("Turbo", pos + Vector3.up * 0.03f, new Vector3(3.2f, 0.04f, 6f), m, p);
            g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var c = g.GetComponent<BoxCollider>();
            c.isTrigger = true;
            c.size = new Vector3(1, 40f, 1);
            g.AddComponent<Pickup>().kind = PickKind.Boost;
        }

        void Oil(Transform p, Vector3 pos)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = "Aceite";
            g.transform.SetParent(p, false);
            g.transform.localPosition = pos + Vector3.up * 0.02f;
            g.transform.localScale = new Vector3(3.6f, 0.01f, 3.0f);
            g.GetComponent<Renderer>().sharedMaterial = lib.oil;
            Destroy(g.GetComponent<Collider>());
            var c = g.AddComponent<BoxCollider>();
            c.isTrigger = true;
            c.size = new Vector3(0.8f, 200f, 0.8f);
            g.AddComponent<Pickup>().kind = PickKind.Oil;
        }

        ReflectionProbe probe;
        int probeFrames;

        void LateUpdate()
        {
            if (probe && probeFrames < 3 && ++probeFrames == 3) probe.RenderProbe();
        }

        public GameObject SpawnCell(Vector3 pos, int slot)
        {
            var g = new GameObject("Celda");
            g.transform.SetParent(missionProps.transform, false);
            g.transform.localPosition = pos;
            var body = new GameObject("Cristal");
            body.transform.SetParent(g.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = crystal;
            var r = body.AddComponent<MeshRenderer>();
            r.sharedMaterial = lib.cell;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(g.transform, false);
            glow.transform.localPosition = Vector3.up * 0.03f;
            glow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            glow.transform.localScale = Vector3.one * 2.6f;
            glow.GetComponent<Renderer>().sharedMaterial = lib.glowRing;

            var col = g.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.1f;
            col.center = Vector3.up * 0.8f;
            var pk = g.AddComponent<Pickup>();
            pk.kind = PickKind.Cell;
            pk.slot = slot;
            return g;
        }

        public void Burst(Vector3 pos, bool cell)
        {
            var ps = cell ? burst : sparks;
            ps.transform.position = pos + Vector3.up * 0.6f;
            ps.Emit(cell ? 50 : 35);
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

        static Mesh Crystal()
        {
            var m = new Mesh { name = "Cristal" };
            var v = new List<Vector3>();
            var tris = new List<int>();
            Vector3 top = new Vector3(0, 1.6f, 0), bot = new Vector3(0, 0.4f, 0);
            int n = 6;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 1f, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 1f, Mathf.Sin(a1) * 0.5f);
                int k = v.Count;
                v.AddRange(new[] { top, p1, p0 });
                tris.AddRange(new[] { k, k + 1, k + 2 });
                k = v.Count;
                v.AddRange(new[] { bot, p0, p1 });
                tris.AddRange(new[] { k, k + 1, k + 2 });
            }
            m.SetVertices(v);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        void Update()
        {
            float o = -Time.time * 1.2f;
            foreach (var m in scrolling)
            {
                m.SetTextureOffset("_BaseMap", new Vector2(0, o));
                m.SetTextureOffset("_EmissionMap", new Vector2(0, o));
            }
        }
    }
}
