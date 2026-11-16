# figuras del informe
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch

AMB = "#E0A11B"; CY = "#1BA9D6"; GR = "#333B48"; LT = "#F3F5F8"; RD = "#D8414F"; VI = "#7F63D9"
plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 9})


def box(ax, x, y, w, h, title, lines, col):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.08", fc=LT, ec=col, lw=1.8))
    ax.text(x + w / 2, y + h - 0.17, title, ha="center", va="top", fontsize=9.5, weight="bold", color=GR)
    for i, l in enumerate(lines):
        ax.text(x + w / 2, y + h - 0.6 - i * 0.36, l, ha="center", va="top", fontsize=7.6, color="#4a5260")


def arrow(ax, x0, y0, x1, y1, col):
    ax.add_patch(FancyArrowPatch((x0, y0), (x1, y1), arrowstyle="-|>", mutation_scale=11, color=col, lw=1.6))


def lab(ax, x, y, t, c):
    ax.text(x, y, t, ha="center", va="center", fontsize=6.9, color=c, linespacing=1.3)


# arquitectura
fig, ax = plt.subplots(figsize=(7.2, 2.6))
ax.set_xlim(0, 10.4); ax.set_ylim(0.4, 3.6); ax.axis("off")
box(ax, 0.1, 0.7, 2.4, 2.6, "Unity (PC)", ["UI propia (UI Toolkit)", "Gemelo digital URP", "Misión / Auto / Diagnóstico", "System.IO.Ports"], CY)
box(ax, 4.0, 0.7, 2.4, 2.6, "ESP32 control", ["Joystick + botón", "LED D19, zumbador D21", "USB + Bluetooth SPP", "ESP-NOW canal 1"], AMB)
box(ax, 7.9, 0.7, 2.4, 2.6, "ESP32 carro", ["L298N + 4 motores TT", "LED GPIO2", "Modos RC y AUTO", "Telemetría 20 Hz"], AMB)
arrow(ax, 3.95, 2.45, 2.55, 2.45, CY); lab(ax, 3.25, 2.95, "D (20 Hz): joystick,\nmodo, motores, paso", CY)
arrow(ax, 2.55, 1.45, 3.95, 1.45, CY); lab(ax, 3.25, 0.95, "L C T F V S M R:\nLED, tono, freno...", CY)
arrow(ax, 6.45, 2.45, 7.85, 2.45, AMB); lab(ax, 7.15, 2.95, "ctrl_packet: x, y,\nmodo, velocidad, orden", AMB)
arrow(ax, 7.85, 1.45, 6.45, 1.45, AMB); lab(ax, 7.15, 0.95, "car_packet:\nmotA, motB, paso", AMB)
fig.tight_layout(); fig.savefig("figs/arquitectura.png", dpi=300); plt.close(fig)

# flujo de pantallas
fig, ax = plt.subplots(figsize=(7.2, 2.3))
ax.set_xlim(0, 10); ax.set_ylim(0, 3.2); ax.axis("off")


def node(x, y, t, col, w=1.55, h=0.62):
    ax.add_patch(FancyBboxPatch((x - w / 2, y - h / 2), w, h, boxstyle="round,pad=0.02,rounding_size=0.12", fc=LT, ec=col, lw=1.6))
    ax.text(x, y, t, ha="center", va="center", fontsize=8, color=GR, weight="bold")


P = {"spl": (0.95, 2.3), "con": (2.85, 2.3), "men": (4.85, 2.3), "mis": (6.95, 2.75), "res": (8.95, 2.75), "aut": (6.95, 1.75), "dia": (6.95, 0.75)}
node(*P["spl"], "Bienvenida", GR); node(*P["con"], "Conexión\n(puerto COM)", CY); node(*P["men"], "Menú", GR)
node(*P["mis"], "Misión manual", AMB); node(*P["res"], "Resultados", GR); node(*P["aut"], "Modo auto", AMB); node(*P["dia"], "Diagnóstico", VI)


def a(p, q, c=GR, rad=0.0):
    ax.add_patch(FancyArrowPatch(p, q, arrowstyle="-|>", mutation_scale=10, color=c, lw=1.2, connectionstyle=f"arc3,rad={rad}", shrinkA=24, shrinkB=24))


a(P["spl"], P["con"]); a(P["con"], P["men"]); a(P["men"], P["mis"]); a(P["men"], P["aut"]); a(P["men"], P["dia"]); a(P["mis"], P["res"])
a(P["res"], P["men"], rad=0.35)
ax.text(2.85, 1.25, "pérdida de enlace\n→ vuelve a Conexión", ha="center", fontsize=7.2, color=RD)
fig.tight_layout(); fig.savefig("figs/flujo.png", dpi=300); plt.close(fig)
print("ok")
