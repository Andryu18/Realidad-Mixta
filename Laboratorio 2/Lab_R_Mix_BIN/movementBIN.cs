using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO.Ports;

public class ArduinoController3 : MonoBehaviour
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
    private bool connected = false;

    private int b7 = 0;
    private int b6 = 0;
    private int b5 = 0;
    private int b4 = 0;

    private int potValue = 0;

    private string lastState = "";

    private bool isGrounded = false;
    private bool canJump = false;
    private bool previousB4 = false;
    private bool previousB5 = true;
    private bool restartRequested = false;

    // Binary protocol
    private const byte HEADER_1 = 0xAA;
    private const byte HEADER_2 = 0x55;
    private const byte DATA_LENGTH = 5;

    // Packet parser
    private int packetState = 0;
    private readonly byte[] data = new byte[DATA_LENGTH];
    private int dataIndex = 0;

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
            serial.DtrEnable = true;
            serial.RtsEnable = true;

            serial.Open();
            serial.DiscardInBuffer();

            ResetPacket();
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
        // Receive binary data
        if (connected && serial != null && serial.IsOpen)
        {
            try
            {
                while (serial.BytesToRead > 0)
                {
                    int value = serial.ReadByte();

                    if (value >= 0)
                        ProcessByte((byte)value);
                }
            }
            catch (TimeoutException)
            {
            }
            catch (Exception e)
            {
                if (showDebug)
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

    // Parse binary protocol
    void ProcessByte(byte value)
    {
        switch (packetState)
        {
            case 0:
                if (value == HEADER_1)
                    packetState = 1;
                break;

            case 1:
                if (value == HEADER_2)
                {
                    packetState = 2;
                }
                else if (value == HEADER_1)
                {
                    packetState = 1;
                }
                else
                {
                    packetState = 0;
                }
                break;

            case 2:
                if (value == DATA_LENGTH)
                {
                    dataIndex = 0;
                    packetState = 3;
                }
                else if (value == HEADER_1)
                {
                    packetState = 1;
                }
                else
                {
                    ResetPacket();
                }
                break;

            case 3:
                data[dataIndex] = value;
                dataIndex++;

                if (dataIndex >= DATA_LENGTH)
                    packetState = 4;
                break;

            case 4:
                byte calculatedChecksum = CalculateChecksum(data);

                if (value == calculatedChecksum)
                {
                    UpdateControls();
                }
                else if (showDebug)
                {
                    Debug.LogWarning(
                        "Invalid checksum. Packet discarded."
                    );
                }

                ResetPacket();
                break;
        }
    }

    // Calculate checksum
    byte CalculateChecksum(byte[] values)
    {
        int sum = 0;

        for (int i = 0; i < DATA_LENGTH; i++)
        {
            sum += values[i];
        }

        return (byte)(sum & 0xFF);
    }

    // Update controls after packet validation
    void UpdateControls()
    {
        for (int i = 0; i < 4; i++)
        {
            if (data[i] > 1)
            {
                if (showDebug)
                    Debug.LogWarning(
                        "Invalid button state. Packet discarded."
                    );
                return;
            }
        }

        if (data[4] > 100)
        {
            if (showDebug)
                Debug.LogWarning(
                    "Invalid potentiometer value."
                );
            return;
        }

        b7 = data[0];
        b6 = data[1];
        b5 = data[2];
        b4 = data[3];
        potValue = data[4];

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

    // Reset packet parser
    void ResetPacket()
    {
        packetState = 0;
        dataIndex = 0;
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

