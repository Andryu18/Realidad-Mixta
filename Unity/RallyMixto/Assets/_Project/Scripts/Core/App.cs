using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyMixto
{
    // flujo de la experiencia
    public class App : MonoBehaviour
    {
        public MaterialLibrary lib;
        public UIDocument doc;
        public StyleSheet sheet;
        public Camera cam;

        [HideInInspector] public Arena arena;
        [HideInInspector] public Twin twin;
        [HideInInspector] public CameraRig rig;
        [HideInInspector] public Mission mission;
        [HideInInspector] public AutoMode autoMode;
        [HideInInspector] public bool autoshots;

        public ConnectScreen connect;
        public MenuScreen menu;
        public HudScreen hud;
        public ResultsScreen results;
        public AutoScreen auto;

        VisualElement uiRoot, screens, topbar, toasts, chipDot;
        Label chipPort;
        UiScreen current;
        Hub hub;

        void Start()
        {
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;

            hub = gameObject.AddComponent<Hub>();
            gameObject.AddComponent<Sfx>();
            arena = Arena.Build(lib);
            twin = Twin.Build(lib, null);
            twin.Place(arena.showcasePos + new Vector3(0, 0, -12.8f), 200f);
            twin.Frozen = true;

            rig = cam.gameObject.AddComponent<CameraRig>();
            rig.target = twin.transform;
            rig.Snap();

            mission = gameObject.AddComponent<Mission>();
            mission.Init(arena, twin, rig);
            autoMode = gameObject.AddComponent<AutoMode>();
            autoMode.Init(arena, twin, rig);

            BuildUi();

            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-autoshots");
            if (i >= 0 && i + 1 < args.Length)
            {
                autoshots = true;
                StartCoroutine(AutoShots(args[i + 1]));
            }
            Show(connect);
        }

        void BuildUi()
        {
            var r = doc.rootVisualElement;
            r.styleSheets.Add(sheet);
            r.style.position = Position.Absolute;
            r.style.left = 0; r.style.top = 0; r.style.right = 0; r.style.bottom = 0;
            uiRoot = Ui.El(r, "root");

            var vig = Ui.El(uiRoot, "layer", "vignette");
            vig.style.backgroundImage = Vignette();
            vig.pickingMode = PickingMode.Ignore;

            screens = Ui.El(uiRoot, "layer");
            screens.pickingMode = PickingMode.Ignore;

            topbar = Ui.El(uiRoot, "topbar");
            topbar.pickingMode = PickingMode.Ignore;
            var brand = Ui.El(topbar, "brand");
            Ui.Text(brand, "RALLY ", "brand-name").style.marginLeft = 0;
            Ui.Text(brand, "MIXTO", "brand-name", "brand-name-b").style.marginLeft = 0;
            var chip = Ui.El(topbar, "chip");
            chipDot = Ui.El(chip, "chip-dot");
            chipPort = Ui.Text(chip, "", "chip-text");

            toasts = Ui.El(uiRoot, "toasts");
            toasts.pickingMode = PickingMode.Ignore;

            connect = Make<ConnectScreen>();
            menu = Make<MenuScreen>();
            hud = Make<HudScreen>();
            results = Make<ResultsScreen>();
            auto = Make<AutoScreen>();
        }

        T Make<T>() where T : UiScreen, new()
        {
            var s = new T();
            s.Init(this);
            return s;
        }

        static Texture2D Vignette()
        {
            int w = 256, h = 144;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x / (w - 1f)) * 2 - 1, dy = (y / (h - 1f)) * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
                    float a = Mathf.SmoothStep(0.55f, 1.45f, d) * 0.8f;
                    float bottom = Mathf.SmoothStep(0.4f, 1f, -dy) * 0.25f;
                    px[y * w + x] = new Color(0.01f, 0.015f, 0.03f, Mathf.Clamp01(a + bottom));
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        public void Show(UiScreen s)
        {
            if (current != null)
            {
                current.OnHide();
                var old = current.root;
                old.RemoveFromClassList("screen--in");
                old.pickingMode = PickingMode.Ignore;
                old.schedule.Execute(() => old.RemoveFromHierarchy()).ExecuteLater(300);
            }
            current = s;
            s.root.RemoveFromHierarchy();
            s.root.RemoveFromClassList("screen--in");
            s.root.pickingMode = PickingMode.Position;
            screens.Add(s.root);
            s.root.schedule.Execute(() => s.root.AddToClassList("screen--in")).ExecuteLater(30);
            s.OnShow();
        }

        // camara de vitrina con el carro a la derecha
        public void ToShowcase(bool on)
        {
            if (on)
            {
                mission.Abort();
                twin.Frozen = true;
                twin.Braking = twin.Shaking = false;
                arena.missionProps.SetActive(true);
                twin.Place(arena.showcasePos + new Vector3(0, 0, -12.8f), 200f);
                rig.mode = CamMode.Showcase;
                rig.showcaseOffset = new Vector2(1.5f, 0.15f);
                rig.Snap();
            }
        }

        public void Toast(string text, bool err = false)
        {
            var t = Ui.El(toasts, "toast");
            if (err) t.AddToClassList("toast--err");
            Ui.Text(t, text, "toast-text");
            t.schedule.Execute(() => t.AddToClassList("toast--hide")).ExecuteLater(3200);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).ExecuteLater(3600);
        }

        public void Quit()
        {
            hub.Disconnect();
            Application.Quit();
        }

        void Update()
        {
            current?.Tick(Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.Escape)) current?.OnBack();

            // conexion perdida fuera de la pantalla de conexion
            if (hub.Phase == LinkPhase.Failed && current != connect)
            {
                Sfx.I.Play("error");
                Toast("Se perdió la conexión con el control", true);
                Show(connect);
            }
            if (twin.Frozen) twin.LedPattern = hub.CarLedNow;
            UpdateChip();
        }

        void UpdateChip()
        {
            bool ready = hub.Ready;
            chipDot.EnableInClassList("chip-dot--ok", ready && hub.CarLink);
            chipDot.EnableInClassList("chip-dot--warn", ready && !hub.CarLink);
            chipDot.EnableInClassList("chip-dot--off", !ready);
            if (!ready) { chipPort.text = "Sin conexión"; return; }
            chipPort.text = (hub.IsDemo ? "Demo" : hub.PortName) + (hub.CarLink ? " · carro enlazado" : " · carro sin enlace");
        }

        // capturas automaticas para la documentacion (-autoshots carpeta)
        IEnumerator AutoShots(string dir)
        {
            Directory.CreateDirectory(dir);
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                yield return null;
                yield return null;
            }
            var wait = new WaitForSecondsRealtime(0.1f);

            yield return new WaitForSecondsRealtime(1.6f);
            yield return Shot("02_conexion");

            hub.StartDemo();
            var sim = (SimLink)hub.Link;
            yield return new WaitForSecondsRealtime(0.8f);
            Show(menu);
            yield return new WaitForSecondsRealtime(1.5f);
            sim.forcedStick = new Vector2(-650, 400);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("03_menu");
            sim.forcedStick = Vector2.zero;

            Show(hud);
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Shot("04_cuenta_regresiva");
            float t = 0, worst = 0;
            int frames = 0;
            bool shot5 = false, shot6 = false, shot7 = false;
            while (t < 34f)
            {
                sim.forcedStick = Autopilot();
                t += Time.unscaledDeltaTime;
                frames++;
                if (t > 2f) worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                if (!shot5 && t > 9f) { shot5 = true; yield return Shot("05_mision"); }
                if (!shot6 && mission.BoostT > 2.5f && t > 12f) { shot6 = true; yield return Shot("06_mision_turbo"); }
                if (!shot7 && t > 22f) { shot7 = true; yield return Shot("07_mision_retro"); }
                yield return null;
            }
            if (!shot6) yield return Shot("06_mision_turbo");
            File.WriteAllText(Path.Combine(dir, "rendimiento.txt"),
                $"fps_promedio={frames / Mathf.Max(t, 0.01f):0.0}\nfps_minimo={1f / Mathf.Max(worst, 1e-4f):0.0}\nresolucion={Screen.width}x{Screen.height}\nceldas={mission.Cells}\nturbos={mission.Boosts}\nchoques={mission.Hits}\n");
            mission.SetTimeLeft(0.5f);
            yield return new WaitForSecondsRealtime(3.2f);
            sim.forcedStick = Vector2.zero;
            yield return Shot("08_resultados");

            Show(auto);
            yield return new WaitForSecondsRealtime(9.5f);
            yield return Shot("09_automatico");

            Show(menu);
            yield return new WaitForSecondsRealtime(0.8f);
            uiRoot.style.display = DisplayStyle.None;
            rig.showcaseOffset = Vector2.zero;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("11_gemelo_digital");
            uiRoot.style.display = DisplayStyle.Flex;
            yield return wait;
            Application.Quit();
        }

        // piloto automatico hacia la celda mas cercana (solo capturas)
        Vector2 Autopilot()
        {
            Transform best = null;
            float bd = 1e9f;
            foreach (var p in FindObjectsByType<Pickup>(FindObjectsSortMode.None))
            {
                if (p.kind != PickKind.Cell) continue;
                float d = Vector3.Distance(p.transform.position, twin.transform.position);
                if (d < bd) { bd = d; best = p.transform; }
            }
            if (!best) return new Vector2(-700, 0);
            var to = best.position - twin.transform.position;
            to.y = 0;
            float ang = Vector3.SignedAngle(twin.transform.forward, to, Vector3.up);
            float turn = Mathf.Clamp(ang / 45f, -1, 1) * 1000f;
            float thr = Mathf.Abs(ang) > 70 ? 300 : 1000;
            return new Vector2(-thr, turn);
        }
    }
}
