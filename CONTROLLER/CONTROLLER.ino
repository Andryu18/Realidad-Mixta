/*
   control esp32 - rally mixto
   puente entre unity (usb o bluetooth) y el carro (esp-now)

   joystick       esp32
   vrx  ........  d35
   vry  ........  d34
   sw   ........  d18
   vcc  ........  3v3
   gnd  ........  gnd

   led rojo: d19 -> 220-330 ohm -> led -> gnd
   zumbador (opcional): d21 -> zumbador -> gnd

   placa: esp32 dev module
   particion: huge app (3mb no ota/1mb spiffs)

   boton del joystick: cambia de modo (led encendido = rc)
*/

#include <esp_now.h>
#include <WiFi.h>
#include <esp_wifi.h>
#include <BluetoothSerial.h>
#include <esp_coexist.h>

#define JOY_X_PIN  35
#define JOY_Y_PIN  34
#define JOY_SW_PIN 18
#define LED_PIN    19
#define BUZZER_PIN 21

#define BT_NAME  "CarroRM-Control"
#define FW_VER   "2.0"

// modos (igual que el carro)
#define MODE_AUTO 0
#define MODE_RC   1

// ordenes para el carro
#define CMD_NONE    0
#define CMD_BRAKE   1
#define CMD_SHAKE   2
#define CMD_RESTART 3

const uint8_t START_MODE = MODE_AUTO;

uint8_t receiverAddress[] = { 0xec, 0xc9, 0xff, 0xfe, 0x85, 0xc8 };  // mac del carro

const int DEAD_ZONE = 150;

const unsigned long T_SEND       = 50;
const unsigned long T_DEBOUNCE   = 40;
const unsigned long T_LINK       = 500;
const unsigned long T_UNITY_IDLE = 3000;

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

typedef struct __attribute__((packed)) {
  int16_t motA;
  int16_t motB;
  uint8_t mode;
  uint8_t step;
  uint8_t state;
  uint8_t speed;
  uint32_t uptime;
} car_packet;

BluetoothSerial SerialBT;

ctrl_packet out = {};
uint8_t mode = START_MODE;
int16_t joyX = 0;
int16_t joyY = 0;

bool btnState = HIGH;
bool lastReading = HIGH;
unsigned long tBtnChange = 0;
unsigned long tLastSend = 0;

// estado de unity
unsigned long tUnity = 0;
uint8_t unitySpeed = 100;
uint8_t unityLed = 0;
uint8_t carLed = 0;
uint8_t flashPrev = 0;
unsigned long tFlashEnd = 0;

// telemetria del carro
portMUX_TYPE rxMux = portMUX_INITIALIZER_UNLOCKED;
volatile car_packet tele = {};
volatile unsigned long tTele = 0;

bool unityActive() {
  return tUnity > 0 && millis() - tUnity < T_UNITY_IDLE;
}

void onReceive(const esp_now_recv_info_t *info, const uint8_t *data, int len) {
  if (len != sizeof(car_packet)) return;
  portENTER_CRITICAL(&rxMux);
  memcpy((void *)&tele, data, len);
  tTele = millis();
  portEXIT_CRITICAL(&rxMux);
}

int16_t applyDeadZone(int v) {
  if (abs(v) <= DEAD_ZONE) return 0;
  if (v > 0) return map(v, DEAD_ZONE, 1000, 0, 1000);
  return map(v, -1000, -DEAD_ZONE, -1000, 0);
}

// salida hacia unity por usb y bluetooth
void sendLine(const char *line) {
  Serial.println(line);
  if (SerialBT.hasClient()) SerialBT.println(line);
}

// zumbador
unsigned long tToneEnd = 0;

void playTone(int freq, int ms) {
  if (freq <= 0 || ms <= 0) return;
  ledcWriteTone(BUZZER_PIN, freq);
  tToneEnd = millis() + ms;
}

void updateTone() {
  if (tToneEnd && millis() >= tToneEnd) {
    ledcWriteTone(BUZZER_PIN, 0);
    tToneEnd = 0;
  }
}

// led del control
void updateLed() {
  if (tFlashEnd && millis() >= tFlashEnd) {
    unityLed = flashPrev;
    tFlashEnd = 0;
  }

  uint8_t p = unityActive() ? unityLed : (mode == MODE_RC ? 1 : 0);
  unsigned long t = millis();
  unsigned long c = t % 1000;
  bool on = false;
  switch (p) {
    case 1: on = true; break;
    case 2: on = (t / 500) % 2; break;
    case 3: on = (t / 120) % 2; break;
    case 4: on = c < 80 || (c > 160 && c < 240); break;
  }
  digitalWrite(LED_PIN, on);
}

void queueCmd(uint8_t cmd, uint16_t ms) {
  out.cmd = cmd;
  out.cmdMs = ms;
  out.cmdId++;
  tLastSend = millis() - T_SEND;
}

void setMode(uint8_t m) {
  if (m != MODE_AUTO && m != MODE_RC) return;
  mode = m;
  tLastSend = millis() - T_SEND;
}

// comunicacion esp con unity
void handleLine(char *line) {
  tUnity = millis();
  char c = line[0];
  char *arg = strchr(line, ',');
  int a = 0, b = 0;
  if (arg) {
    a = atoi(arg + 1);
    char *arg2 = strchr(arg + 1, ',');
    if (arg2) b = atoi(arg2 + 1);
  }

  switch (c) {
    case '?':
      sendLine("H,CarroRM," FW_VER);
      break;
    case 'M':
      setMode(a);
      break;
    case 'S':
      unitySpeed = constrain(a, 0, 100);
      break;
    case 'L':
      if (a == 4) {
        if (!tFlashEnd) flashPrev = unityLed;
        tFlashEnd = millis() + 1000;
      } else {
        tFlashEnd = 0;
      }
      unityLed = constrain(a, 0, 4);
      break;
    case 'C':
      carLed = constrain(a, 0, 4);
      break;
    case 'T':
      playTone(a, b);
      break;
    case 'F':
      queueCmd(CMD_BRAKE, constrain(a, 50, 3000));
      break;
    case 'V':
      queueCmd(CMD_SHAKE, constrain(a, 50, 3000));
      break;
    case 'R':
      queueCmd(CMD_RESTART, 0);
      break;
  }
}

// lectura de lineas de un puerto
void readStream(Stream &s, char *buf, int &len) {
  while (s.available()) {
    char ch = s.read();
    if (ch == '\r') continue;
    if (ch == '\n') {
      buf[len] = 0;
      if (len > 0) handleLine(buf);
      len = 0;
    } else if (len < 31) {
      buf[len++] = ch;
    }
  }
}

char usbBuf[32];
int usbLen = 0;
char btBuf[32];
int btLen = 0;

void readButton() {
  bool reading = digitalRead(JOY_SW_PIN);

  if (reading != lastReading) {
    lastReading = reading;
    tBtnChange = millis();
  }

  if ((millis() - tBtnChange) >= T_DEBOUNCE && reading != btnState) {
    btnState = reading;
    if (btnState == LOW) {
      mode = (mode == MODE_RC) ? MODE_AUTO : MODE_RC;
      sendLine(mode == MODE_RC ? "E,BTN,1" : "E,BTN,0");
      tLastSend = millis() - T_SEND;
    }
  }
}

void setup() {
  Serial.begin(115200);

  pinMode(JOY_SW_PIN, INPUT_PULLUP);
  pinMode(LED_PIN, OUTPUT);
  btnState = lastReading = digitalRead(JOY_SW_PIN);
  ledcAttach(BUZZER_PIN, 2000, 8);
  ledcWriteTone(BUZZER_PIN, 0);

  // bluetooth primero para que siempre sea visible
  SerialBT.begin(BT_NAME);

  WiFi.mode(WIFI_STA);
  WiFi.disconnect();

  esp_wifi_set_promiscuous(true);
  esp_wifi_set_channel(1, WIFI_SECOND_CHAN_NONE);
  esp_wifi_set_promiscuous(false);

  if (esp_now_init() != ESP_OK) {
    Serial.println("error iniciando esp-now");
    while (1);
  }
  esp_now_register_recv_cb(onReceive);

  esp_now_peer_info_t peerInfo = {};
  memcpy(peerInfo.peer_addr, receiverAddress, 6);
  peerInfo.channel = 1;
  peerInfo.encrypt = false;
  if (esp_now_add_peer(&peerInfo) != ESP_OK) {
    Serial.println("error agregando el carro");
    while (1);
  }

  // prioridad de radio al bluetooth
  esp_coex_preference_set(ESP_COEX_PREFER_BT);
  sendLine("H,CarroRM," FW_VER);
}

void loop() {
  readStream(Serial, usbBuf, usbLen);
  readStream(SerialBT, btBuf, btLen);
  readButton();
  updateLed();
  updateTone();

  if (millis() - tLastSend < T_SEND) return;
  tLastSend = millis();

  int mappedX = map(analogRead(JOY_X_PIN), 0, 4095, 1000, -1000);
  int mappedY = map(analogRead(JOY_Y_PIN), 0, 4095, 1000, -1000);
  joyX = applyDeadZone(mappedX);
  joyY = applyDeadZone(mappedY);

  bool unity = unityActive();
  out.x = mode == MODE_RC ? joyX : 0;
  out.y = mode == MODE_RC ? joyY : 0;
  out.mode = mode;
  out.speed = unity ? unitySpeed : 100;
  out.led = unity ? carLed : 0;

  esp_now_send(receiverAddress, (uint8_t *)&out, sizeof(out));

  car_packet t;
  portENTER_CRITICAL(&rxMux);
  memcpy(&t, (const void *)&tele, sizeof(t));
  unsigned long tt = tTele;
  portEXIT_CRITICAL(&rxMux);
  bool link = tt > 0 && millis() - tt < T_LINK;

  // d,x,y,modo,enlace,motA,motB,paso,estado,velocidad
  char line[64];
  snprintf(line, sizeof(line), "D,%d,%d,%d,%d,%d,%d,%d,%d,%d",
           joyX, joyY, mode, link ? 1 : 0,
           link ? t.motA : 0, link ? t.motB : 0,
           link ? t.step : 255, link ? t.state : 0, out.speed);
  sendLine(line);
}
