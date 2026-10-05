const int BUTTON_7 = 7;

const int BUTTON_6 = 6;

const int BUTTON_5 = 5;

const int BUTTON_4 = 4;

const int POT_PIN = A0;

// binary protocol

const byte HEADER_1 = 0xAA;

const byte HEADER_2 = 0x55;

const byte DATA_LENGTH = 5;

// Data buffer

byte data[DATA_LENGTH];

// Function to calculate the checksum

byte calculateChecksum(const byte* data, byte length)

{

  byte checksum = 0;

  for (byte i = 0; i < length; i++)

  {

    checksum += data[i];

  }

  return checksum;

}

void setup()

{

  pinMode(BUTTON_7, INPUT_PULLUP);

  pinMode(BUTTON_6, INPUT_PULLUP);

  pinMode(BUTTON_5, INPUT_PULLUP);

  pinMode(BUTTON_4, INPUT_PULLUP);

  Serial.begin(9600);

}

void loop()

{

  // Button reading

  data[0] = !digitalRead(BUTTON_7);

  data[1] = !digitalRead(BUTTON_6);

  data[2] = !digitalRead(BUTTON_5);

  data[3] = !digitalRead(BUTTON_4);

  // Potentiometer reading and mapping

  int potMap = map(analogRead(POT_PIN), 0, 1023, 0, 100);

  data[4] = (byte)potMap;

  byte checksum = calculateChecksum(data, DATA_LENGTH);

  Serial.write(HEADER_1);

  Serial.write(HEADER_2);

  Serial.write(DATA_LENGTH);

  Serial.write(data, DATA_LENGTH);

  Serial.write(checksum);

  delay(100);

}
