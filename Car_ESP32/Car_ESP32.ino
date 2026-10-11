/*
   Wiring:

     Channel A (ENA/IN1/IN2) -> OUT1/OUT2 -> BOTH LEFT motors
     Channel B (ENB/IN3/IN4) -> OUT3/OUT4 -> BOTH RIGHT motors

     ESP32          L298N
     D14  ........  ENA
     D27  ........  IN1
     D26  ........  IN2
     D25  ........  IN3
     D33  ........  IN4
     D32  ........  ENB
     GND  ........  GND  (common with battery negative)

     ESP32          HC-SR04
     3V3  ........  VCC  (at 5V, ECHO needs a 1k/2k divider)
     GND  ........  GND
     D23  ........  TRIG
     D22  ........  ECHO

   Modes (controller joystick button):
     RC   (LED on)  -> joystick drive, sensor off
     AUTO (LED off) -> sequence + obstacle avoidance

   Joystick (RC):
     X -1000 forward      X +1000 backward
     Y +1000 left         Y -1000 right

   Sequence (about 25 s):
     initial wait          3.0 s
     1) forward            5.0 s
        pause              1.0 s
     2) turn RIGHT         2.5 s   (left fwd / right back)
        pause              1.0 s
     3) forward            5.0 s
        pause              1.0 s
     4) turn LEFT          2.5 s   (right fwd / left back)
        pause              1.0 s
     5) reverse            3.0 s
     6) stop

   Obstacle in AUTO (forward steps only):
     brake -> back up -> spin until clear -> resume step
 */

#include <esp_now.h>
#include <WiFi.h>
#include <esp_wifi.h>

//  Pins
const int PIN_ENA = 14;   // PWM channel A
const int PIN_IN1 = 27;
const int PIN_IN2 = 26;

const int PIN_ENB = 32;   // PWM channel B
const int PIN_IN3 = 25;
const int PIN_IN4 = 33;

const int PIN_TRIG = 23;  // HC-SR04
const int PIN_ECHO = 22;

//  PWM
const int PWM_FREQ = 1000;   // Hz
const int PWM_RES  = 8;      // 8 bits, 0..255
const int PWM_CH_A = 0;      // Core 2.x only
const int PWM_CH_B = 1;

//  MOTOR DIRECTION
// Direction
const bool INVERT_A = true;
const bool INVERT_B = false;

//  SINGLE SPEED
// Same for every maneuver
// Keep margin below 255
const int SPEED = 254;       // 0 to 255

//  PER SIDE TRIM
// 100 means no change
// Lower the faster side
const int TRIM_A = 100;      // Left motors trim
const int TRIM_B = 100;      // Right motors trim

//  Startup and limits
const int PWM_MIN  = 70;
const int PWM_KICK = 255;
const int T_KICK   = 60;
const int T_DEADTIME = 120;

//  Timing
const unsigned long T_START_DELAY = 3000;
const unsigned long T_PAUSE       = 1000;
const unsigned long T_FORWARD     = 5000;
const unsigned long T_TURN        = 2500;
const unsigned long T_REVERSE     = 3000;

//  RC
const int SPEED_RC = 255;     
const unsigned long T_FAILSAFE = 500;  // Stop if no data (ms)

//  Obstacle avoidance (AUTO only)
// Keep DIST_STOP < DIST_FREE < DIST_CLEAR
const bool USE_SENSOR = true;
const int DIST_STOP  = 10;   // cm, brake and avoid
const int DIST_FREE  = 20;   // cm, obstacle gone
const int DIST_CLEAR = 40;   // cm, new heading OK
const unsigned long T_MEASURE    = 60;     // ms readings
const unsigned long ECHO_TIMEOUT = 12000;

const int AVOID_SPEED = 200;               // Back up and spin speed
const unsigned long T_BRAKE      = 150;
const unsigned long T_BACKUP     = 400;
const unsigned long T_SPIN_EXTRA = 200;    // Extra spin to clear the body
const unsigned long T_SPIN_MAX   = 4000;   // Then try the other side

//  Modes (must match controller)
#define MODE_AUTO    0
#define MODE_RC      1
#define MODE_NO_LINK 255

//  Packet (must match controller)
typedef struct __attribute__((packed)) {
  int16_t x;     // -1000 to 1000
  int16_t y;     // -1000 to 1000
  uint8_t mode;
} struct_message;

//  ESP32 core
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


// Returns -1, 0, +1
int sign(int v) {
  if (v > 0) return 1;
  if (v < 0) return -1;
  return 0;
}

// Apply side trim
int applyTrim(int pwm, int trim) {
  if (pwm <= 0) return 0;
  int v = (int)(((long)pwm * trim) / 100);
  return constrain(v, PWM_MIN, 255);
}

//  Channel control ( Speed: -255 to +255 )
void channelA(int speed) {
  if (INVERT_A) speed = -speed;
  int pwm = applyTrim(abs(speed), TRIM_A);

  if (pwm == 0) {
    digitalWrite(PIN_IN1, LOW);
    digitalWrite(PIN_IN2, LOW);
  } else if (speed > 0) {
    digitalWrite(PIN_IN1, HIGH);
    digitalWrite(PIN_IN2, LOW);
  } else {
    digitalWrite(PIN_IN1, LOW);
    digitalWrite(PIN_IN2, HIGH);
  }
  PWM_WRITE(PIN_ENA, PWM_CH_A, pwm);
}

void channelB(int speed) {
  if (INVERT_B) speed = -speed;
  int pwm = applyTrim(abs(speed), TRIM_B);

  if (pwm == 0) {
    digitalWrite(PIN_IN3, LOW);
    digitalWrite(PIN_IN4, LOW);
  } else if (speed > 0) {
    digitalWrite(PIN_IN3, HIGH);
    digitalWrite(PIN_IN4, LOW);
  } else {
    digitalWrite(PIN_IN3, LOW);
    digitalWrite(PIN_IN4, HIGH);
  }
  PWM_WRITE(PIN_ENB, PWM_CH_B, pwm);
}

// Current speed per side
int curSpeedA = 0;
int curSpeedB = 0;

void drive(int spdA, int spdB) {
  bool flipA = (curSpeedA > 0 && spdA < 0) || (curSpeedA < 0 && spdA > 0);
  bool flipB = (curSpeedB > 0 && spdB < 0) || (curSpeedB < 0 && spdB > 0);

  // Deadtime on direction change
  if (flipA || flipB) {
    channelA(0);
    channelB(0);
    delay(T_DEADTIME);
    curSpeedA = 0;
    curSpeedB = 0;
  }

  // Kick only the sides starting from stop
  bool startA = (curSpeedA == 0 && spdA != 0);
  bool startB = (curSpeedB == 0 && spdB != 0);
  if (startA || startB) {
    channelA(startA ? sign(spdA) * PWM_KICK : spdA);
    channelB(startB ? sign(spdB) * PWM_KICK : spdB);
    delay(T_KICK);
  }

  channelA(spdA);
  channelB(spdB);
  curSpeedA = spdA;
  curSpeedB = spdB;
}

// Active brake
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

// All use SPEED constant
void stopMotors() {
  drive(0, 0);
}

void forward() {
  drive(SPEED, SPEED);
}

void backward() {
  drive(-SPEED, -SPEED);
}

// Turn right (Left forward, right back)
void turnRight() {
  drive(SPEED, -SPEED);
}

// Turn left (Right forward, left back)
void turnLeft() {
  drive(-SPEED, SPEED);
}


//  HC-SR04 (AUTO only)

int distanceCm = 999;  // 999 = nothing in range
bool obstacle = false;
int nearReadings = 0;
int farReadings = 0;
int clearReadings = 0;
unsigned long tLastMeasure = 0;

int measureDistanceCm() {
  digitalWrite(PIN_TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(PIN_TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(PIN_TRIG, LOW);

  unsigned long us = pulseIn(PIN_ECHO, HIGH, ECHO_TIMEOUT);
  if (us == 0) return 999;
  return us / 58;
}

void resetSensor() {
  distanceCm = 999;
  obstacle = false;
  nearReadings = 0;
  farReadings = 0;
  clearReadings = 0;
}

// State changes need 2 readings in a row
void updateSensor() {
  if (!USE_SENSOR) return;
  if (millis() - tLastMeasure < T_MEASURE) return;
  tLastMeasure = millis();

  distanceCm = measureDistanceCm();

  if (distanceCm < DIST_STOP) {
    if (nearReadings < 2) nearReadings++;
    farReadings = 0;
  } else if (distanceCm > DIST_FREE) {
    if (farReadings < 2) farReadings++;
    nearReadings = 0;
  }

  if (distanceCm > DIST_CLEAR) {
    if (clearReadings < 2) clearReadings++;
  } else {
    clearReadings = 0;
  }

  if (!obstacle && nearReadings >= 2) {
    obstacle = true;
    Serial.printf("[SENSOR] Obstacle at %d cm\n", distanceCm);
  } else if (obstacle && farReadings >= 2) {
    obstacle = false;
    Serial.printf("[SENSOR] Path clear (%d cm)\n", distanceCm);
  }
}


//  AUTO sequence

struct Step {
  void (*action)();
  unsigned long duration;
  const char *name;
};

const Step SEQUENCE[] = {
  { stopMotors, T_START_DELAY, "Initial wait" },
  { forward,    T_FORWARD,     "Forward" },
  { stopMotors, T_PAUSE,       "Pause" },
  { turnRight,  T_TURN,        "Turn right" },
  { stopMotors, T_PAUSE,       "Pause" },
  { forward,    T_FORWARD,     "Forward" },
  { stopMotors, T_PAUSE,       "Pause" },
  { turnLeft,   T_TURN,        "Turn left" },
  { stopMotors, T_PAUSE,       "Pause" },
  { backward,   T_REVERSE,     "Reverse" },
};
const int NUM_STEPS = sizeof(SEQUENCE) / sizeof(SEQUENCE[0]);

int curStep = 0;
unsigned long tStepStart = 0;
unsigned long tElapsed = 0;  // Step time done before avoiding
bool seqDone = true;

// Avoidance states
#define AV_NONE   0
#define AV_BRAKE  1
#define AV_BACKUP 2
#define AV_SPIN   3
#define AV_EXTRA  4

int avoidState = AV_NONE;
unsigned long tAvoid = 0;
int spinDir = 1;  // 1 right, -1 left (alternates)

// Sensor faces forward
bool stepGoesForward(int i) {
  return SEQUENCE[i].action == forward;
}

void setAvoid(int state) {
  avoidState = state;
  tAvoid = millis();
}

void startAvoid() {
  Serial.printf("[AUTO] Obstacle at %d cm -> avoiding\n", distanceCm);
  brake();
  setAvoid(AV_BRAKE);
}

void backUp() {
  drive(-AVOID_SPEED, -AVOID_SPEED);
  setAvoid(AV_BACKUP);
}

void spin() {
  Serial.println(spinDir > 0 ? "[AUTO] Searching right" : "[AUTO] Searching left");
  drive(spinDir * AVOID_SPEED, -spinDir * AVOID_SPEED);
  clearReadings = 0;
  setAvoid(AV_SPIN);
}

void updateAvoid() {
  unsigned long t = millis() - tAvoid;

  switch (avoidState) {
    case AV_BRAKE:
      if (t >= T_BRAKE) backUp();
      break;

    case AV_BACKUP:
      if (t >= T_BACKUP) spin();
      break;

    case AV_SPIN:
      if (clearReadings >= 2) {
        setAvoid(AV_EXTRA);
      } else if (t >= T_SPIN_MAX) {
        Serial.println("[AUTO] No way out -> other side");
        spinDir = -spinDir;
        backUp();
      }
      break;

    case AV_EXTRA:
      if (t >= T_SPIN_EXTRA) {
        Serial.println("[AUTO] Path clear -> resuming");
        avoidState = AV_NONE;
        spinDir = -spinDir;
        SEQUENCE[curStep].action();
        tStepStart = millis() - tElapsed;  // Resume remaining time
      }
      break;
  }
}

void startStep(int i) {
  curStep = i;
  Serial.printf("[AUTO] Step %d/%d: %s\n", i + 1, NUM_STEPS, SEQUENCE[i].name);

  if (obstacle && stepGoesForward(i)) {
    tElapsed = 0;
    startAvoid();
    return;
  }
  SEQUENCE[i].action();
  tStepStart = millis();
}

void startAuto() {
  resetSensor();
  avoidState = AV_NONE;
  seqDone = false;
  startStep(0);
}

void updateAuto() {
  if (seqDone) return;

  if (avoidState != AV_NONE) {
    updateAvoid();
    return;
  }

  if (obstacle && stepGoesForward(curStep)) {
    tElapsed = millis() - tStepStart;
    startAvoid();
    return;
  }

  if (millis() - tStepStart < SEQUENCE[curStep].duration) return;

  if (curStep + 1 < NUM_STEPS) {
    startStep(curStep + 1);
  } else {
    stopMotors();
    seqDone = true;
    Serial.println("[AUTO] Done. Press twice to repeat.");
  }
}


//  RC drive (sensor off)

void driveRC(int16_t x, int16_t y) {
  int throttle = -x;  // X- forward
  int turn     = -y;  // Y- right

  // Tank mixing
  int motorA = constrain(throttle + turn, -1000, 1000);  // Left
  int motorB = constrain(throttle - turn, -1000, 1000);  // Right

  motorA = map(motorA, -1000, 1000, -SPEED_RC, SPEED_RC);
  motorB = map(motorB, -1000, 1000, -SPEED_RC, SPEED_RC);

  Serial.printf("[RC] X: %d Y: %d -> A: %d B: %d\n", x, y, motorA, motorB);
  drive(motorA, motorB);
}


//  ESP-NOW

portMUX_TYPE rxMux = portMUX_INITIALIZER_UNLOCKED;
volatile int16_t rxX = 0;
volatile int16_t rxY = 0;
volatile uint8_t rxMode = MODE_NO_LINK;
volatile unsigned long tLastPacket = 0;
volatile bool newData = false;

// Only stores data; motors run in loop()
void onReceive(const esp_now_recv_info_t *info, const uint8_t *data, int len) {
  if (len != sizeof(struct_message)) return;

  struct_message msg;
  memcpy(&msg, data, sizeof(msg));
  if (msg.mode != MODE_AUTO && msg.mode != MODE_RC) return;

  unsigned long now = millis();
  portENTER_CRITICAL(&rxMux);
  rxX = msg.x;
  rxY = msg.y;
  rxMode = msg.mode;
  tLastPacket = now;
  newData = true;
  portEXIT_CRITICAL(&rxMux);
}

uint8_t mode = MODE_NO_LINK;
bool failsafeOn = false;

void setup() {
  Serial.begin(115200);

  pinMode(PIN_IN1, OUTPUT);
  pinMode(PIN_IN2, OUTPUT);
  pinMode(PIN_IN3, OUTPUT);
  pinMode(PIN_IN4, OUTPUT);

  pinMode(PIN_TRIG, OUTPUT);
  pinMode(PIN_ECHO, INPUT);
  digitalWrite(PIN_TRIG, LOW);

  PWM_INIT(PIN_ENA, PWM_CH_A);
  PWM_INIT(PIN_ENB, PWM_CH_B);

  channelA(0);
  channelB(0);

  WiFi.mode(WIFI_STA);
  WiFi.disconnect();

  // Same channel as controller
  esp_wifi_set_promiscuous(true);
  esp_wifi_set_channel(1, WIFI_SECOND_CHAN_NONE);
  esp_wifi_set_promiscuous(false);

  if (esp_now_init() != ESP_OK) {
    Serial.println("ESP-NOW init failed");
    while (1);
  }

  esp_now_register_recv_cb(onReceive);

  Serial.print("Car MAC: ");
  Serial.println(WiFi.macAddress());
  Serial.println("Waiting for controller...");
}

void loop() {
  portENTER_CRITICAL(&rxMux);
  int16_t x = rxX;
  int16_t y = rxY;
  uint8_t newMode = rxMode;
  unsigned long tPacket = tLastPacket;
  bool isNew = newData;
  newData = false;
  portEXIT_CRITICAL(&rxMux);

  bool linked = (millis() - tPacket) < T_FAILSAFE;

  // Mode change: stop first
  if (newMode != mode) {
    stopMotors();
    mode = newMode;
    failsafeOn = false;

    if (mode == MODE_RC) {
      Serial.println(">> RC mode (sensor off)");
    } else if (mode == MODE_AUTO) {
      Serial.println(">> AUTO mode (sensor on)");
      startAuto();
    }
  }

  switch (mode) {
    case MODE_RC:
      if (!linked) {
        if (!failsafeOn) {
          stopMotors();
          failsafeOn = true;
          Serial.println("[RC] Signal lost -> stop");
        }
      } else if (isNew) {
        failsafeOn = false;
        driveRC(x, y);
      }
      break;

    case MODE_AUTO:
      updateSensor();
      updateAuto();
      break;

    default:
      break;  // No controller yet
  }
}