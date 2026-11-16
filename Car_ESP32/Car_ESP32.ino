/*
   carro rc esp32 - rally mixto

   esp32          l298n
   d14  ........  ena   (motores izquierdos)
   d27  ........  in1
   d26  ........  in2
   d25  ........  in3
   d33  ........  in4
   d32  ........  enb   (motores derechos)
   gnd  ........  gnd   (comun con la bateria)

   d2   ........  led integrado (retroalimentacion de unity)

   modos (boton del joystick o unity):
     rc   -> manejo con joystick
     auto -> rutina de movimientos (sin sensor)
*/

#include <esp_now.h>
#include <WiFi.h>
#include <esp_wifi.h>

// pines
const int PIN_ENA = 14;
const int PIN_IN1 = 27;
const int PIN_IN2 = 26;
const int PIN_ENB = 32;
const int PIN_IN3 = 25;
const int PIN_IN4 = 33;
const int PIN_LED = 2;

// pwm
const int PWM_FREQ = 1000;
const int PWM_RES  = 8;
const int PWM_CH_A = 0;
const int PWM_CH_B = 1;

// sentido de giro
const bool INVERT_A = true;
const bool INVERT_B = false;

// velocidad
const int SPEED    = 254;
const int SPEED_RC = 255;
const int TRIM_A   = 100;
const int TRIM_B   = 100;

// arranque
const int PWM_MIN    = 70;
const int PWM_KICK   = 255;
const int T_KICK     = 60;
const int T_DEADTIME = 120;

// tiempos rutina
const unsigned long T_START_DELAY = 3000;
const unsigned long T_PAUSE       = 1000;
const unsigned long T_FORWARD     = 5000;
const unsigned long T_TURN        = 2500;
const unsigned long T_REVERSE     = 3000;

const unsigned long T_FAILSAFE  = 500;
const unsigned long T_TELEMETRY = 50;

// vibracion
const int SHAKE_PWM = 190;
const unsigned long T_SHAKE_HALF = 45;

// modos (igual que el control)
#define MODE_AUTO    0
#define MODE_RC      1
#define MODE_NO_LINK 255

// ordenes de unity
#define CMD_NONE    0
#define CMD_BRAKE   1
#define CMD_SHAKE   2
#define CMD_RESTART 3

// estado del carro
#define ST_IDLE  0
#define ST_AUTO  1
#define ST_DONE  2
#define ST_BRAKE 3
#define ST_SHAKE 4

// paquete del control (v1 = 5 bytes)
typedef struct __attribute__((packed)) {
  int16_t x;
  int16_t y;
  uint8_t mode;
  uint8_t speed;
  uint8_t cmd;
  uint8_t cmdId;
  uint16_t cmdMs;
  uint8_t led;
} ctrl_packet;

// telemetria hacia el control
typedef struct __attribute__((packed)) {
  int16_t motA;
  int16_t motB;
  uint8_t mode;
  uint8_t step;
  uint8_t state;
  uint8_t speed;
  uint32_t uptime;
} car_packet;

const int PACKET_V1 = 5;

// core esp32 2.x y 3.x
#if defined(ESP_ARDUINO_VERSION_MAJOR) && ESP_ARDUINO_VERSION_MAJOR >= 3
#define PWM_INIT(pin, ch) ledcAttach((pin), PWM_FREQ, PWM_RES)
#define PWM_WRITE(pin, ch, val) ledcWrite((pin), (val))
#else
#define PWM_INIT(pin, ch) \
  do { \
    ledcSetup((ch), PWM_FREQ, PWM_RES); \
    ledcAttachPin((pin), (ch)); \
  } while (0)
#define PWM_WRITE(pin, ch, val) ledcWrite((ch), (val))
#endif

int sign(int v) {
  if (v > 0) return 1;
  if (v < 0) return -1;
  return 0;
}

// limite de velocidad de unity (0-100)
uint8_t speedLimit = 100;

int applyTrim(int pwm, int trim) {
  if (pwm <= 0 || speedLimit == 0) return 0;
  int v = (int)(((long)pwm * trim) / 100);
  v = (int)(((long)v * speedLimit) / 100);
  return constrain(v, PWM_MIN, 255);
}

void channelA(int speed) {
  if (INVERT_A) speed = -speed;
  int pwm = applyTrim(abs(speed), TRIM_A);
  digitalWrite(PIN_IN1, pwm > 0 && speed > 0);
  digitalWrite(PIN_IN2, pwm > 0 && speed < 0);
  PWM_WRITE(PIN_ENA, PWM_CH_A, pwm);
}

void channelB(int speed) {
  if (INVERT_B) speed = -speed;
  int pwm = applyTrim(abs(speed), TRIM_B);
  digitalWrite(PIN_IN3, pwm > 0 && speed > 0);
  digitalWrite(PIN_IN4, pwm > 0 && speed < 0);
  PWM_WRITE(PIN_ENB, PWM_CH_B, pwm);
}

int curSpeedA = 0;
int curSpeedB = 0;

void drive(int spdA, int spdB) {
  bool flipA = (curSpeedA > 0 && spdA < 0) || (curSpeedA < 0 && spdA > 0);
  bool flipB = (curSpeedB > 0 && spdB < 0) || (curSpeedB < 0 && spdB > 0);

  // pausa al cambiar de sentido
  if (flipA || flipB) {
    channelA(0);
    channelB(0);
    delay(T_DEADTIME);
    curSpeedA = 0;
    curSpeedB = 0;
  }

  // impulso al arrancar desde cero
  bool startA = (curSpeedA == 0 && spdA != 0);
  bool startB = (curSpeedB == 0 && spdB != 0);
  if ((startA || startB) && speedLimit > 0) {
    channelA(startA ? sign(spdA) * PWM_KICK : spdA);
    channelB(startB ? sign(spdB) * PWM_KICK : spdB);
    delay(T_KICK);
  }

  channelA(spdA);
  channelB(spdB);
  curSpeedA = spdA;
  curSpeedB = spdB;
}

void brake() {
  digitalWrite(PIN_IN1, HIGH);
  digitalWrite(PIN_IN2, HIGH);
  digitalWrite(PIN_IN3, HIGH);
  digitalWrite(PIN_IN4, HIGH);
  PWM_WRITE(PIN_ENA, PWM_CH_A, 255);
  PWM_WRITE(PIN_ENB, PWM_CH_B, 255);
  curSpeedA = 0;
  curSpeedB = 0;
}

void stopMotors() { drive(0, 0); }
void forward()    { drive(SPEED, SPEED); }
void backward()   { drive(-SPEED, -SPEED); }
void turnRight()  { drive(SPEED, -SPEED); }
void turnLeft()   { drive(-SPEED, SPEED); }

// pwm real que recibe cada lado (para el gemelo)
int effective(int spd, int trim) {
  return sign(spd) * applyTrim(abs(spd), trim);
}

// rutina automatica
struct Step {
  void (*action)();
  unsigned long duration;
  const char *name;
};

const Step SEQUENCE[] = {
  { stopMotors, T_START_DELAY, "espera" },
  { forward,    T_FORWARD,     "avanza" },
  { stopMotors, T_PAUSE,       "pausa" },
  { turnRight,  T_TURN,        "gira derecha" },
  { stopMotors, T_PAUSE,       "pausa" },
  { forward,    T_FORWARD,     "avanza" },
  { stopMotors, T_PAUSE,       "pausa" },
  { turnLeft,   T_TURN,        "gira izquierda" },
  { stopMotors, T_PAUSE,       "pausa" },
  { backward,   T_REVERSE,     "retrocede" },
};
const int NUM_STEPS = sizeof(SEQUENCE) / sizeof(SEQUENCE[0]);

int curStep = 0;
unsigned long tStepStart = 0;
bool seqDone = true;

void startStep(int i) {
  curStep = i;
  Serial.printf("[auto] paso %d/%d: %s\n", i + 1, NUM_STEPS, SEQUENCE[i].name);
  SEQUENCE[i].action();
  tStepStart = millis();
}

void startAuto() {
  seqDone = false;
  startStep(0);
}

void updateAuto() {
  if (seqDone) return;
  if (millis() - tStepStart < SEQUENCE[curStep].duration) return;

  if (curStep + 1 < NUM_STEPS) {
    startStep(curStep + 1);
  } else {
    stopMotors();
    seqDone = true;
    Serial.println("[auto] fin de rutina");
  }
}

// mezcla tipo tanque
void driveRC(int16_t x, int16_t y) {
  int throttle = -x;
  int turn     = y;

  int motorA = constrain(throttle + turn, -1000, 1000);
  int motorB = constrain(throttle - turn, -1000, 1000);

  motorA = map(motorA, -1000, 1000, -SPEED_RC, SPEED_RC);
  motorB = map(motorB, -1000, 1000, -SPEED_RC, SPEED_RC);
  drive(motorA, motorB);
}

// retroalimentacion: freno y vibracion
uint8_t fxState = ST_IDLE;
unsigned long tFxStart = 0;
unsigned long tFxLen = 0;

void startFx(uint8_t cmd, uint16_t ms) {
  tFxStart = millis();
  tFxLen = ms;
  if (cmd == CMD_BRAKE) {
    fxState = ST_BRAKE;
    brake();
    Serial.printf("[unity] freno %u ms\n", ms);
  } else {
    fxState = ST_SHAKE;
    Serial.printf("[unity] vibracion %u ms\n", ms);
  }
}

// true mientras el efecto este activo
bool updateFx() {
  if (fxState == ST_IDLE) return false;
  unsigned long t = millis() - tFxStart;

  if (t >= tFxLen) {
    fxState = ST_IDLE;
    channelA(0);
    channelB(0);
    curSpeedA = 0;
    curSpeedB = 0;
    // la rutina recupera el tiempo perdido
    if (!seqDone) {
      tStepStart += tFxLen;
      SEQUENCE[curStep].action();
    }
    return false;
  }

  if (fxState == ST_SHAKE) {
    bool fwd = ((t / T_SHAKE_HALF) % 2) == 0;
    int pwm = fwd ? SHAKE_PWM : -SHAKE_PWM;
    uint8_t keep = speedLimit;
    speedLimit = 100;
    channelA(pwm);
    channelB(pwm);
    speedLimit = keep;
  }
  return true;
}

// led integrado
uint8_t ledPattern = 0;

void updateLed() {
  unsigned long t = millis();
  unsigned long c = t % 1000;
  bool on = false;
  switch (ledPattern) {
    case 1: on = true; break;
    case 2: on = (t / 500) % 2; break;
    case 3: on = (t / 120) % 2; break;
    case 4: on = c < 80 || (c > 160 && c < 240); break;
  }
  digitalWrite(PIN_LED, on);
}

// esp-now
portMUX_TYPE rxMux = portMUX_INITIALIZER_UNLOCKED;
volatile ctrl_packet rx;
volatile unsigned long tLastPacket = 0;
volatile bool newData = false;
uint8_t ctrlMac[6];
volatile bool ctrlKnown = false;
bool peerAdded = false;

// solo guarda datos, los motores van en loop()
void onReceive(const esp_now_recv_info_t *info, const uint8_t *data, int len) {
  if (len != sizeof(ctrl_packet) && len != PACKET_V1) return;

  ctrl_packet msg = {};
  memcpy(&msg, data, len);
  if (len == PACKET_V1) msg.speed = 100;
  if (msg.mode != MODE_AUTO && msg.mode != MODE_RC) return;

  portENTER_CRITICAL(&rxMux);
  memcpy((void *)&rx, &msg, sizeof(msg));
  tLastPacket = millis();
  newData = true;
  if (!ctrlKnown) {
    memcpy(ctrlMac, info->src_addr, 6);
    ctrlKnown = true;
  }
  portEXIT_CRITICAL(&rxMux);
}

uint8_t mode = MODE_NO_LINK;
uint8_t lastCmdId = 0;
bool failsafeOn = false;

// telemetria de vuelta al control
void sendTelemetry() {
  static unsigned long tLast = 0;
  if (!ctrlKnown || millis() - tLast < T_TELEMETRY) return;
  tLast = millis();

  if (!peerAdded) {
    esp_now_peer_info_t peer = {};
    memcpy(peer.peer_addr, ctrlMac, 6);
    peer.channel = 1;
    peer.encrypt = false;
    peerAdded = esp_now_add_peer(&peer) == ESP_OK;
    if (!peerAdded) return;
  }

  bool shaking = fxState == ST_SHAKE;
  car_packet tx;
  tx.motA = shaking ? 0 : effective(curSpeedA, TRIM_A);
  tx.motB = shaking ? 0 : effective(curSpeedB, TRIM_B);
  tx.mode = mode;
  tx.step = seqDone ? 255 : curStep;
  tx.state = fxState != ST_IDLE ? fxState : (seqDone ? ST_IDLE : ST_AUTO);
  tx.speed = speedLimit;
  tx.uptime = millis();
  esp_now_send(ctrlMac, (uint8_t *)&tx, sizeof(tx));
}

void setup() {
  Serial.begin(115200);

  pinMode(PIN_IN1, OUTPUT);
  pinMode(PIN_IN2, OUTPUT);
  pinMode(PIN_IN3, OUTPUT);
  pinMode(PIN_IN4, OUTPUT);
  pinMode(PIN_LED, OUTPUT);

  PWM_INIT(PIN_ENA, PWM_CH_A);
  PWM_INIT(PIN_ENB, PWM_CH_B);
  channelA(0);
  channelB(0);

  WiFi.mode(WIFI_STA);
  WiFi.disconnect();

  // mismo canal que el control
  esp_wifi_set_promiscuous(true);
  esp_wifi_set_channel(1, WIFI_SECOND_CHAN_NONE);
  esp_wifi_set_promiscuous(false);

  if (esp_now_init() != ESP_OK) {
    Serial.println("error iniciando esp-now");
    while (1);
  }
  esp_now_register_recv_cb(onReceive);

  Serial.print("Car MAC: ");
  Serial.println(WiFi.macAddress());
  Serial.println("esperando control...");
}

void loop() {
  ctrl_packet msg;
  portENTER_CRITICAL(&rxMux);
  memcpy(&msg, (const void *)&rx, sizeof(msg));
  unsigned long tPacket = tLastPacket;
  bool isNew = newData;
  newData = false;
  portEXIT_CRITICAL(&rxMux);

  bool linked = tPacket > 0 && (millis() - tPacket) < T_FAILSAFE;

  if (isNew) {
    speedLimit = constrain(msg.speed, 0, 100);
    ledPattern = msg.led;

    // orden nueva de unity
    if (msg.cmdId != lastCmdId) {
      lastCmdId = msg.cmdId;
      if (msg.cmd == CMD_BRAKE || msg.cmd == CMD_SHAKE) {
        startFx(msg.cmd, msg.cmdMs);
      } else if (msg.cmd == CMD_RESTART && msg.mode == MODE_AUTO) {
        startAuto();
      }
    }
  }

  // cambio de modo: primero se detiene
  if (tPacket > 0 && msg.mode != mode) {
    fxState = ST_IDLE;
    stopMotors();
    mode = msg.mode;
    failsafeOn = false;
    if (mode == MODE_RC) {
      seqDone = true;
      Serial.println(">> modo rc");
    } else {
      Serial.println(">> modo auto");
      startAuto();
    }
  }

  updateLed();
  sendTelemetry();

  if (updateFx()) return;

  switch (mode) {
    case MODE_RC:
      if (!linked) {
        if (!failsafeOn) {
          stopMotors();
          failsafeOn = true;
          Serial.println("[rc] sin senal -> stop");
        }
      } else if (isNew) {
        failsafeOn = false;
        driveRC(msg.x, msg.y);
      }
      break;

    case MODE_AUTO:
      updateAuto();
      break;
  }
}
