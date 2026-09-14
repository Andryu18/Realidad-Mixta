//Arduino 1


// C++ code
//
void setup()
{
  Serial.begin(9600);
}

void loop()
{
  if(Serial.available()>0){
  	String result = Serial.readStringUntil('\n');
    Serial.println(result);
  }
}

//Arduino 2

// C++ code
//
void setup()
{
  pinMode(LED_BUILTIN, OUTPUT);
  Serial.begin(9600);
}

void loop()
{
  Serial.println("A");
  delay(500);
}

