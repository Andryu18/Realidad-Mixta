const int BUTTON_7 = 7;

const int BUTTON_6 = 6;

const int BUTTON_5 = 5;

const int BUTTON_4 = 4;

const int POT_PIN = A0;

int potMap = 0;

void setup() {

  pinMode(BUTTON_7, INPUT_PULLUP);

  pinMode(BUTTON_6, INPUT_PULLUP);

  pinMode(BUTTON_5, INPUT_PULLUP);

  pinMode(BUTTON_4, INPUT_PULLUP);

  Serial.begin(9600);

}

void loop() {

  // Potentiometer mapping (0-1023 to 0-100)

  potMap = map(analogRead(POT_PIN), 0, 1023, 0, 100);

  // Send data in JSON format

  Serial.print("{");

  Serial.print("\"button_7\":");

  Serial.print(!digitalRead(BUTTON_7));

  Serial.print(",");

  Serial.print("\"button_6\":");

  Serial.print(!digitalRead(BUTTON_6));

  Serial.print(",");

  Serial.print("\"button_5\":");

  Serial.print(!digitalRead(BUTTON_5));

  Serial.print(",");

  Serial.print("\"button_4\":");

  Serial.print(!digitalRead(BUTTON_4));

  Serial.print(",");

  Serial.print("\"potentiometer\":");

  Serial.print(potMap);

  Serial.println("}");

  delay(100);

}

