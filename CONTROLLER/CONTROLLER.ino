/*
   Wiring:

     Joystick       ESP32
     VRx  ........  D35
     VRy  ........  D34
     SW   ........  D18
     VCC  ........  3V3
     GND  ........  GND

     Red LED: D19 -> 220-330 ohm -> LED -> GND

   Joystick button toggles mode:
     LED on  -> RC
     LED off -> AUTO
 */

#include <esp_now.h>
#include <WiFi.h>
#include <esp_wifi.h>

#define JOY_X_PIN  35  
#define JOY_Y_PIN  34 
#define JOY_SW_PIN 18  
#define LED_PIN    19  

//  Modes (must match car)
#define MODE_AUTO 0
#define MODE_RC   1

const uint8_t START_MODE = MODE_AUTO;

uint8_t receiverAddress[] = { 0xec, 0xc9, 0xff, 0xfe, 0x85, 0xc8 };  // Receiver MAC

const int DEAD_ZONE = 150;

const unsigned long T_SEND     = 50;  // send every 50 ms (~20 Hz)
const unsigned long T_DEBOUNCE = 40;  // ms

typedef struct __attribute__((packed)) {
  int16_t x;  // -1000 to 1000 range
  int16_t y;
  uint8_t mode;
} struct_message;

struct_message myData;
uint8_t mode = START_MODE;

bool btnState = HIGH;
bool lastReading = HIGH;
unsigned long tBtnChange = 0;
unsigned long tLastSend = 0;

void onSent(const wifi_tx_info_t *tx_info, esp_now_send_status_t status) {
  Serial.print("\r\nLast Packet Send Status:\t");
  Serial.println(status == ESP_NOW_SEND_SUCCESS ? "Delivery Success" : "Delivery Fail");
}

// Dead zone, then rescale the rest to full range
int16_t applyDeadZone(int v) {
  if (abs(v) <= DEAD_ZONE) return 0;
  if (v > 0) return map(v, DEAD_ZONE, 1000, 0, 1000);
  return map(v, -1000, -DEAD_ZONE, -1000, 0);
}

void updateLed() {
  digitalWrite(LED_PIN, mode == MODE_RC ? HIGH : LOW);
}

// Toggle mode on each press (debounced)
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
      updateLed();
      Serial.println(mode == MODE_RC ? ">> RC mode (LED on)" : ">> AUTO mode (LED off)");
      tLastSend = millis() - T_SEND;  // Send change now
    }
  }
}

void setup() {
  Serial.begin(115200);

  pinMode(JOY_SW_PIN, INPUT_PULLUP);
  pinMode(LED_PIN, OUTPUT);
  btnState = lastReading = digitalRead(JOY_SW_PIN);
  updateLed();

  WiFi.mode(WIFI_STA);
  WiFi.disconnect();

  esp_wifi_set_promiscuous(true);
  esp_wifi_set_channel(1, WIFI_SECOND_CHAN_NONE);
  esp_wifi_set_promiscuous(false);

  if (esp_now_init() != ESP_OK) {
    Serial.println("ESP-NOW init failed");
    while (1);
  }

  esp_now_register_send_cb(onSent);

  esp_now_peer_info_t peerInfo = {};
  memcpy(peerInfo.peer_addr, receiverAddress, 6);
  peerInfo.channel = 1;
  peerInfo.encrypt = false;

  if (esp_now_add_peer(&peerInfo) != ESP_OK) {
    Serial.println("Failed to add peer");
    while (1);
  }
}

void loop() {
  readButton();

  if (millis() - tLastSend < T_SEND) return;
  tLastSend = millis();

  if (mode == MODE_RC) {
    // 1. Initial mapping (-1000 to 1000)
    int mappedX = map(analogRead(JOY_X_PIN), 0, 4095, 1000, -1000);
    int mappedY = map(analogRead(JOY_Y_PIN), 0, 4095, 1000, -1000);

    // 2. Dead zone
    myData.x = applyDeadZone(mappedX);
    myData.y = applyDeadZone(mappedY);
  } else {
    // Joystick ignored in AUTO
    myData.x = 0;
    myData.y = 0;
  }
  myData.mode = mode;

  Serial.printf("Mode: %s \t X: %d \t Y: %d\n", mode == MODE_RC ? "RC" : "AUTO", myData.x, myData.y);

  // Send via ESP-NOW
  esp_err_t result = esp_now_send(receiverAddress, (uint8_t *)&myData, sizeof(myData));

  if (result != ESP_OK) {
    Serial.println("Send error");
  }
}