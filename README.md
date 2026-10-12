# Automóvil RC con ESP32

Carro a control remoto con dos ESP32 comunicados por **ESP-NOW**: uno va en el vehículo y otro en el control con joystick. El carro tiene dos modos:

- **RC (manual):** se maneja con el joystick.
- **Automático:** ejecuta una rutina de movimientos y esquiva obstáculos con un sensor ultrasónico HC-SR04.

Proyecto de Ingeniería Multimedia, Universidad de San Buenaventura.

## Contenido del repositorio

```
├── Car_ESP32/
│   └── Car_ESP32.ino              # ESP32 del vehículo
└── CONTROLLER/
    └── CONTROLLER.ino       # ESP32 del control (joystick)
```

## Materiales

| Vehículo | Control |
|---|---|
| ESP32 | ESP32 |
| Puente H L298N | Joystick analógico (KY-023) |
| 4 motores TT con ruedas | LED rojo + resistencia de 220–330 Ω |
| Sensor HC-SR04 + 3 resistencias de 1 kΩ | |
| Baterías | |

## Conexiones

### Vehículo

| ESP32 | Conecta a |
|---|---|
| D14 | ENA del L298N (motores izquierdos) |
| D27 / D26 | IN1 / IN2 |
| D25 / D33 | IN3 / IN4 |
| D32 | ENB del L298N (motores derechos) |
| D23 | TRIG del HC-SR04 |
| D22 | ECHO del HC-SR04, a través del divisor de voltaje* |
| VIN | 5V del L298N y VCC del HC-SR04 |
| GND | GND del L298N, del HC-SR04 y negativo de las baterías |

\* **Divisor del ECHO:** ECHO → 1 kΩ → D22, y D22 → 2 kΩ (dos de 1 kΩ en serie) → GND. El ECHO entrega 5 V y el ESP32 solo admite 3.3 V.

Antes de conectar ENA y ENB al ESP32, **quita sus jumpers en el L298N**.

### Control

| Joystick / LED | ESP32 |
|---|---|
| VRx | D35 |
| VRy | D34 |
| SW (botón) | D18 |
| VCC | 3V3 |
| GND | GND |
| LED (+), con resistencia | D19 |
| LED (−) | GND |

## Instalación

1. Instala [Arduino IDE](https://www.arduino.cc/en/software) y, desde el **Gestor de placas**, el paquete **esp32 by Espressif** en su **versión 3.3 o más reciente**.
2. Selecciona la placa **ESP32 Dev Module**.
3. Sube `car_v2.ino` al ESP32 del vehículo. Abre el **Monitor Serie a 115200** y copia la MAC que aparece en `Car MAC: XX:XX:XX:XX:XX:XX`.
4. En `controller_v2.ino`, escribe esa MAC en esta línea:
   ```cpp
   uint8_t receiverAddress[] = { 0xXX, 0xXX, 0xXX, 0xXX, 0xXX, 0xXX };
   ```
5. Sube `controller_v2.ino` al ESP32 del control.

## Uso

Enciende el carro y luego el control. El carro no se mueve hasta que recibe datos del control.

**Botón del joystick:** cada pulsación cambia de modo. Con el LED **encendido** está en modo RC; con el LED **apagado**, en modo automático. Al cambiar de modo, el carro siempre se detiene primero.

**Modo RC:**

| Joystick | Movimiento |
|---|---|
| X hacia −1000 | Avanza |
| X hacia +1000 | Retrocede |
| Y hacia +1000 | Gira a la izquierda |
| Y hacia −1000 | Gira a la derecha |

Si el carro deja de recibir datos durante más de 0.5 s, se detiene. En este modo el sensor está desactivado.

**Modo automático:** el carro espera 3 s y ejecuta la rutina: avanza, gira a la derecha, avanza, gira a la izquierda y retrocede, con pausas de 1 s. Si detecta un obstáculo a menos de **25 cm** mientras avanza, frena, retrocede, gira hasta encontrar camino libre y continúa la rutina. Para repetir la rutina, oprime el botón dos veces.

## Parámetros principales

| Archivo | Parámetro | Para qué sirve |
|---|---|---|
| `car_v2.ino` | `SPEED` / `SPEED_RC` | Velocidad en modo automático / velocidad máxima en RC (0–255) |
| `car_v2.ino` | `DIST_STOP` | Distancia (cm) a la que esquiva un obstáculo |
| `car_v2.ino` | `INVERT_A` / `INVERT_B` | Invierte el sentido de giro de cada lado |
| `car_v2.ino` | `TRIM_A` / `TRIM_B` | Compensa si un lado es más rápido que el otro |
| `controller_v2.ino` | `DEAD_ZONE` | Zona muerta del joystick |
| `controller_v2.ino` | `START_MODE` | Modo con el que arranca el control |

## Problemas comunes

| Problema | Solución |
|---|---|
| El control muestra `Delivery Fail` | La MAC de `receiverAddress` no coincide con la del carro |
| El carro no responde a nada | Revisa que el control esté encendido y que los dos tengan el código de este repositorio |
| El sensor siempre marca `999 cm` | Revisa la alimentación del sensor, el divisor del ECHO y que TRIG y ECHO no estén intercambiados |
| El ESP32 se reinicia al arrancar los motores | Carga las baterías o agrega un condensador de 470–1000 µF entre 5V y GND |
| Un lado gira al revés | Cambia `INVERT_A` o `INVERT_B` |

## Autores

Julian Bello, Andres Clavijo, Axl Umaña y Juan Velasco.

## Referencias

- [ESP-NOW — Espressif](https://docs.espressif.com/projects/esp-idf/en/v5.4/esp32/api-reference/network/esp_now.html)
- [ESPNOW RC Car using ESP32 — Aslam Hossain (YouTube)](https://youtu.be/kCbziRhsq-s)
