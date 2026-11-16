using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyMixto
{
    public static class Pal
    {
        public static readonly Color Amber = new Color32(255, 194, 61, 255);
        public static readonly Color Cyan = new Color32(54, 214, 255, 255);
        public static readonly Color Red = new Color32(255, 82, 99, 255);
        public static readonly Color Green = new Color32(77, 255, 166, 255);
        public static readonly Color Violet = new Color32(176, 146, 255, 255);
        public static readonly Color Text = new Color32(236, 241, 247, 255);
        public static readonly Color Muted = new Color32(139, 151, 168, 255);
        public static readonly Color Line = new Color(1, 1, 1, 0.12f);
        public static readonly Color Line2 = new Color(1, 1, 1, 0.22f);
    }

    // atajos para construir la interfaz
    public static class Ui
    {
        public static VisualElement El(params string[] cls)
        {
            var e = new VisualElement();
            foreach (var c in cls) e.AddToClassList(c);
            return e;
        }

        public static VisualElement El(VisualElement parent, params string[] cls)
        {
            var e = El(cls);
            parent.Add(e);
            return e;
        }

        public static Label Text(VisualElement parent, string text, params string[] cls)
        {
            var l = new Label(text);
            foreach (var c in cls) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        public static Button Btn(VisualElement parent, string text, Action onClick, params string[] cls)
        {
            var b = new Button(() => { Sfx.I?.Play("click"); onClick?.Invoke(); }) { text = text };
            b.AddToClassList("btn");
            foreach (var c in cls) b.AddToClassList(c);
            b.RegisterCallback<MouseEnterEvent>(_ => { if (b.enabledSelf) Sfx.I?.Play("hover"); });
            parent?.Add(b);
            return b;
        }

        public static void Toggle(VisualElement e, string cls, bool on)
        {
            if (on) e.AddToClassList(cls); else e.RemoveFromClassList(cls);
        }

        public static void Show(VisualElement e, bool on) { e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }
    }

    // elemento con dibujo vectorial propio
    public class Draw : VisualElement
    {
        readonly Action<Painter2D, Rect> paint;

        public Draw(Action<Painter2D, Rect> paint, float w, float h)
        {
            this.paint = paint;
            style.width = w;
            style.height = h;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx => paint(ctx.painter2D, contentRect);
        }

        public static void Circle(Painter2D p, Vector2 c, float r, Color col, float w)
        {
            p.strokeColor = col;
            p.lineWidth = w;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360));
            p.Stroke();
        }

        public static void Disc(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillColor = col;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill();
        }

        public static void Line(Painter2D p, Vector2 a, Vector2 b, Color col, float w)
        {
            p.strokeColor = col;
            p.lineWidth = w;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        public static void ArcStroke(Painter2D p, Vector2 c, float r, float a0, float a1, Color col, float w)
        {
            if (Mathf.Abs(a1 - a0) < 0.1f) return;
            p.strokeColor = col;
            p.lineWidth = w;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(a0), Angle.Degrees(a1));
            p.Stroke();
        }
    }

    // joystick del control en vivo
    public class StickView : VisualElement
    {
        public Vector2 value;
        Vector2 shown;

        public StickView(float size)
        {
            style.width = size;
            style.height = size;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
            schedule.Execute(() =>
            {
                if (panel == null) return;
                shown = Vector2.Lerp(shown, value, 0.35f);
                MarkDirtyRepaint();
            }).Every(16);
        }

        void Paint(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            var c = r.center;
            float rad = r.width * 0.5f - 4;
            Draw.Disc(p, c, rad, new Color(1, 1, 1, 0.03f));
            Draw.Circle(p, c, rad, Pal.Line2, 2);
            Draw.Circle(p, c, rad * 0.5f, Pal.Line, 1.5f);
            Draw.Line(p, c + Vector2.left * rad, c + Vector2.right * rad, Pal.Line, 1.5f);
            Draw.Line(p, c + Vector2.up * rad, c + Vector2.down * rad, Pal.Line, 1.5f);
            var dot = c + new Vector2(shown.x, shown.y) * (rad - 12);
            Draw.Line(p, c, dot, new Color(Pal.Cyan.r, Pal.Cyan.g, Pal.Cyan.b, 0.5f), 3);
            Draw.Disc(p, dot, 13, new Color(Pal.Cyan.r, Pal.Cyan.g, Pal.Cyan.b, 0.25f));
            Draw.Disc(p, dot, 8, Pal.Cyan);
        }
    }

    // barras de motores izquierdo y derecho (-255..255)
    public class MotorView : VisualElement
    {
        public float a, b;
        float sa, sb;

        public MotorView(float w, float h)
        {
            style.width = w;
            style.height = h;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
            schedule.Execute(() =>
            {
                if (panel == null) return;
                sa = Mathf.Lerp(sa, a, 0.3f);
                sb = Mathf.Lerp(sb, b, 0.3f);
                MarkDirtyRepaint();
            }).Every(16);
        }

        void Paint(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            float bw = r.width * 0.3f, mid = r.height / 2;
            Bar(p, new Rect(r.width * 0.12f, 0, bw, r.height), sa, mid);
            Bar(p, new Rect(r.width * 0.58f, 0, bw, r.height), sb, mid);
        }

        static void Bar(Painter2D p, Rect r, float v, float mid)
        {
            p.fillColor = new Color(1, 1, 1, 0.06f);
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
            p.Fill();
            float h = Mathf.Clamp(v / 255f, -1, 1) * (r.height / 2 - 2);
            if (Mathf.Abs(h) > 0.5f)
            {
                p.fillColor = v >= 0 ? Pal.Amber : Pal.Red;
                float y0 = mid, y1 = mid - h;
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMin, Mathf.Min(y0, y1))); p.LineTo(new Vector2(r.xMax, Mathf.Min(y0, y1)));
                p.LineTo(new Vector2(r.xMax, Mathf.Max(y0, y1))); p.LineTo(new Vector2(r.xMin, Mathf.Max(y0, y1)));
                p.ClosePath();
                p.Fill();
            }
            Draw.Line(p, new Vector2(r.xMin - 4, mid), new Vector2(r.xMax + 4, mid), Pal.Line2, 1.5f);
        }
    }

    // medidor circular de velocidad permitida
    public class GaugeView : VisualElement
    {
        public float value;
        public Color color = Pal.Amber;
        float shown;
        public readonly Label val, sub;

        public GaugeView(float size)
        {
            style.width = size;
            style.height = size;
            generateVisualContent += Paint;
            val = Ui.Text(this, "0", "gauge-val");
            sub = Ui.Text(this, "", "gauge-sub");
            schedule.Execute(() =>
            {
                if (panel == null) return;
                shown = Mathf.Lerp(shown, value, 0.2f);
                MarkDirtyRepaint();
            }).Every(16);
        }

        void Paint(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            var c = r.center;
            float rad = r.width / 2 - 10;
            Draw.ArcStroke(p, c, rad, 150, 390, Pal.Line, 10);
            Draw.ArcStroke(p, c, rad, 150, 150 + 240 * Mathf.Clamp01(shown), color, 10);
            for (int i = 0; i <= 10; i++)
            {
                float a = (150 + 24 * i) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Draw.Line(p, c + d * (rad - 16), c + d * (rad - (i % 5 == 0 ? 26 : 21)), Pal.Line2, 2);
            }
        }
    }

    // marca grafica: anillo de joystick con ondas esp-now
    public class LogoMark : VisualElement
    {
        float t;

        public LogoMark(float size, bool animate = true)
        {
            style.width = size;
            style.height = size;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
            if (animate) schedule.Execute(() => { if (panel == null) return; t += 0.016f; MarkDirtyRepaint(); }).Every(16);
        }

        void Paint(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            float s = r.width;
            var c = r.center;
            Draw.Circle(p, c, s * 0.42f, Pal.Amber, s * 0.06f);
            Draw.Disc(p, c, s * 0.15f, Pal.Amber);
            for (int i = 0; i < 3; i++)
            {
                float k = (t * 0.6f + i / 3f) % 1f;
                var col = new Color(Pal.Cyan.r, Pal.Cyan.g, Pal.Cyan.b, 1 - k);
                float rr = s * (0.2f + k * 0.28f);
                Draw.ArcStroke(p, c, rr, -50, 50, col, s * 0.035f);
                Draw.ArcStroke(p, c, rr, 130, 230, col, s * 0.035f);
            }
        }
    }

    // diagrama del enlace pc - control - carro
    public class LinkDiagram : VisualElement
    {
        float t;
        public bool usb = true, bt = true, carLink = true;

        public LinkDiagram(float w, float h)
        {
            style.width = w;
            style.height = h;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
            schedule.Execute(() => { if (panel == null) return; t += 0.016f; MarkDirtyRepaint(); }).Every(16);
            var row = Ui.El(this);
            row.style.position = Position.Absolute;
            row.style.left = 0; row.style.right = 0; row.style.bottom = 0;
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            foreach (var s in new[] { "UNITY · PC", "CONTROL ESP32", "CARRO ESP32" })
            {
                var l = Ui.Text(row, s, "label-caps");
                l.style.width = w / 3;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
            }
            var mid = Ui.El(this);
            mid.style.position = Position.Absolute;
            mid.style.left = 0; mid.style.right = 0; mid.style.top = 6;
            mid.style.flexDirection = FlexDirection.Row;
            var l1 = Ui.Text(mid, "USB / BLUETOOTH", "label-caps");
            l1.style.width = w / 2; l1.style.unityTextAlign = TextAnchor.MiddleCenter; l1.style.color = Pal.Cyan; l1.style.fontSize = 13;
            var l2 = Ui.Text(mid, "ESP-NOW", "label-caps");
            l2.style.width = w / 2; l2.style.unityTextAlign = TextAnchor.MiddleCenter; l2.style.color = Pal.Amber; l2.style.fontSize = 13;
        }

        void Paint(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            float y = r.height * 0.45f;
            var n0 = new Vector2(r.width / 6, y);
            var n1 = new Vector2(r.width / 2, y);
            var n2 = new Vector2(r.width * 5 / 6, y);
            Dashes(p, n0, n1, Pal.Cyan, t, true);
            Dashes(p, n1, n2, carLink ? Pal.Amber : Pal.Red, t, carLink);
            Node(p, n0, Pal.Cyan, 0);
            Node(p, n1, Pal.Amber, 1);
            Node(p, n2, Pal.Amber, 2);
        }

        static void Node(Painter2D p, Vector2 c, Color col, int kind)
        {
            Draw.Disc(p, c, 34, new Color(0.05f, 0.07f, 0.1f, 1));
            Draw.Circle(p, c, 34, col, 2.5f);
            if (kind == 0)
            {
                Draw.Line(p, c + new Vector2(-13, -8), c + new Vector2(13, -8), col, 3);
                Draw.Line(p, c + new Vector2(-13, -8), c + new Vector2(-13, 8), col, 3);
                Draw.Line(p, c + new Vector2(13, -8), c + new Vector2(13, 8), col, 3);
                Draw.Line(p, c + new Vector2(-13, 8), c + new Vector2(13, 8), col, 3);
                Draw.Line(p, c + new Vector2(-6, 14), c + new Vector2(6, 14), col, 3);
            }
            else if (kind == 1)
            {
                Draw.Circle(p, c, 13, col, 3);
                Draw.Disc(p, c, 5, col);
            }
            else
            {
                Draw.Line(p, c + new Vector2(-14, 4), c + new Vector2(14, 4), col, 3);
                Draw.Line(p, c + new Vector2(-10, -6), c + new Vector2(10, -6), col, 3);
                Draw.Line(p, c + new Vector2(-10, -6), c + new Vector2(-14, 4), col, 3);
                Draw.Line(p, c + new Vector2(10, -6), c + new Vector2(14, 4), col, 3);
                Draw.Disc(p, c + new Vector2(-8, 10), 4, col);
                Draw.Disc(p, c + new Vector2(8, 10), 4, col);
            }
        }

        static void Dashes(Painter2D p, Vector2 a, Vector2 b, Color col, float t, bool alive)
        {
            var d = (b - a);
            float len = d.magnitude - 80;
            var dir = d.normalized;
            var s = a + dir * 40;
            float seg = 14, gap = 10, off = alive ? (t * 40f) % (seg + gap) : 0;
            for (float x = -seg + off; x < len; x += seg + gap)
            {
                float x0 = Mathf.Max(0, x), x1 = Mathf.Min(len, x + seg);
                if (x1 <= x0) continue;
                Draw.Line(p, s + dir * x0, s + dir * x1, new Color(col.r, col.g, col.b, alive ? 0.9f : 0.35f), 3);
            }
        }
    }

    // estrellas de resultado
    public class StarsView : VisualElement
    {
        public int count;

        public StarsView()
        {
            AddToClassList("stars");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                var r = contentRect;
                for (int i = 0; i < 3; i++)
                {
                    var c = new Vector2(r.width * (0.2f + 0.3f * i), r.height * (i == 1 ? 0.45f : 0.55f));
                    Star(p, c, i == 1 ? 40 : 32, i < count ? Pal.Amber : new Color(1, 1, 1, 0.1f));
                }
            };
        }

        static void Star(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillColor = col;
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = (-90 + i * 36) * Mathf.Deg2Rad;
                float rr = i % 2 == 0 ? r : r * 0.45f;
                var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
        }
    }

    // icono de celda de energia
    public class CellIcon : VisualElement
    {
        public CellIcon(float w, float h, Color col)
        {
            style.width = w;
            style.height = h;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                var r = contentRect;
                p.fillColor = col;
                p.BeginPath();
                p.MoveTo(new Vector2(r.center.x, 0));
                p.LineTo(new Vector2(r.width, r.height * 0.4f));
                p.LineTo(new Vector2(r.center.x, r.height));
                p.LineTo(new Vector2(0, r.height * 0.4f));
                p.ClosePath();
                p.Fill();
            };
        }
    }

    // iconos de las tarjetas del menu
    public class ModeIcon : VisualElement
    {
        public ModeIcon(int kind)
        {
            AddToClassList("mode-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx =>
            {
                var p = ctx.painter2D;
                var r = contentRect;
                var c = r.center;
                float s = r.width;
                Draw.Disc(p, c, s / 2, new Color(1, 1, 1, 0.04f));
                Draw.Circle(p, c, s / 2 - 1, Pal.Line2, 1.5f);
                var col = kind == 0 ? Pal.Amber : kind == 1 ? Pal.Cyan : Pal.Violet;
                if (kind == 0)
                {
                    Draw.Circle(p, c, s * 0.24f, col, 4);
                    Draw.Disc(p, c + new Vector2(s * 0.08f, -s * 0.08f), s * 0.09f, col);
                }
                else if (kind == 1)
                {
                    Draw.ArcStroke(p, c, s * 0.25f, -60, 220, col, 4);
                    var tip = c + new Vector2(Mathf.Cos(-60 * Mathf.Deg2Rad), Mathf.Sin(-60 * Mathf.Deg2Rad)) * s * 0.25f;
                    Draw.Line(p, tip, tip + new Vector2(-s * 0.11f, -s * 0.02f), col, 4);
                    Draw.Line(p, tip, tip + new Vector2(s * 0.01f, s * 0.11f), col, 4);
                }
                else
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float x = c.x + (i - 1) * s * 0.18f;
                        Draw.Line(p, new Vector2(x, c.y - s * 0.22f), new Vector2(x, c.y + s * 0.22f), Pal.Line2, 3);
                        float k = new[] { 0.1f, -0.12f, 0.05f }[i];
                        Draw.Disc(p, new Vector2(x, c.y + k * s), s * 0.06f, col);
                    }
                }
            };
        }
    }

    // deslizador propio (0..100)
    public class RangeSlider : VisualElement
    {
        public float value;
        public event Action<float> Changed, Released;
        readonly VisualElement fill, thumb, track;

        public RangeSlider(float initial)
        {
            AddToClassList("slider");
            track = Ui.El(this, "slider-track");
            fill = Ui.El(track, "slider-fill");
            thumb = Ui.El(this, "slider-thumb");
            value = initial;
            RegisterCallback<PointerDownEvent>(e => { this.CapturePointer(e.pointerId); Set(e.localPosition.x); });
            RegisterCallback<PointerMoveEvent>(e => { if (this.HasPointerCapture(e.pointerId)) Set(e.localPosition.x); });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (!this.HasPointerCapture(e.pointerId)) return;
                this.ReleasePointer(e.pointerId);
                Released?.Invoke(value);
            });
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        void Set(float x)
        {
            value = Mathf.Round(Mathf.Clamp01(x / Mathf.Max(1, layout.width)) * 100f);
            Layout();
            Changed?.Invoke(value);
        }

        void Layout()
        {
            fill.style.width = Length.Percent(value);
            thumb.style.left = Length.Percent(value);
        }
    }
}
