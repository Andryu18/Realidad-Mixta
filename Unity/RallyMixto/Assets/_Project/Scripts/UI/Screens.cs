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

        protected override void Build()
        {
            root.AddToClassList("menu");
            var left = Ui.El(root);
            Ui.Text(left, "Menú", "h1");
            first = Card(left, "Modo manual", "Maneja el carro con el joystick. El gemelo lo sigue.", () => app.Show(app.manualScreen));
            Card(left, "Modo automático", "El carro hace su rutina y el gemelo la replica.", () => app.Show(app.auto));

            var row = Ui.El(left, "btn-row");
            row.style.marginTop = 8;
            Ui.Btn(row, "DESCONECTAR", () => { hub.Disconnect(); app.Show(app.connect); }, "btn--ghost", "btn--sm");
            Ui.Btn(row, "SALIR", () => app.Quit(), "btn--ghost", "btn--sm");
        }

        Button Card(VisualElement p, string title, string desc, System.Action go)
        {
            var b = new Button(go);
            b.AddToClassList("mode-card");
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
            first.Focus();
        }
    }

    // registro serial en la esquina inferior derecha
    public class SerialLogView
    {
        const int Rows = 9;
        static readonly string[] States = { "normal", "rutina", "fin", "frenando", "vibrando" };
        readonly Label head, tele;
        readonly Label[] rows = new Label[Rows];
        int ver = -1;

        public SerialLogView(VisualElement parent)
        {
            var box = Ui.El(parent, "serial-log");
            head = Ui.Text(box, "", "serial-head");
            tele = Ui.Text(box, "", "serial-tele");
            for (int i = 0; i < Rows; i++) rows[i] = Ui.Text(box, "", "serial-row");
        }

        public void Tick()
        {
            var h = Hub.I;
            head.text = $"SERIAL · {h.PortName} · {h.RxHz:0} Hz · {(h.CarLink ? "CARRO ENLAZADO" : "SIN ENLACE")}";
            string st = h.CarState >= 0 && h.CarState < States.Length ? States[h.CarState] : h.CarState.ToString();
            tele.text = $"motA {h.MotA,4}  motB {h.MotB,4}  vel {h.Speed}%  {st}";
            if (ver == h.LogVersion) return;
            ver = h.LogVersion;
            var log = h.SerialLog;
            for (int i = 0; i < Rows; i++)
            {
                int k = log.Count - Rows + i;
                var line = k >= 0 ? log[k] : "";
                rows[i].text = line;
                Ui.Toggle(rows[i], "serial-row--tx", line.Contains("  > "));
            }
        }
    }

    // modo manual
    public class ManualScreen : UiScreen
    {
        Label status;
        SerialLogView log;
        ManualMode m => app.manual;

        protected override void Build()
        {
            var top = Ui.El(root, "hud-top");
            var t = Ui.Text(top, "MODO MANUAL", "timer");
            t.style.fontSize = 48;
            status = Ui.Text(top, "", "label-caps");

            var back = Ui.Btn(root, "VOLVER", () => app.Show(app.menu));
            back.style.position = Position.Absolute;
            back.style.left = 56;
            back.style.bottom = 48;
            log = new SerialLogView(root);
        }

        public override void OnShow()
        {
            app.ToShowcase(false);
            m.Enter();
            root.Q<Button>().Focus();
        }

        public override void OnHide() { m.Exit(); }

        public override void Tick(float dt)
        {
            status.text = hub.Mode == 0 ? "EL CONTROL ESTÁ EN AUTOMÁTICO: PULSA EL BOTÓN DEL JOYSTICK" : "";
            log.Tick();
        }

        public override void OnBack() { app.Show(app.menu); }
    }

    // modo automatico
    public class AutoScreen : UiScreen
    {
        Label current;
        SerialLogView log;
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
            log = new SerialLogView(root);
        }

        public override void OnShow()
        {
            app.ToShowcase(false);
            a.Enter();
            root.Q<Button>().Focus();
        }

        public override void OnHide() { a.Exit(); }

        public override void Tick(float dt)
        {
            if (a.Step != 255) current.text = $"PASO {a.Step + 1}/{AutoRoutine.Steps.Length} · {AutoRoutine.Steps[a.Step].name.ToUpper()}";
            else current.text = a.Done ? "RUTINA COMPLETA" : hub.Mode == 1 ? "CONTROL EN MANUAL" : "ESPERANDO AL CARRO";
            log.Tick();
        }

        public override void OnBack() { app.Show(app.menu); }
    }
}
