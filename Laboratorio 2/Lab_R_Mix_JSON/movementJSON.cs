
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO.Ports;
using System.Text;

public class ArduinoController2 : MonoBehaviour
{
    [Header("Arduino Connection")]
    [SerializeField] private string comPort = "COM5";
    [SerializeField] private int baudRate = 9600;

    [Header("Movement")]
    [SerializeField] private float speed = 5f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private Transform groundPoint;
    [SerializeField] private float groundRadius = 0.1f;

    [Header("Potentiometer")]
    [SerializeField] private bool usePotentiometer = false;
    [SerializeField] private float minimumSpeed = 2f;
    [SerializeField] private float maximumSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    private SerialPort serial;
    private Rigidbody2D rb;
    private readonly StringBuilder buffer = new StringBuilder();

    private int b7 = 0;
    private int b6 = 0;
    private int b5 = 0;
    private int b4 = 0;

    private int potValue = 0;

    private string lastState = "";
    private bool connected = false;

    private bool isGrounded = false;
    private bool canJump = false;
    private bool previousB4 = false;
    private bool previousB5 = true;
    private bool restartRequested = false;

    // JSON data structure
    [Serializable]
    private class ArduinoData
    {
        public int button_7;
        public int button_6;
        public int button_5;
        public int button_4;
        public int potentiometer;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        if (rb == null)
        {
            Debug.LogError(
                "ArduinoController: Rigidbody2D is missing from the character."
            );
            enabled = false;
            return;
        }

        if (groundPoint == null)
        {
            Debug.LogError(
                "ArduinoController: Assign the Ground Point in the Inspector."
            );
            enabled = false;
            return;
        }

        ConnectArduino();
    }

    void ConnectArduino()
    {
        try
        {
            serial = new SerialPort(
                comPort,
                baudRate,
                Parity.None,
                8,
                StopBits.One
            );

            serial.Handshake = Handshake.None;
            serial.ReadTimeout = 50;
            serial.WriteTimeout = 50;
            serial.NewLine = "\n";
            serial.DtrEnable = true;
            serial.RtsEnable = true;
            serial.Encoding = Encoding.ASCII;

            serial.Open();
            serial.DiscardInBuffer();

            connected = true;

            Debug.Log(
                "Arduino connected successfully on " + comPort
            );
        }
        catch (Exception e)
        {
            connected = false;
            Debug.LogError("Arduino connection error: " + e.Message);
            ClosePort();
        }
    }

    void Update()
    {
        // Read Arduino data
        if (connected && serial != null && serial.IsOpen)
        {
            try
            {
                string receivedData = serial.ReadExisting();

                if (!string.IsNullOrEmpty(receivedData))
                {
                    buffer.Append(receivedData);

                    while (true)
                    {
                        string content = buffer.ToString();
                        int index = content.IndexOf('\n');

                        if (index < 0)
                            break;

                        string line = content
                            .Substring(0, index)
                            .Trim();

                        buffer.Remove(0, index + 1);

                        if (!string.IsNullOrEmpty(line))
                            ProcessData(line);
                    }

                    if (buffer.Length > 1024)
                    {
                        buffer.Clear();

                        if (showDebug)
                            Debug.LogWarning(
                                "Serial buffer reset."
                            );
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "Serial read error: " + e.Message
                );
            }
        }

        if (restartRequested)
        {
            RestartGame();
            return;
        }

        isGrounded = Physics2D.OverlapCircle(
            groundPoint.position,
            groundRadius,
            groundLayers
        );

        if (isGrounded && !canJump)
        {
            canJump = true;

            if (showDebug)
                Debug.Log("Grounded: jump enabled");
        }
    }

    void ProcessData(string data)
    {
        ArduinoData arduinoData;

        try
        {
            // Parse JSON
            arduinoData = JsonUtility.FromJson<ArduinoData>(data);
        }
        catch (Exception e)
        {
            if (showDebug)
                Debug.LogWarning(
                    "JSON parsing error: " + e.Message +
                    " | Data: " + data
                );
            return;
        }

        if (arduinoData == null)
        {
            if (showDebug)
                Debug.LogWarning("Empty or invalid JSON: " + data);
            return;
        }

        if ((arduinoData.button_7 != 0 && arduinoData.button_7 != 1) ||
            (arduinoData.button_6 != 0 && arduinoData.button_6 != 1) ||
            (arduinoData.button_5 != 0 && arduinoData.button_5 != 1) ||
            (arduinoData.button_4 != 0 && arduinoData.button_4 != 1))
        {
            if (showDebug)
                Debug.LogWarning(
                    "Invalid button state: " + data
                );
            return;
        }

        b7 = arduinoData.button_7;
        b6 = arduinoData.button_6;
        b5 = arduinoData.button_5;
        b4 = arduinoData.button_4;
        potValue = Mathf.Clamp(arduinoData.potentiometer, 0, 100);

        bool b5Pressed = b5 == 1;

        if (b5Pressed && !previousB5)
            restartRequested = true;

        previousB5 = b5Pressed;

        string currentState = $"{b7},{b6},{b5},{b4}";

        if (showDebug && currentState != lastState)
        {
            Debug.Log(
                $"Buttons -> B7:{b7} B6:{b6} " +
                $"B5:{b5} B4:{b4} | Pot:{potValue}"
            );

            lastState = currentState;
        }
    }

    void FixedUpdate()
    {
        if (rb == null)
            return;

        float moveX = 0f;

        if (b6 == 1)
            moveX += 1f;

        if (b7 == 1)
            moveX -= 1f;

        float currentSpeed = speed;

        if (usePotentiometer)
        {
            currentSpeed = Mathf.Lerp(
                minimumSpeed,
                maximumSpeed,
                potValue / 100f
            );
        }

        rb.linearVelocity = new Vector2(
            moveX * currentSpeed,
            rb.linearVelocity.y
        );

        bool b4Pressed = b4 == 1;
        bool newPress = b4Pressed && !previousB4;

        if (newPress && CanJump())
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                jumpForce
            );

            canJump = false;

            if (showDebug)
                Debug.Log("JUMP EXECUTED");
        }

        previousB4 = b4Pressed;

        if (showDebug && moveX != 0)
        {
            Debug.Log(
                $"VELOCITY: {rb.linearVelocity} | " +
                $"GROUNDED: {isGrounded} | " +
                $"CAN JUMP: {canJump}"
            );
        }
    }

    bool CanJump()
    {
        return isGrounded && canJump;
    }

    void RestartGame()
    {
        restartRequested = false;

        int sceneIndex = SceneManager.GetActiveScene().buildIndex;

        if (sceneIndex < 0)
        {
            Debug.LogError(
                "ArduinoController: Add the current scene to the Build Settings to restart the game."
            );
            return;
        }

        if (showDebug)
            Debug.Log("RESTARTING GAME");

        Time.timeScale = 1f;
        ClosePort();
        SceneManager.LoadScene(sceneIndex);
    }

    void OnDrawGizmosSelected()
    {
        if (groundPoint == null)
            return;

        Gizmos.color = isGrounded ? Color.green : Color.red;

        Gizmos.DrawWireSphere(
            groundPoint.position,
            groundRadius
        );
    }

    void OnDestroy()
    {
        ClosePort();
    }

    void OnApplicationQuit()
    {
        ClosePort();
    }

    void ClosePort()
    {
        connected = false;

        if (serial == null)
            return;

        try
        {
            if (serial.IsOpen)
                serial.Close();

            serial.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "Error closing serial port: " + e.Message
            );
        }

        serial = null;
    }
}
