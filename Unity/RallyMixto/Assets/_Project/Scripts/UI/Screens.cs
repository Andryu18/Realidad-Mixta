using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyMixto
{
    public abstract class UiScreen
    {
        public VisualElement root;
        protected App app;
        protected Hub hub => Hub.I;
        public virtual bool ShowTopbar => true;

        public void Init(App a)
        {
            app = a;
            root = Ui.El("screen");
            Build();
        }

        protected abstract void Build();
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick(float dt) { }
        public virtual void OnBack() { }
    }

    // pantalla de bienvenida
    public class SplashScreen : UiScreen
    {
        VisualElement fill;
        float t;
        public override bool ShowTopbar => false;

        protected override void Build()
        {
            root.AddToClassList("splash");
            root.Add(new LogoMark(150));
            var title = Ui.El(root);
            title.style.flexDirection = FlexDirection.Row;
            Ui.Text(title, "RALLY ", "splash-title");
            Ui.Text(title, "MIXTO", "splash-title").style.color = Pal.Amber;
            Ui.Text(root, "Experiencia físico-digital · carro ESP32 + Unity", "splash-sub");
            var bar = Ui.El(root, "splash-bar");
            fill = Ui.El(bar, "splash-fill");
            Ui.Text(root, "INGENIERÍA MULTIMEDIA · UNIVERSIDAD DE SAN BUENAVENTURA BOGOTÁ", "splash-foot");
            root.RegisterCallback<PointerDownEvent>(_ => t = 9);
        }

        public override void OnShow() { t = 0; }

        public override void Tick(float dt)
        {
            t += dt;
            fill.style.width = Length.Percent(Mathf.Clamp01(t / 2.6f) * 100f);
            if (Input.anyKeyDown) t = 9;
            if (t >= 3f && !app.autoshots) app.Show(app.connect);
        }
    }

    // seleccion del puerto del control
    public class ConnectScreen : UiScreen
    {
        VisualElement list, status;
        Label statusText;
        Button connectBtn, refreshBtn;
        List<PortInfo> ports = new List<PortInfo>();
        string selected;
        float tScan, tReady;
        LinkPhase lastPhase;
        public LinkDiagram diagram;

        protected override void Build()
        {
            root.AddToClassList("connect");
            var left = Ui.El(root, "col-left");
            Ui.Text(left, "PASO 1 · CONEXIÓN", "eyebrow");
            Ui.Text(left, "Conecta el control", "h1");
            Ui.Text(left, "Elige el puerto del ESP32 del control (el del joystick). Puede estar conectado por cable USB o por Bluetooth.", "body");
            list = Ui.El(left, "port-list");

            var row = Ui.El(left, "btn-row");
            refreshBtn = Ui.Btn(row, "ACTUALIZAR", () => { Scan(); Sfx.I.Play("click"); });
            Ui.El(row, "gap");
            connectBtn = Ui.Btn(row, "CONECTAR  →", Connect, "btn--primary");
            connectBtn.style.flexGrow = 1;

            status = Ui.El(left, "status");
            statusText = Ui.Text(status, "", "status-text");

            var demo = Ui.Btn(left, "Probar sin hardware (modo demostración con teclado)", () => { hub.StartDemo(); }, "btn--ghost", "btn--sm");
            demo.style.marginTop = 10;
            demo.style.alignSelf = Align.FlexStart;

            var right = Ui.El(root, "col-right");
            var guide = Ui.El(right, "panel", "guide");
            Ui.Text(guide, "CÓMO SE CONECTA", "panel-title");
            diagram = new LinkDiagram(584, 150);
            diagram.style.marginBottom = 12;
            guide.Add(diagram);
            Step(guide, "1", "Por cable: ", "conecta el ESP32 del control por USB. Aparece con la etiqueta USB.");
            Step(guide, "2", "Por Bluetooth: ", "en Windows abre Bluetooth y dispositivos y empareja «CarroRM-Control». Aparecen puertos BLUETOOTH: usa el de salida (normalmente el número menor).");
            Step(guide, "3", "Un programa a la vez: ", "cierra el Monitor Serie de Arduino antes de conectar.");
        }

        static void Step(VisualElement p, string n, string bold, string text)
        {
            var s = Ui.El(p, "guide-step");
            Ui.Text(s, n, "guide-num");
            var t = Ui.El(s);
            t.style.flexShrink = 1;
            t.style.flexDirection = FlexDirection.Row;
            t.style.flexWrap = Wrap.Wrap;
            var l = Ui.Text(t, "<b>" + bold + "</b>" + text, "guide-text");
            l.enableRichText = true;
        }

        public override void OnShow()
        {
            app.ToShowcase(true);
            Scan();
            SetStatus(null, "");
            lastPhase = hub.Phase;
            if (hub.Phase == LinkPhase.Failed) ShowFail();
        }

        void Scan()
        {
            ports = PortScanner.Scan();
            if (selected == null || !ports.Any(p => p.name == selected))
            {
                var best = ports.FirstOrDefault(p => p.kind == PortKind.Usb || p.kind == PortKind.Bluetooth);
                selected = best.name ?? ports.FirstOrDefault().name;
            }
            list.Clear();
            if (ports.Count == 0)
            {
                var e = Ui.El(list, "empty");
                Ui.Text(e, "No hay puertos disponibles. Conecta el control por USB o empareja el Bluetooth y pulsa Actualizar.", "small");
            }
            foreach (var p in ports)
            {
                var item = Ui.El(list, "port");
                Ui.Toggle(item, "port--sel", p.name == selected);
                var radio = Ui.El(item, "port-radio");
                Ui.El(radio, "port-radio-in");
                Ui.Text(item, p.name, "port-name");
                Ui.Text(item, string.IsNullOrEmpty(p.device) ? "" : p.device.Replace("\\Device\\", ""), "port-dev");
                var badge = Ui.Text(item, PortScanner.KindLabel(p.kind), "badge");
                badge.AddToClassList(p.kind == PortKind.Bluetooth ? "badge--bt" : p.kind == PortKind.Usb ? "badge--usb" : "badge--sys");
                var name = p.name;
                item.RegisterCallback<ClickEvent>(e =>
                {
                    selected = name;
                    Sfx.I.Play("click");
                    foreach (var c in list.Children()) Ui.Toggle(c, "port--sel", false);
                    item.AddToClassList("port--sel");
                    if (e.clickCount == 2) Connect();
                });
            }
            tScan = 0;
        }

        void Connect()
        {
            var p = ports.FirstOrDefault(x => x.name == selected);
            if (string.IsNullOrEmpty(p.name)) { SetStatus("err", "Selecciona un puerto de la lista."); return; }
            hub.Connect(p);
        }

        void SetStatus(string kind, string text)
        {
            status.RemoveFromClassList("status--info");
            status.RemoveFromClassList("status--err");
            status.RemoveFromClassList("status--ok");
            if (kind != null) status.AddToClassList("status--" + kind);
            statusText.text = text;
        }

        void ShowFail()
        {
            string port = selected ?? "el puerto";
            string msg;
            switch (hub.FailReason)
            {
                case "busy": msg = $"{port} está ocupado. Cierra el Monitor Serie u otro programa que lo esté usando."; break;
                case "car": msg = $"{port} es el ESP32 del carro. Conecta el ESP32 del control (el del joystick)."; break;
                case "unknown": msg = $"El dispositivo en {port} respondió, pero no habla el protocolo del control. Carga CONTROLLER.ino v2."; break;
                case "silent": msg = $"Sin respuesta en {port}. Revisa que el control tenga el firmware v2 y esté encendido."; break;
                case "lost": msg = "Se perdió la conexión con el control. Verifica el cable o el Bluetooth y vuelve a conectar."; break;
                default: msg = $"No se pudo abrir {port}. Si es Bluetooth, verifica que el control esté encendido y emparejado."; break;
            }
            SetStatus("err", msg);
            Sfx.I.Play("error");
        }

        public override void Tick(float dt)
        {
            var ph = hub.Phase;
            bool busy = ph == LinkPhase.Opening || ph == LinkPhase.Handshake;
            connectBtn.SetEnabled(!busy && ph != LinkPhase.Ready);
            refreshBtn.SetEnabled(!busy);

            if (ph != lastPhase)
            {
                if (ph == LinkPhase.Opening) SetStatus("info", $"Abriendo {selected}…" + (hub.Kind == PortKind.Bluetooth ? " (Bluetooth puede tardar unos segundos)" : ""));
                else if (ph == LinkPhase.Handshake) SetStatus("info", hub.IsDemo ? "Iniciando modo demostración…" : "Puerto abierto. Esperando respuesta del control…");
                else if (ph == LinkPhase.Ready)
                {
                    SetStatus("ok", hub.IsDemo ? "Modo demostración: W A S D mueven el joystick, Espacio es el botón." : $"Conectado a {hub.PortName} · firmware {hub.Firmware}");
                    Sfx.I.Play("connect");
                    tReady = 0;
                }
                else if (ph == LinkPhase.Failed) ShowFail();
                lastPhase = ph;
            }

            if (ph == LinkPhase.Ready)
            {
                tReady += dt;
                if (tReady > (hub.IsDemo ? 1.6f : 0.9f) && !app.autoshots) app.Show(app.menu);
            }
            else if (!busy)
            {
                tScan += dt;
                if (tScan > 2.5f)
                {
                    var now = PortScanner.Scan();
                    if (now.Count != ports.Count || now.Any(n => !ports.Any(p => p.name == n.name))) Scan();
                    tScan = 0;
                }
            }
            diagram.carLink = true;
        }
    }

    // menu principal
    public class MenuScreen : UiScreen
    {
        StickView stick;
        Label mode, car, rate, best;
        VisualElement firstCard;

        protected override void Build()
        {
            root.AddToClassList("menu");
            var left = Ui.El(root);
            Ui.Text(left, "PASO 2 · EXPERIENCIA", "eyebrow");
            Ui.Text(left, "Elige cómo jugar", "h1");
            firstCard = Card(left, 0, "MANUAL · JOYSTICK", "Misión manual", "Recoge celdas de energía en 90 s. Turbos, aceite y choques se sienten en el carro real.", () => app.Show(app.hud));
            best = firstCard.Q<Label>("best");
            Card(left, 1, "AUTO · RUTINA", "Modo automático", "El carro ejecuta su rutina y el gemelo digital la replica paso a paso.", () => app.Show(app.auto));
            Card(left, 2, "PRUEBAS", "Diagnóstico", "Prueba cada señal: LED, sonido, freno, vibración y velocidad.", () => app.Show(app.diag));

            var row = Ui.El(left, "btn-row");
            row.style.marginTop = 8;
            Ui.Btn(row, "DESCONECTAR", () => { hub.Disconnect(); app.Show(app.connect); }, "btn--ghost", "btn--sm");
            Ui.Btn(row, "SALIR", () => app.Quit(), "btn--ghost", "btn--sm");

            var right = Ui.El(root);
            right.style.flexGrow = 1;
            right.style.justifyContent = Justify.FlexEnd;
            right.style.alignItems = Align.FlexEnd;
            var tele = Ui.El(right, "panel", "tele");
            Ui.Text(tele, "TELEMETRÍA EN VIVO · HARDWARE → UNITY", "panel-title");
            var trow = Ui.El(tele);
            trow.style.flexDirection = FlexDirection.Row;
            trow.style.alignItems = Align.Center;
            stick = new StickView(150);
            trow.Add(stick);
            var vals = Ui.El(trow);
            vals.style.flexGrow = 1;
            vals.style.marginLeft = 26;
            mode = Row(vals, "Modo del control");
            car = Row(vals, "Carro");
            rate = Row(vals, "Datos recibidos");
            var hint = Ui.Text(tele, "Pulsa el botón del joystick para cambiar entre manual y automático.", "small");
            hint.style.marginTop = 16;
        }

        VisualElement Card(VisualElement p, int kind, string tag, string title, string desc, System.Action go)
        {
            var b = new Button(() => { Sfx.I.Play("click"); go(); });
            b.AddToClassList("mode-card");
            b.RegisterCallback<MouseEnterEvent>(_ => Sfx.I.Play("hover"));
            b.Add(new ModeIcon(kind));
            var body = Ui.El(b, "mode-body");
            var tg = Ui.Text(body, tag, "label-caps", "mode-tag");
            tg.style.color = kind == 0 ? Pal.Amber : kind == 1 ? Pal.Cyan : Pal.Violet;
            Ui.Text(body, title, "mode-title");
            Ui.Text(body, desc, "mode-desc");
            if (kind == 0)
            {
                var bl = Ui.Text(body, "", "small");
                bl.name = "best";
                bl.style.marginTop = 4;
                bl.style.color = Pal.Amber;
            }
            Ui.Text(b, "›", "mode-go");
            p.Add(b);
            return b;
        }

        static Label Row(VisualElement p, string key)
        {
            var r = Ui.El(p, "tele-row");
            Ui.Text(r, key, "tele-key");
            return Ui.Text(r, "—", "tele-val");
        }

        public override void OnShow()
        {
            app.ToShowcase(true);
            hub.SetSpeed(0);
            hub.SetLed(Led.On, "Conectado");
            hub.SetCarLed(Led.Off);
            int b = PlayerPrefs.GetInt("best", 0);
            best.text = b > 0 ? "Récord: " + b + " pts" : "";
            firstCard.Focus();
            Sfx.I.Music(0.22f);
        }

        public override void Tick(float dt)
        {
            stick.value = new Vector2(hub.JoyY, hub.JoyX) / 1000f;
            mode.text = hub.Mode == 1 ? "MANUAL" : "AUTOMÁTICO";
            mode.EnableInClassList("tele-val--amber", hub.Mode == 1);
            mode.EnableInClassList("tele-val--cyan", hub.Mode == 0);
            car.text = hub.CarLink ? (hub.IsDemo ? "SIMULADO" : "ENLAZADO") : "SIN ENLACE";
            car.EnableInClassList("tele-val--green", hub.CarLink);
            car.EnableInClassList("tele-val--red", !hub.CarLink);
            rate.text = Mathf.RoundToInt(hub.RxHz) + " Hz";
        }

        public override void OnBack() { }
    }

    // hud de la mision
    public class HudScreen : UiScreen
    {
        Label timer, score, cells, hits, countdown, effectText;
        VisualElement timeFill, effect, effectPill, modal, feedList;
        Label modalBody;
        StickView stick;
        MotorView motors;
        GaugeView gauge;
        readonly List<Label> popups = new List<Label>();
        int popupNext, lastCount = -1;
        Mission m => app.mission;
        float tFinish;
        Dictionary<string, int> startStats;

        protected override void Build()
        {
            var sb = Ui.El(root, "score-box");
            Ui.Text(sb, "PUNTAJE", "label-caps");
            score = Ui.Text(sb, "0", "score");
            var sub = Ui.El(sb, "score-sub");
            var ci = new CellIcon(18, 26, Pal.Cyan);
            ci.AddToClassList("cell-ico");
            sub.Add(ci);
            cells = Ui.Text(sub, "0", "cell-count");
            hits = Ui.Text(sub, "CHOQUES 0", "hits-count");

            var top = Ui.El(root, "hud-top");
            timer = Ui.Text(top, "01:30", "timer");
            var bar = Ui.El(top, "timebar");
            timeFill = Ui.El(bar, "timebar-fill");

            effect = Ui.El(root, "effect");
            effectPill = Ui.El(effect, "effect-pill");
            effectText = Ui.Text(effectPill, "", "effect-text");

            countdown = Ui.Text(root, "", "countdown");
            for (int i = 0; i < 3; i++)
            {
                var p = Ui.Text(root, "", "popup", "popup--go");
                p.pickingMode = PickingMode.Ignore;
                popups.Add(p);
            }

            Ui.Text(root, "ESC · pausa", "hint");

            var feed = Ui.El(root, "panel", "feed");
            Ui.Text(feed, "RETROALIMENTACIÓN · UNITY → HARDWARE", "panel-title");
            feedList = Ui.El(feed);
            feedList.style.height = 160;

            var dash = Ui.El(root, "panel", "dash");
            stick = new StickView(120);
            Block(dash, stick, "JOYSTICK");
            motors = new MotorView(100, 120);
            Block(dash, motors, "MOTORES I · D");
            gauge = new GaugeView(150);
            Block(dash, gauge, "VELOCIDAD DEL CARRO");

            modal = Ui.El(root, "layer", "modal");
            var card = Ui.El(modal, "panel", "modal-card");
            Ui.Text(card, "Pausa", "h1");
            modalBody = Ui.Text(card, "", "body");
            modalBody.style.marginBottom = 18;
            Ui.Btn(card, "REANUDAR", () => Resume(), "btn--primary");
            Ui.Btn(card, "REINICIAR MISIÓN", () => { Ui.Show(modal, false); Start(); });
            Ui.Btn(card, "SALIR AL MENÚ", () => { m.Abort(); app.Show(app.menu); }, "btn--ghost");
            Ui.Show(modal, false);
        }

        static void Block(VisualElement p, VisualElement w, string cap)
        {
            var b = Ui.El(p, "dash-block");
            b.Add(w);
            Ui.Text(b, cap, "dash-cap");
        }

        void Start()
        {
            startStats = new Dictionary<string, int>(hub.Stats);
            m.Begin();
            tFinish = 0;
            lastCount = -1;
            feedList.Clear();
        }

        public Dictionary<string, int> MissionSignals()
        {
            var d = new Dictionary<string, int>();
            foreach (var k in new[] { "LED", "SONIDO", "CARRO" })
            {
                hub.Stats.TryGetValue(k, out var now);
                int before = 0;
                startStats?.TryGetValue(k, out before);
                d[k] = now - before;
            }
            return d;
        }

        public override void OnShow()
        {
            app.ToShowcase(false);
            m.Popup += OnPopup;
            m.Finished += OnFinished;
            hub.Feedback += OnFeedback;
            hub.Button += OnButton;
            Ui.Show(modal, false);
            Sfx.I.Music(0.08f);
            Start();
        }

        public override void OnHide()
        {
            m.Popup -= OnPopup;
            m.Finished -= OnFinished;
            hub.Feedback -= OnFeedback;
            hub.Button -= OnButton;
            Time.timeScale = 1;
        }

        void OnFinished() { tFinish = 0.001f; }

        void OnButton(int mode)
        {
            if (mode == 0 && m.state == Mission.State.Running) Pause("Cambiaste a modo automático desde el control. Pulsa Reanudar o el botón del joystick para volver a manual.");
            else if (mode == 1 && m.state == Mission.State.Paused) Resume();
        }

        void Pause(string why)
        {
            m.Pause(true);
            modalBody.text = why;
            Ui.Show(modal, true);
            modal.Q<Button>().Focus();
        }

        void Resume()
        {
            Ui.Show(modal, false);
            m.Pause(false);
        }

        public override void OnBack()
        {
            if (m.state == Mission.State.Running) Pause("El carro real está detenido mientras la misión está en pausa.");
            else if (m.state == Mission.State.Paused) Resume();
        }

        void OnPopup(string text, string color)
        {
            var p = popups[popupNext++ % popups.Count];
            p.text = text;
            foreach (var c in new[] { "cyan", "amber", "violet", "red" }) p.RemoveFromClassList("popup--" + c);
            p.AddToClassList("popup--" + color);
            p.RemoveFromClassList("popup--go");
            p.style.transitionDuration = new List<TimeValue> { new TimeValue(0) };
            p.schedule.Execute(() =>
            {
                p.style.transitionDuration = StyleKeyword.Null;
                p.AddToClassList("popup--go");
            }).ExecuteLater(60);
        }

        void OnFeedback(string kind, string label)
        {
            var item = Ui.El("feed-item", "feed-item--new");
            var tag = Ui.Text(item, kind, "feed-tag");
            tag.AddToClassList("feed-tag--" + kind);
            Ui.Text(item, label, "feed-text");
            feedList.Insert(0, item);
            item.schedule.Execute(() => item.RemoveFromClassList("feed-item--new")).ExecuteLater(20);
            while (feedList.childCount > 4) feedList.RemoveAt(feedList.childCount - 1);
        }

        public override void Tick(float dt)
        {
            int secs = Mathf.CeilToInt(m.TimeLeft);
            timer.text = $"{secs / 60:00}:{secs % 60:00}";
            timer.EnableInClassList("timer--warn", secs <= 10);
            timeFill.style.width = Length.Percent(m.TimeLeft / Mission.Duration * 100f);
            timeFill.style.backgroundColor = secs <= 10 ? Pal.Red : Pal.Amber;
            score.text = m.Score.ToString("N0").Replace(",", " ");
            cells.text = m.Cells.ToString();
            hits.text = "CHOQUES " + m.Hits;

            if (m.state == Mission.State.Countdown)
            {
                int n = Mathf.CeilToInt(m.Countdown);
                if (n != lastCount)
                {
                    lastCount = n;
                    countdown.text = n.ToString();
                    countdown.RemoveFromClassList("countdown--pop");
                    countdown.schedule.Execute(() => countdown.AddToClassList("countdown--pop")).ExecuteLater(500);
                }
            }
            else if (lastCount != 0)
            {
                lastCount = 0;
                countdown.text = m.state == Mission.State.Running ? "¡YA!" : "";
                countdown.RemoveFromClassList("countdown--pop");
                countdown.schedule.Execute(() => { countdown.AddToClassList("countdown--pop"); countdown.schedule.Execute(() => countdown.text = "").ExecuteLater(300); }).ExecuteLater(350);
            }

            bool boost = m.BoostT > 0, oil = m.OilT > 0;
            Ui.Show(effect, boost || oil);
            effectPill.EnableInClassList("effect-pill--amber", boost);
            effectPill.EnableInClassList("effect-pill--violet", oil);
            if (boost) effectText.text = $"TURBO · CARRO AL 100 %  {m.BoostT:0.0} s";
            if (oil) effectText.text = $"ACEITE · CARRO AL 35 % + VIBRACIÓN  {m.OilT:0.0} s";

            stick.value = new Vector2(hub.JoyY, hub.JoyX) / 1000f;
            motors.a = hub.CarLink ? hub.MotA : app.twin.TargetA;
            motors.b = hub.CarLink ? hub.MotB : app.twin.TargetB;
            int sp = m.SpeedNow;
            gauge.value = sp / 100f;
            gauge.val.text = sp + "%";
            gauge.sub.text = boost ? "TURBO" : oil ? "ACEITE" : m.state == Mission.State.Running ? "NORMAL" : "DETENIDO";
            gauge.color = boost ? Pal.Amber : oil ? Pal.Violet : Pal.Cyan;

            if (tFinish > 0)
            {
                tFinish += dt;
                if (tFinish > 1.4f) app.Show(app.results);
            }
        }
    }

    // resultados
    public class ResultsScreen : UiScreen
    {
        Label eyebrow, score, sCells, sHits, sBoost, sBest, nLed, nSnd, nCar;
        StarsView stars;

        protected override void Build()
        {
            root.AddToClassList("results");
            var card = Ui.El(root, "panel", "results-card");
            eyebrow = Ui.Text(card, "MISIÓN COMPLETA", "eyebrow");
            stars = new StarsView();
            card.Add(stars);
            score = Ui.Text(card, "0", "h1");
            score.style.fontSize = 96;
            score.style.marginBottom = 0;
            Ui.Text(card, "PUNTOS", "label-caps");
            var stats = Ui.El(card, "stats");
            sCells = Stat(stats, "CELDAS");
            sHits = Stat(stats, "CHOQUES");
            sBoost = Stat(stats, "TURBOS");
            sBest = Stat(stats, "RÉCORD");
            var sig = Ui.El(card, "signals");
            Ui.Text(sig, "Señales enviadas al hardware", "small");
            nLed = Signal(sig, "LED");
            nSnd = Signal(sig, "SONIDO");
            nCar = Signal(sig, "CARRO");
            var row = Ui.El(card, "btn-row");
            Ui.Btn(row, "JUGAR DE NUEVO", () => app.Show(app.hud), "btn--primary").style.width = 320;
            Ui.El(row, "gap");
            Ui.Btn(row, "MENÚ", () => app.Show(app.menu)).style.width = 200;
        }

        static Label Stat(VisualElement p, string key)
        {
            var s = Ui.El(p, "stat");
            var v = Ui.Text(s, "0", "stat-val");
            Ui.Text(s, key, "stat-key");
            return v;
        }

        static Label Signal(VisualElement p, string kind)
        {
            var s = Ui.El(p, "signal");
            var t = Ui.Text(s, kind, "feed-tag", "feed-tag--" + kind);
            t.style.marginRight = 0;
            return Ui.Text(s, "0", "signal-n");
        }

        public override void OnShow()
        {
            var m = app.mission;
            app.ToShowcase(true);
            eyebrow.text = m.NewBest ? "¡NUEVO RÉCORD!" : "MISIÓN COMPLETA";
            score.text = m.Score.ToString();
            sCells.text = m.Cells.ToString();
            sHits.text = m.Hits.ToString();
            sBoost.text = m.Boosts.ToString();
            sBest.text = m.Best.ToString();
            stars.count = m.Score >= 1600 ? 3 : m.Score >= 1000 ? 2 : m.Score >= 400 ? 1 : 0;
            stars.MarkDirtyRepaint();
            var s = app.hud.MissionSignals();
            nLed.text = s["LED"].ToString();
            nSnd.text = s["SONIDO"].ToString();
            nCar.text = s["CARRO"].ToString();
            root.Q<Button>().Focus();
            Sfx.I.Music(0.22f);
        }

        public override void OnBack() { app.Show(app.menu); }
    }

    // modo automatico
    public class AutoScreen : UiScreen
    {
        readonly List<VisualElement> rows = new List<VisualElement>();
        readonly List<VisualElement> fills = new List<VisualElement>();
        Label source, current, remaining;
        MotorView motors;
        VisualElement feedList;
        AutoMode a => app.autoMode;

        protected override void Build()
        {
            root.AddToClassList("auto");
            var left = Ui.El(root, "panel", "steps");
            Ui.Text(left, "PASO 2 · MODO AUTOMÁTICO", "eyebrow");
            Ui.Text(left, "Rutina del carro", "h2");
            var src = Ui.El(left, "source");
            source = Ui.Text(src, "", "small");
            for (int i = 0; i < AutoRoutine.Steps.Length; i++)
            {
                var s = AutoRoutine.Steps[i];
                var r = Ui.El(left, "step");
                var f = Ui.El(r, "step-fill");
                Ui.Text(r, (i + 1).ToString("00"), "step-n");
                Ui.Text(r, s.name, "step-name");
                Ui.Text(r, s.duration.ToString("0.0") + " s", "step-dur");
                rows.Add(r);
                fills.Add(f);
            }
            var row = Ui.El(left, "btn-row");
            row.style.marginTop = 16;
            Ui.Btn(row, "REINICIAR RUTINA", () => a.Restart(), "btn--primary").style.flexGrow = 1;
            Ui.El(row, "gap");
            Ui.Btn(row, "VOLVER", () => app.Show(app.menu));

            var top = Ui.El(root, "hud-top");
            current = Ui.Text(top, "", "timer");
            current.style.fontSize = 48;
            remaining = Ui.Text(top, "", "label-caps");

            var feed = Ui.El(root, "panel", "feed");
            feed.style.left = StyleKeyword.Auto;
            feed.style.right = 56;
            feed.style.bottom = 230;
            Ui.Text(feed, "RETROALIMENTACIÓN · UNITY → HARDWARE", "panel-title");
            feedList = Ui.El(feed);
            feedList.style.height = 160;

            var dash = Ui.El(root, "panel", "dash");
            motors = new MotorView(100, 120);
            var b = Ui.El(dash, "dash-block");
            b.Add(motors);
            Ui.Text(b, "MOTORES I · D", "dash-cap");
        }

        public override void OnShow()
        {
            app.ToShowcase(false);
            hub.Feedback += OnFeedback;
            feedList.Clear();
            a.Enter();
            Sfx.I.Music(0.1f);
            root.Q<Button>().Focus();
        }

        public override void OnHide()
        {
            hub.Feedback -= OnFeedback;
            a.Exit();
        }

        void OnFeedback(string kind, string label)
        {
            var item = Ui.El("feed-item");
            Ui.Text(item, kind, "feed-tag", "feed-tag--" + kind);
            Ui.Text(item, label, "feed-text");
            feedList.Insert(0, item);
            while (feedList.childCount > 4) feedList.RemoveAt(feedList.childCount - 1);
        }

        public override void Tick(float dt)
        {
            source.text = a.Simulated
                ? "Sin telemetría del carro: el gemelo sigue la rutina por tiempo."
                : "Datos en vivo: motores y paso reportados por el carro (ESP-NOW).";
            source.style.color = a.Simulated ? Pal.Amber : Pal.Green;
            for (int i = 0; i < rows.Count; i++)
            {
                bool now = i == a.Step, done = a.Step != 255 && i < a.Step || a.Done;
                rows[i].EnableInClassList("step--now", now);
                rows[i].EnableInClassList("step--done", done && !now);
                fills[i].style.width = Length.Percent(now ? a.StepProgress * 100f : 0);
            }
            if (a.Step != 255)
            {
                current.text = $"PASO {a.Step + 1} · {AutoRoutine.Steps[a.Step].name.ToUpper()}";
                float left = (1 - a.StepProgress) * AutoRoutine.Steps[a.Step].duration;
                for (int i = a.Step + 1; i < AutoRoutine.Steps.Length; i++) left += AutoRoutine.Steps[i].duration;
                remaining.text = $"QUEDAN {left:0} s DE RUTINA";
            }
            else
            {
                current.text = a.Done ? "RUTINA COMPLETA" : hub.Mode == 1 ? "CONTROL EN MANUAL" : "ESPERANDO AL CARRO";
                remaining.text = a.Done ? "PULSA REINICIAR PARA REPETIR" : "";
            }
            motors.a = app.twin.TargetA;
            motors.b = app.twin.TargetB;
        }

        public override void OnBack() { app.Show(app.menu); }
    }

    // diagnostico de entradas y salidas
    public class DiagScreen : UiScreen
    {
        StickView stick;
        MotorView motors;
        Label x, y, mode, car, rate, btn, motorVals, speedVal;
        VisualElement log;
        RangeSlider slider;
        float lastBtn = -1;

        protected override void Build()
        {
            root.AddToClassList("diag");

            var c1 = Ui.El(root, "panel", "diag-col");
            Ui.Text(c1, "ENTRADAS · HARDWARE → UNITY", "panel-title");
            var top = Ui.El(c1);
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            stick = new StickView(200);
            top.Add(stick);
            motors = new MotorView(90, 160);
            motors.style.marginLeft = 24;
            top.Add(motors);
            x = Row(c1, "Joystick X (adelante −)");
            y = Row(c1, "Joystick Y (derecha +)");
            btn = Row(c1, "Botón del joystick");
            mode = Row(c1, "Modo del control");
            car = Row(c1, "Enlace ESP-NOW con el carro");
            motorVals = Row(c1, "PWM motores I / D");
            rate = Row(c1, "Paquetes por segundo");

            var c2 = Ui.El(root, "panel", "diag-col");
            c2.style.marginLeft = 20;
            c2.style.marginRight = 20;
            Ui.Text(c2, "SALIDAS · UNITY → HARDWARE", "panel-title");
            LedGroup(c2, "LED DEL CONTROL (D19)", p => hub.SetLed(p, "LED control: " + LedName(p)));
            LedGroup(c2, "LED DEL CARRO (GPIO2)", p => hub.SetCarLed(p, "LED carro: " + LedName(p)));
            var g3 = Group(c2, "ZUMBADOR DEL CONTROL (D21, opcional)");
            Ui.Btn(g3, "GRAVE", () => hub.Tone(220, 300, "Tono 220 Hz"), "btn--sm");
            Ui.Btn(g3, "MEDIO", () => hub.Tone(880, 200, "Tono 880 Hz"), "btn--sm");
            Ui.Btn(g3, "AGUDO", () => hub.Tone(1760, 150, "Tono 1760 Hz"), "btn--sm");
            Ui.Btn(g3, "MELODÍA", () => hub.Melody(new[] { 523, 659, 784, 1046 }, 160, "Melodía"), "btn--sm");
            var g4 = Group(c2, "CARRO");
            Ui.Btn(g4, "FRENO 400 ms", () => hub.Brake(400), "btn--sm");
            Ui.Btn(g4, "VIBRACIÓN 450 ms", () => hub.Shake(450), "btn--sm");
            Ui.Btn(g4, "MANUAL", () => hub.SetMode(1), "btn--sm");
            Ui.Btn(g4, "AUTO", () => hub.SetMode(0), "btn--sm");
            var g5 = Ui.El(c2, "group");
            var hdr = Ui.El(g5);
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.justifyContent = Justify.SpaceBetween;
            Ui.Text(hdr, "VELOCIDAD PERMITIDA DEL CARRO", "label-caps");
            speedVal = Ui.Text(hdr, "0 %", "tele-val", "tele-val--amber");
            slider = new RangeSlider(0);
            g5.Add(slider);
            slider.Changed += v => speedVal.text = v + " %";
            slider.Released += v => hub.SetSpeed((int)v, "Velocidad " + v + "%");

            var c3 = Ui.El(root, "diag-col");
            var lp = Ui.El(c3, "panel");
            Ui.Text(lp, "REGISTRO SERIAL", "panel-title");
            log = Ui.El(lp, "log");
            log.style.height = 520;
            var back = Ui.Btn(c3, "VOLVER AL MENÚ", () => app.Show(app.menu));
            back.style.marginTop = 16;
        }

        static string LedName(int p) => new[] { "apagado", "fijo", "lento", "rápido", "destello" }[p];

        VisualElement Group(VisualElement p, string title)
        {
            var g = Ui.El(p, "group");
            Ui.Text(g, title, "label-caps").style.marginBottom = 8;
            return Ui.El(g, "group-row");
        }

        void LedGroup(VisualElement p, string title, System.Action<int> act)
        {
            var g = Group(p, title);
            string[] n = { "APAGADO", "FIJO", "LENTO", "RÁPIDO", "DESTELLO" };
            for (int i = 0; i < n.Length; i++)
            {
                int k = i;
                Ui.Btn(g, n[i], () => act(k), "btn--sm");
            }
        }

        static Label Row(VisualElement p, string key)
        {
            var r = Ui.El(p, "tele-row");
            Ui.Text(r, key, "tele-key");
            return Ui.Text(r, "—", "tele-val");
        }

        public override void OnShow()
        {
            app.ToShowcase(true);
            hub.SetMode(1);
            hub.SetSpeed(0);
            slider.value = 0;
            speedVal.text = "0 %";
            hub.Button += OnBtn;
            root.Q<Button>().Focus();
        }

        public override void OnHide()
        {
            hub.Button -= OnBtn;
            hub.SetSpeed(0);
            hub.SetLed(Led.Off);
            hub.SetCarLed(Led.Off);
        }

        void OnBtn(int m) { lastBtn = Time.unscaledTime; }

        public override void Tick(float dt)
        {
            stick.value = new Vector2(hub.JoyY, hub.JoyX) / 1000f;
            motors.a = hub.MotA;
            motors.b = hub.MotB;
            x.text = hub.JoyX.ToString();
            y.text = hub.JoyY.ToString();
            btn.text = lastBtn < 0 ? "sin pulsar" : Time.unscaledTime - lastBtn < 1.2f ? "¡PULSADO!" : $"hace {Time.unscaledTime - lastBtn:0} s";
            btn.EnableInClassList("tele-val--cyan", lastBtn >= 0 && Time.unscaledTime - lastBtn < 1.2f);
            mode.text = hub.Mode == 1 ? "MANUAL" : "AUTOMÁTICO";
            car.text = hub.CarLink ? "ACTIVO" : "SIN ENLACE";
            car.EnableInClassList("tele-val--green", hub.CarLink);
            car.EnableInClassList("tele-val--red", !hub.CarLink);
            motorVals.text = $"{hub.MotA} / {hub.MotB}";
            rate.text = Mathf.RoundToInt(hub.RxHz) + " Hz";

            if (Time.frameCount % 10 == 0)
            {
                var lines = hub.RxLog.Select(l => (l, false)).Concat(hub.TxLog.Select(l => (l, true)))
                    .OrderBy(t => t.Item1.Substring(0, 10)).ToList();
                log.Clear();
                foreach (var (l, tx) in lines.Skip(Mathf.Max(0, lines.Count - 23)))
                {
                    var parts = l.Split(new[] { "  " }, 2, System.StringSplitOptions.None);
                    var t = Ui.Text(log, parts[0] + (tx ? "   → " : "   ← ") + (parts.Length > 1 ? parts[1] : ""), "log-line");
                    if (tx) t.AddToClassList("log-line--tx");
                }
            }
        }

        public override void OnBack() { app.Show(app.menu); }
    }
}
