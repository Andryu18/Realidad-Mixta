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

    // seleccion del puerto del control
    public class ConnectScreen : UiScreen
    {
        VisualElement list, status;
        Label statusText;
        Button connectBtn;
        List<PortInfo> ports = new List<PortInfo>();
        string selected;
        float tScan, tReady;
        LinkPhase lastPhase;

        protected override void Build()
        {
            root.AddToClassList("connect");
            var left = Ui.El(root, "col-left");
            Ui.Text(left, "Conecta el control", "h1");
            Ui.Text(left, "Elige el puerto del ESP32 del control (USB o Bluetooth).", "body");
            list = Ui.El(left, "port-list");

            var row = Ui.El(left, "btn-row");
            Ui.Btn(row, "ACTUALIZAR", Scan);
            Ui.El(row, "gap");
            connectBtn = Ui.Btn(row, "CONECTAR", Connect, "btn--primary");
            connectBtn.style.flexGrow = 1;

            status = Ui.El(left, "status");
            statusText = Ui.Text(status, "", "status-text");

            var demo = Ui.Btn(left, "Probar sin hardware (teclado)", () => hub.StartDemo(), "btn--ghost", "btn--sm");
            demo.style.marginTop = 10;
            demo.style.alignSelf = Align.FlexStart;
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
                Ui.Text(e, "No hay puertos. Conecta el control y pulsa Actualizar.", "small");
            }
            foreach (var p in ports)
            {
                var item = Ui.El(list, "port");
                Ui.Toggle(item, "port--sel", p.name == selected);
                var radio = Ui.El(item, "port-radio");
                Ui.El(radio, "port-radio-in");
                Ui.Text(item, p.name, "port-name");
                Ui.El(item).style.flexGrow = 1;
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
            if (string.IsNullOrEmpty(p.name)) { SetStatus("err", "Selecciona un puerto."); return; }
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
                case "busy": msg = $"{port} está ocupado. Cierra el Monitor Serie."; break;
                case "car": msg = $"{port} es el ESP32 del carro. Conecta el del control."; break;
                case "unknown":
                case "silent": msg = $"Sin respuesta en {port}. Revisa el firmware del control."; break;
                case "lost": msg = "Se perdió la conexión con el control."; break;
                default: msg = $"No se pudo abrir {port}."; break;
            }
            SetStatus("err", msg);
            Sfx.I.Play("error");
        }

        public override void Tick(float dt)
        {
            var ph = hub.Phase;
            bool busy = ph == LinkPhase.Opening || ph == LinkPhase.Handshake;
            connectBtn.SetEnabled(!busy && ph != LinkPhase.Ready);

            if (ph != lastPhase)
            {
                if (busy) SetStatus("info", hub.IsDemo ? "Iniciando demostración…" : $"Conectando a {selected}…");
                else if (ph == LinkPhase.Ready)
                {
                    SetStatus("ok", hub.IsDemo ? "Demostración: W A S D y Espacio." : $"Conectado a {hub.PortName}");
                    Sfx.I.Play("connect");
                    tReady = 0;
                }
                else if (ph == LinkPhase.Failed) ShowFail();
                lastPhase = ph;
            }

            if (ph == LinkPhase.Ready)
            {
                tReady += dt;
                if (tReady > (hub.IsDemo ? 1.4f : 0.8f) && !app.autoshots) app.Show(app.menu);
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
        }
    }

    // menu principal
    public class MenuScreen : UiScreen
    {
        Button first;
        Label best;

        protected override void Build()
        {
            root.AddToClassList("menu");
            var left = Ui.El(root);
            Ui.Text(left, "Elige un modo", "h1");
            first = Card(left, "Misión manual", "Recoge celdas en 90 s con el joystick.", () => app.Show(app.hud));
            best = Ui.Text(first.Q(className: "mode-body"), "", "small");
            best.style.color = Pal.Amber;
            Card(left, "Modo automático", "El carro hace su rutina y el gemelo la replica.", () => app.Show(app.auto));

            var row = Ui.El(left, "btn-row");
            row.style.marginTop = 8;
            Ui.Btn(row, "DESCONECTAR", () => { hub.Disconnect(); app.Show(app.connect); }, "btn--ghost", "btn--sm");
            Ui.Btn(row, "SALIR", () => app.Quit(), "btn--ghost", "btn--sm");
        }

        Button Card(VisualElement p, string title, string desc, System.Action go)
        {
            var b = new Button(() => { Sfx.I.Play("click"); go(); });
            b.AddToClassList("mode-card");
            b.RegisterCallback<MouseEnterEvent>(_ => Sfx.I.Play("hover"));
            var body = Ui.El(b, "mode-body");
            Ui.Text(body, title, "mode-title");
            Ui.Text(body, desc, "mode-desc");
            Ui.Text(b, "›", "mode-go");
            p.Add(b);
            return b;
        }

        public override void OnShow()
        {
            app.ToShowcase(true);
            hub.SetSpeed(0);
            hub.SetLed(Led.On, "Conectado");
            hub.SetCarLed(Led.Off);
            int b = PlayerPrefs.GetInt("best", 0);
            best.text = b > 0 ? "Récord: " + b + " pts" : "";
            first.Focus();
            Sfx.I.Music(0.22f);
        }
    }

    // hud de la mision
    public class HudScreen : UiScreen
    {
        Label timer, score, countdown, effectText;
        VisualElement timeFill, effect, effectPill, modal;
        Label modalBody;
        readonly List<Label> popups = new List<Label>();
        int popupNext, lastCount = -1;
        Mission m => app.mission;
        float tFinish;

        protected override void Build()
        {
            var sb = Ui.El(root, "score-box");
            Ui.Text(sb, "PUNTAJE", "label-caps");
            score = Ui.Text(sb, "0", "score");

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

            modal = Ui.El(root, "layer", "modal");
            var card = Ui.El(modal, "panel", "modal-card");
            Ui.Text(card, "Pausa", "h1");
            modalBody = Ui.Text(card, "", "body");
            modalBody.style.marginBottom = 18;
            Ui.Btn(card, "REANUDAR", Resume, "btn--primary");
            Ui.Btn(card, "SALIR AL MENÚ", () => { m.Abort(); app.Show(app.menu); }, "btn--ghost");
            Ui.Show(modal, false);
        }

        public override void OnShow()
        {
            app.ToShowcase(false);
            m.Popup += OnPopup;
            m.Finished += OnFinished;
            hub.Button += OnButton;
            Ui.Show(modal, false);
            Sfx.I.Music(0.08f);
            m.Begin();
            tFinish = 0;
            lastCount = -1;
        }

        public override void OnHide()
        {
            m.Popup -= OnPopup;
            m.Finished -= OnFinished;
            hub.Button -= OnButton;
            Time.timeScale = 1;
        }

        void OnFinished() { tFinish = 0.001f; }

        void OnButton(int mode)
        {
            if (mode == 0 && m.state == Mission.State.Running) Pause("Cambiaste a automático desde el control.");
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
            if (m.state == Mission.State.Running) Pause("El carro está detenido.");
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

        public override void Tick(float dt)
        {
            int secs = Mathf.CeilToInt(m.TimeLeft);
            timer.text = $"{secs / 60:00}:{secs % 60:00}";
            timer.EnableInClassList("timer--warn", secs <= 10);
            timeFill.style.width = Length.Percent(m.TimeLeft / Mission.Duration * 100f);
            timeFill.style.backgroundColor = secs <= 10 ? Pal.Red : Pal.Amber;
            score.text = m.Score.ToString();

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
                countdown.schedule.Execute(() => countdown.text = "").ExecuteLater(600);
            }

            bool boost = m.BoostT > 0, oil = m.OilT > 0;
            Ui.Show(effect, boost || oil);
            effectPill.EnableInClassList("effect-pill--amber", boost);
            effectPill.EnableInClassList("effect-pill--violet", oil);
            if (boost) effectText.text = "TURBO";
            if (oil) effectText.text = "ACEITE";

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
        Label eyebrow, score, sCells, sHits, sBest;

        protected override void Build()
        {
            root.AddToClassList("results");
            var card = Ui.El(root, "panel", "results-card");
            eyebrow = Ui.Text(card, "MISIÓN COMPLETA", "eyebrow");
            score = Ui.Text(card, "0", "h1");
            score.style.fontSize = 96;
            score.style.marginBottom = 0;
            Ui.Text(card, "PUNTOS", "label-caps");
            var stats = Ui.El(card, "stats");
            sCells = Stat(stats, "CELDAS");
            sHits = Stat(stats, "CHOQUES");
            sBest = Stat(stats, "RÉCORD");
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

        public override void OnShow()
        {
            var m = app.mission;
            app.ToShowcase(true);
            eyebrow.text = m.NewBest ? "¡NUEVO RÉCORD!" : "MISIÓN COMPLETA";
            score.text = m.Score.ToString();
            sCells.text = m.Cells.ToString();
            sHits.text = m.Hits.ToString();
            sBest.text = m.Best.ToString();
            root.Q<Button>().Focus();
            Sfx.I.Music(0.22f);
        }

        public override void OnBack() { app.Show(app.menu); }
    }

    // modo automatico
    public class AutoScreen : UiScreen
    {
        Label current;
        AutoMode a => app.autoMode;

        protected override void Build()
        {
            var top = Ui.El(root, "hud-top");
            current = Ui.Text(top, "", "timer");
            current.style.fontSize = 48;

            var row = Ui.El(root, "btn-row");
            row.style.position = Position.Absolute;
            row.style.left = 56;
            row.style.bottom = 48;
            Ui.Btn(row, "REINICIAR RUTINA", () => a.Restart(), "btn--primary");
            Ui.El(row, "gap");
            Ui.Btn(row, "VOLVER", () => app.Show(app.menu));
        }

        public override void OnShow()
        {
            app.ToShowcase(false);
            a.Enter();
            Sfx.I.Music(0.1f);
            root.Q<Button>().Focus();
        }

        public override void OnHide() { a.Exit(); }

        public override void Tick(float dt)
        {
            if (a.Step != 255) current.text = $"PASO {a.Step + 1}/{AutoRoutine.Steps.Length} · {AutoRoutine.Steps[a.Step].name.ToUpper()}";
            else current.text = a.Done ? "RUTINA COMPLETA" : hub.Mode == 1 ? "CONTROL EN MANUAL" : "ESPERANDO AL CARRO";
        }

        public override void OnBack() { app.Show(app.menu); }
    }
}
