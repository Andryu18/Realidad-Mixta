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
 */

//  Pins 
const int PIN_ENA = 14;   // PWM channel A
const int PIN_IN1 = 27;
const int PIN_IN2 = 26;

const int PIN_ENB = 32;   // PWM channel B
const int PIN_IN3 = 25;
const int PIN_IN4 = 33;

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
const int SPEED = 200;       // 0 to 255

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
  }

  // Both sides kick together
  if (spdA != 0 || spdB != 0) {
    channelA(sign(spdA) * PWM_KICK);
    channelB(sign(spdB) * PWM_KICK);
    delay(T_KICK);
  }

  channelA(spdA);
  channelB(spdB);
  curSpeedA = spdA;
  curSpeedB = spdB;
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

// Stop and wait
void pauseStep() {
  stopMotors();
  delay(T_PAUSE);
}

//  setup 

void setup() {
  pinMode(PIN_IN1, OUTPUT);
  pinMode(PIN_IN2, OUTPUT);
  pinMode(PIN_IN3, OUTPUT);
  pinMode(PIN_IN4, OUTPUT);

  PWM_INIT(PIN_ENA, PWM_CH_A);
  PWM_INIT(PIN_ENB, PWM_CH_B);

  channelA(0);
  channelB(0);

  delay(T_START_DELAY);

  forward();
  delay(T_FORWARD);

  pauseStep();

  turnRight();
  delay(T_TURN);

  pauseStep();

  forward();
  delay(T_FORWARD);

  pauseStep();

  turnLeft();
  delay(T_TURN);

  pauseStep();

  backward();
  delay(T_REVERSE);

  stopMotors();
}

void loop() {
  
}