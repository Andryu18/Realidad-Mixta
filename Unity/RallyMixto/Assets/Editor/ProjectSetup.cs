using System.IO;
using RallyMixto;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

// arma el proyecto y compila el ejecutable (tambien en modo batch)
public static class ProjectSetup
{
    const string P = "Assets/_Project";
    const string Mats = P + "/Art/Materials";
    const string Scene = P + "/Scenes/Main.unity";

    [MenuItem("Rally Mixto/1. Configurar proyecto")]
    public static void Setup()
    {
        Player();
        ImportModel();
        var lib = Library();
        Urp();
        var profile = Profile();
        var panel = Panel();
        BuildScene(lib, profile, panel);
        AssetDatabase.SaveAssets();
        Debug.Log("[setup] listo");
    }

    [MenuItem("Rally Mixto/2. Compilar Windows")]
    public static void Build()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Build/RallyMixto"));
        Directory.CreateDirectory(root);
        var opt = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = Path.Combine(root, "RallyMixto.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        var r = BuildPipeline.BuildPlayer(opt);
        Debug.Log("[build] " + r.summary.result + " " + r.summary.totalSize / (1024 * 1024) + " MB -> " + opt.locationPathName);
        if (r.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public static void SetupAndBuild()
    {
        Setup();
        Build();
    }

    static void Player()
    {
        var t = NamedBuildTarget.Standalone;
        PlayerSettings.SetApiCompatibilityLevel(t, ApiCompatibilityLevel.NET_Unity_4_8);
        PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.Mono2x);
        PlayerSettings.companyName = "USB Bogota";
        PlayerSettings.productName = "Rally Mixto";
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.colorSpace = ColorSpace.Linear;

        string icon = P + "/Art/icon.png";
        if (File.Exists(icon))
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(icon);
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(icon);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
        }
    }

    static void ImportModel()
    {
        string path = P + "/Art/Models/carro_rm.fbx";
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.SaveAndReimport();
    }

    static Material Lit(string name, Color c, float smooth = 0.4f, float metal = 0f, Color? emission = null, bool twoSided = false)
    {
        Directory.CreateDirectory(Mats);
        string path = $"{Mats}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else m.DisableKeyword("_EMISSION");
        if (twoSided) m.SetFloat("_Cull", 0);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Additive(string name, Texture2D tex, Color c)
    {
        string path = $"{Mats}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", 2);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D SaveGlow()
    {
        string path = P + "/Art/glow.png";
        if (!File.Exists(path))
        {
            int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Exp(-Mathf.Pow((d - 0.62f) / 0.07f, 2)) * 0.9f;
                    float core = Mathf.Pow(Mathf.Clamp01(1 - d), 2.2f) * 0.55f;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(ring + core)));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static MaterialLibrary Library()
    {
        string path = P + "/Settings/MaterialLibrary.asset";
        var lib = AssetDatabase.LoadAssetAtPath<MaterialLibrary>(path);
        if (lib == null)
        {
            lib = ScriptableObject.CreateInstance<MaterialLibrary>();
            AssetDatabase.CreateAsset(lib, path);
        }
        Color cyan = new Color(0.21f, 0.84f, 1f), amber = new Color(1f, 0.72f, 0.18f), red = new Color(1f, 0.2f, 0.28f);

        // doble cara: el fbx llega espejado
        lib.chassis = Lit("M_Chasis", new Color(0.86f, 0.62f, 0.09f), 0.42f, 0, null, true);
        lib.rubber = Lit("M_Caucho", new Color(0.035f, 0.035f, 0.04f), 0.18f, 0, null, true);
        lib.motor = Lit("M_Motor", new Color(0.93f, 0.74f, 0.12f), 0.55f, 0, null, true);
        lib.pcb = Lit("M_PCB", new Color(0.04f, 0.2f, 0.5f), 0.65f, 0, null, true);
        lib.pcbAlt = Lit("M_PCB2", new Color(0.3f, 0.06f, 0.4f), 0.6f, 0, null, true);
        lib.battery = Lit("M_Bateria", new Color(0.02f, 0.02f, 0.025f), 0.12f, 0, null, true);
        lib.driver = Lit("M_L298N", new Color(0.72f, 0.05f, 0.06f), 0.5f, 0, null, true);
        lib.metal = Lit("M_Disipador", new Color(0.22f, 0.22f, 0.24f), 0.62f, 0.9f, null, true);
        lib.ledOn = Lit("M_LED", new Color(0.2f, 0.45f, 1f), 0.8f, 0, new Color(0.25f, 0.55f, 1f) * 9f);
        lib.brakeLight = Lit("M_Freno", new Color(0.6f, 0.02f, 0.03f), 0.6f, 0, red * 6f);

        lib.floor = Lit("M_Piso", new Color(0.62f, 0.66f, 0.74f), 0.42f, 0.1f);
        lib.wall = Lit("M_Muro", new Color(0.075f, 0.085f, 0.105f), 0.55f, 0.35f);
        lib.wallTop = Lit("M_Muro_Luz", new Color(0.8f, 0.95f, 1f), 0.5f, 0, cyan * 1.6f);
        lib.neonCyan = Lit("M_Neon_Cian", cyan, 0.5f, 0, cyan * 4.5f);
        lib.neonAmber = Lit("M_Neon_Ambar", amber, 0.5f, 0, amber * 4.5f);
        lib.neonRed = Lit("M_Neon_Rojo", red, 0.5f, 0, red * 4f);
        lib.boost = Lit("M_Turbo", amber, 0.4f, 0, amber * 2.6f);
        lib.oil = Lit("M_Aceite", new Color(0.02f, 0.015f, 0.035f), 0.97f, 0.2f);
        lib.cell = Lit("M_Celda", new Color(0.4f, 0.9f, 1f), 0.9f, 0, cyan * 5f, true);
        lib.pad = Lit("M_Pista", new Color(0.1f, 0.11f, 0.13f), 0.3f);
        lib.checker = Lit("M_Cuadros", Color.white, 0.35f);
        lib.dark = Lit("M_Oscuro", new Color(0.05f, 0.05f, 0.06f), 0.5f);

        var glow = SaveGlow();
        lib.particle = Additive("M_Particula", glow, Color.white);
        lib.glowRing = Additive("M_Brillo", glow, new Color(0.3f, 0.85f, 1f, 0.8f));
        lib.carModel = AssetDatabase.LoadAssetAtPath<GameObject>(P + "/Art/Models/carro_rm.fbx");
        EditorUtility.SetDirty(lib);
        return lib;
    }

    static void Urp()
    {
        foreach (var name in new[] { "PC_RPAsset", "Mobile_RPAsset" })
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>($"{P}/Settings/{name}.asset");
            if (a == null) continue;
            a.msaaSampleCount = 4;
            a.shadowDistance = 90;
            a.supportsHDR = true;
            EditorUtility.SetDirty(a);
        }
    }

    static VolumeProfile Profile()
    {
        string path = P + "/Settings/RallyVolume.asset";
        var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (old != null) AssetDatabase.DeleteAsset(path);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, path);

        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(1.15f);
        bloom.scatter.Override(0.68f);
        var tone = prof.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        var vig = prof.Add<Vignette>(true);
        vig.intensity.Override(0.24f);
        vig.smoothness.Override(0.45f);
        var ca = prof.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.35f);
        ca.contrast.Override(14f);
        ca.saturation.Override(8f);
        var wb = prof.Add<WhiteBalance>(true);
        wb.temperature.Override(-6f);

        foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
        EditorUtility.SetDirty(prof);
        AssetDatabase.SaveAssets();
        return prof;
    }

    static PanelSettings Panel()
    {
        string path = P + "/UI/RallyPanel.asset";
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (ps == null)
        {
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, path);
        }
        ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(P + "/UI/Rally.tss");
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1920, 1080);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0.5f;
        ps.clearColor = false;
        EditorUtility.SetDirty(ps);
        return ps;
    }

    static void BuildScene(MaterialLibrary lib, VolumeProfile profile, PanelSettings panel)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 45;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.027f, 0.045f);
        camGo.AddComponent<AudioListener>();
        var data = camGo.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        camGo.transform.position = new Vector3(0, 3, -8);

        var sun = new GameObject("Luz_Principal").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(44, -38, 0);
        sun.color = new Color(1f, 0.94f, 0.86f);
        sun.intensity = 1.5f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;

        var fill = new GameObject("Luz_Relleno").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.transform.rotation = Quaternion.Euler(25, 150, 0);
        fill.color = new Color(0.45f, 0.65f, 1f);
        fill.intensity = 0.35f;
        fill.shadows = LightShadows.None;

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.2f, 0.25f, 0.36f);
        RenderSettings.ambientEquatorColor = new Color(0.11f, 0.12f, 0.16f);
        RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.011f;
        RenderSettings.fogColor = new Color(0.02f, 0.027f, 0.045f);

        var vol = new GameObject("Volumen_Global").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        var appGo = new GameObject("App");
        var docu = appGo.AddComponent<UIDocument>();
        docu.panelSettings = panel;
        var app = appGo.AddComponent<App>();
        app.lib = lib;
        app.doc = docu;
        app.cam = cam;
        app.sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(P + "/UI/Rally.uss");

        EditorSceneManager.SaveScene(scene, Scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
    }
}
