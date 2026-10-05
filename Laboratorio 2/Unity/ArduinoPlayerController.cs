using System;
using System.IO.Ports;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ArduinoPlayerController : MonoBehaviour
{
    public enum SerialProtocol
    {
        Csv,
        Json,
        Binary
    }

    private const int ReadBufferSize = 256;
    private const int MaxPotentiometer = 100;

    [Header("Arduino Connection")]
    [SerializeField] private SerialProtocol protocol = SerialProtocol.Csv;
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

    private readonly ArduinoSerialConnection connection = new ArduinoSerialConnection();
    private readonly byte[] readBuffer = new byte[ReadBufferSize];

    private IInputDecoder decoder;
    private Rigidbody2D rb;
    private ControllerInput currentInput;

    private bool isGrounded = false;
    private bool canJump = false;
    private bool previousJumpPressed = false;
    // Starts pressed so a button held during the scene reload does not restart again
    private bool previousRestartPressed = true;
    private bool restartRequested = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        if (rb == null)
        {
            Debug.LogError("ArduinoPlayerController: Rigidbody2D is missing from the character.");
            enabled = false;
            return;
        }

        if (groundPoint == null)
        {
            Debug.LogError("ArduinoPlayerController: Assign the Ground Point in the Inspector.");
            enabled = false;
            return;
        }

        decoder = CreateDecoder(protocol);
        ConnectArduino();
    }

    void Update()
    {
        ReadSerialInput();

        if (restartRequested)
        {
            RestartGame();
            return;
        }

        UpdateGroundState();
    }

    void FixedUpdate()
    {
        if (rb == null)
            return;

        ApplyHorizontalMovement();
        TryJump();
    }

    void OnDrawGizmosSelected()
    {
        if (groundPoint == null)
            return;

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundPoint.position, groundRadius);
    }

    void OnDestroy()
    {
        connection.Close();
    }

    void OnApplicationQuit()
    {
        connection.Close();
    }

    private IInputDecoder CreateDecoder(SerialProtocol selectedProtocol)
    {
        switch (selectedProtocol)
        {
            case SerialProtocol.Json:
                return new JsonInputDecoder(ReportInvalidPacket);
            case SerialProtocol.Binary:
                return new BinaryInputDecoder(ReportInvalidPacket);
            default:
                return new CsvInputDecoder(ReportInvalidPacket);
        }
    }

    private void ConnectArduino()
    {
        string errorMessage;

        if (connection.TryOpen(comPort, baudRate, out errorMessage))
            Debug.Log($"Arduino connected successfully on {comPort} ({protocol})");
        else
            Debug.LogError("Arduino connection error: " + errorMessage);
    }

    private void ReadSerialInput()
    {
        if (!connection.IsOpen)
            return;

        try
        {
            int bytesRead;

            while ((bytesRead = connection.ReadAvailableBytes(readBuffer)) > 0)
            {
                DecodeBytes(bytesRead);
            }
        }
        catch (TimeoutException)
        {
        }
        catch (Exception e)
        {
            Debug.LogWarning("Serial read error: " + e.Message);
        }
    }

    private void DecodeBytes(int count)
    {
        for (int i = 0; i < count; i++)
        {
            ControllerInput input;

            if (decoder.TryDecode(readBuffer[i], out input))
                ApplyInput(input);
        }
    }

    private void ApplyInput(ControllerInput input)
    {
        if (showDebug && !input.HasSameButtons(currentInput))
            Debug.Log(input.ToString());

        currentInput = input;

        if (input.RestartPressed && !previousRestartPressed)
            restartRequested = true;

        previousRestartPressed = input.RestartPressed;
    }

    private void UpdateGroundState()
    {
        isGrounded = Physics2D.OverlapCircle(groundPoint.position, groundRadius, groundLayers);

        if (isGrounded && !canJump)
        {
            canJump = true;

            if (showDebug)
                Debug.Log("Grounded: jump enabled");
        }
    }

    private void ApplyHorizontalMovement()
    {
        float direction = 0f;

        if (currentInput.RightPressed)
            direction += 1f;

        if (currentInput.LeftPressed)
            direction -= 1f;

        rb.linearVelocity = new Vector2(direction * GetCurrentSpeed(), rb.linearVelocity.y);
    }

    private float GetCurrentSpeed()
    {
        if (!usePotentiometer)
            return speed;

        float normalizedPotentiometer = currentInput.Potentiometer / (float)MaxPotentiometer;
        return Mathf.Lerp(minimumSpeed, maximumSpeed, normalizedPotentiometer);
    }

    private void TryJump()
    {
        bool jumpPressed = currentInput.JumpPressed;
        bool newPress = jumpPressed && !previousJumpPressed;

        if (newPress && isGrounded && canJump)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            canJump = false;

            if (showDebug)
                Debug.Log("JUMP EXECUTED");
        }

        previousJumpPressed = jumpPressed;
    }

    private void ReportInvalidPacket(string message)
    {
        if (showDebug)
            Debug.LogWarning(message);
    }

    private void RestartGame()
    {
        restartRequested = false;
        int sceneIndex = SceneManager.GetActiveScene().buildIndex;

        if (sceneIndex < 0)
        {
            Debug.LogError("ArduinoPlayerController: Add the current scene to the Build Settings to restart the game.");
            return;
        }

        if (showDebug)
            Debug.Log("RESTARTING GAME");

        Time.timeScale = 1f;
        connection.Close();
        SceneManager.LoadScene(sceneIndex);
    }

    private readonly struct ControllerInput
    {
        public bool LeftPressed { get; }
        public bool RightPressed { get; }
        public bool RestartPressed { get; }
        public bool JumpPressed { get; }
        public int Potentiometer { get; }

        public ControllerInput(bool leftPressed, bool rightPressed, bool restartPressed, bool jumpPressed, int potentiometer)
        {
            LeftPressed = leftPressed;
            RightPressed = rightPressed;
            RestartPressed = restartPressed;
            JumpPressed = jumpPressed;
            Potentiometer = potentiometer;
        }

        public bool HasSameButtons(ControllerInput other)
        {
            return LeftPressed == other.LeftPressed
                && RightPressed == other.RightPressed
                && RestartPressed == other.RestartPressed
                && JumpPressed == other.JumpPressed;
        }

        public override string ToString()
        {
            return $"Buttons -> B7:{ToBit(LeftPressed)} B6:{ToBit(RightPressed)} " +
                   $"B5:{ToBit(RestartPressed)} B4:{ToBit(JumpPressed)} | Pot:{Potentiometer}";
        }

        private static int ToBit(bool pressed)
        {
            return pressed ? 1 : 0;
        }
    }

    private interface IInputDecoder
    {
        bool TryDecode(byte value, out ControllerInput input);
    }

    private abstract class LineInputDecoder : IInputDecoder
    {
        private const int MaxLineLength = 1024;
        private const byte LineFeed = (byte)'\n';
        private const byte LastAsciiCode = 0x7F;

        private readonly StringBuilder lineBuffer = new StringBuilder();

        protected readonly Action<string> reportInvalidPacket;

        protected LineInputDecoder(Action<string> reportInvalidPacket)
        {
            this.reportInvalidPacket = reportInvalidPacket;
        }

        public bool TryDecode(byte value, out ControllerInput input)
        {
            input = default(ControllerInput);

            if (value != LineFeed)
            {
                AppendCharacter(value);
                return false;
            }

            string line = lineBuffer.ToString().Trim();
            lineBuffer.Clear();

            return line.Length > 0 && TryParseLine(line, out input);
        }

        protected abstract bool TryParseLine(string line, out ControllerInput input);

        protected bool TryCreateInput(int left, int right, int restart, int jump, int potentiometer, string line, out ControllerInput input)
        {
            input = default(ControllerInput);

            if (!IsButtonState(left) || !IsButtonState(right) || !IsButtonState(restart) || !IsButtonState(jump))
            {
                reportInvalidPacket("Invalid button state: " + line);
                return false;
            }

            int clampedPotentiometer = Mathf.Clamp(potentiometer, 0, MaxPotentiometer);
            input = new ControllerInput(left == 1, right == 1, restart == 1, jump == 1, clampedPotentiometer);
            return true;
        }

        private void AppendCharacter(byte value)
        {
            // Same substitution that Encoding.ASCII applies to non-ASCII bytes
            lineBuffer.Append(value <= LastAsciiCode ? (char)value : '?');

            if (lineBuffer.Length > MaxLineLength)
            {
                lineBuffer.Clear();
                reportInvalidPacket("Serial buffer reset.");
            }
        }

        private static bool IsButtonState(int value)
        {
            return value == 0 || value == 1;
        }
    }

    private sealed class CsvInputDecoder : LineInputDecoder
    {
        private const int FieldCount = 5;

        public CsvInputDecoder(Action<string> reportInvalidPacket) : base(reportInvalidPacket)
        {
        }

        protected override bool TryParseLine(string line, out ControllerInput input)
        {
            input = default(ControllerInput);
            string[] fields = line.Split(',');

            if (fields.Length != FieldCount)
            {
                reportInvalidPacket("Invalid serial format: " + line);
                return false;
            }

            int[] values = new int[FieldCount];

            for (int i = 0; i < FieldCount; i++)
            {
                if (!int.TryParse(fields[i].Trim(), out values[i]))
                {
                    reportInvalidPacket("Error parsing data: " + line);
                    return false;
                }
            }

            return TryCreateInput(values[0], values[1], values[2], values[3], values[4], line, out input);
        }
    }

    // Field names must match the keys sent by the Arduino sketch
    [Serializable]
    private class ArduinoMessage
    {
        public int button_7;
        public int button_6;
        public int button_5;
        public int button_4;
        public int potentiometer;
    }

    private sealed class JsonInputDecoder : LineInputDecoder
    {
        public JsonInputDecoder(Action<string> reportInvalidPacket) : base(reportInvalidPacket)
        {
        }

        protected override bool TryParseLine(string line, out ControllerInput input)
        {
            input = default(ControllerInput);
            ArduinoMessage message;

            try
            {
                message = JsonUtility.FromJson<ArduinoMessage>(line);
            }
            catch (Exception e)
            {
                reportInvalidPacket("JSON parsing error: " + e.Message + " | Data: " + line);
                return false;
            }

            if (message == null)
            {
                reportInvalidPacket("Empty or invalid JSON: " + line);
                return false;
            }

            return TryCreateInput(
                message.button_7,
                message.button_6,
                message.button_5,
                message.button_4,
                message.potentiometer,
                line,
                out input
            );
        }
    }

    private sealed class BinaryInputDecoder : IInputDecoder
    {
        private const byte Header1 = 0xAA;
        private const byte Header2 = 0x55;
        private const byte DataLength = 5;

        private enum PacketState
        {
            WaitHeader1,
            WaitHeader2,
            WaitLength,
            ReadData,
            WaitChecksum
        }

        private readonly byte[] data = new byte[DataLength];
        private readonly Action<string> reportInvalidPacket;

        private PacketState packetState = PacketState.WaitHeader1;
        private int dataIndex = 0;

        public BinaryInputDecoder(Action<string> reportInvalidPacket)
        {
            this.reportInvalidPacket = reportInvalidPacket;
        }

        public bool TryDecode(byte value, out ControllerInput input)
        {
            input = default(ControllerInput);

            switch (packetState)
            {
                case PacketState.WaitHeader1:
                    if (value == Header1)
                        packetState = PacketState.WaitHeader2;
                    return false;

                case PacketState.WaitHeader2:
                    if (value == Header2)
                        packetState = PacketState.WaitLength;
                    else if (value != Header1)
                        packetState = PacketState.WaitHeader1;
                    return false;

                case PacketState.WaitLength:
                    if (value == DataLength)
                    {
                        dataIndex = 0;
                        packetState = PacketState.ReadData;
                    }
                    else if (value == Header1)
                    {
                        packetState = PacketState.WaitHeader2;
                    }
                    else
                    {
                        packetState = PacketState.WaitHeader1;
                    }
                    return false;

                case PacketState.ReadData:
                    data[dataIndex] = value;
                    dataIndex++;
                    if (dataIndex >= DataLength)
                        packetState = PacketState.WaitChecksum;
                    return false;

                default:
                    packetState = PacketState.WaitHeader1;
                    return TryReadPacket(value, out input);
            }
        }

        private bool TryReadPacket(byte receivedChecksum, out ControllerInput input)
        {
            input = default(ControllerInput);

            if (receivedChecksum != CalculateChecksum())
            {
                reportInvalidPacket("Invalid checksum. Packet discarded.");
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (data[i] > 1)
                {
                    reportInvalidPacket("Invalid button state. Packet discarded.");
                    return false;
                }
            }

            if (data[4] > MaxPotentiometer)
            {
                reportInvalidPacket("Invalid potentiometer value.");
                return false;
            }

            input = new ControllerInput(data[0] == 1, data[1] == 1, data[2] == 1, data[3] == 1, data[4]);
            return true;
        }

        private byte CalculateChecksum()
        {
            int sum = 0;

            for (int i = 0; i < DataLength; i++)
            {
                sum += data[i];
            }

            return (byte)(sum & 0xFF);
        }
    }

    private sealed class ArduinoSerialConnection
    {
        private const int DataBits = 8;
        private const int TimeoutMilliseconds = 50;

        private SerialPort serialPort;

        public bool IsOpen => serialPort != null && serialPort.IsOpen;

        public bool TryOpen(string portName, int baudRate, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                serialPort = new SerialPort(portName, baudRate, Parity.None, DataBits, StopBits.One);
                serialPort.Handshake = Handshake.None;
                serialPort.ReadTimeout = TimeoutMilliseconds;
                serialPort.WriteTimeout = TimeoutMilliseconds;
                serialPort.DtrEnable = true;
                serialPort.RtsEnable = true;
                serialPort.Open();
                serialPort.DiscardInBuffer();
                return true;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                Close();
                return false;
            }
        }

        public int ReadAvailableBytes(byte[] buffer)
        {
            if (!IsOpen)
                return 0;

            int availableBytes = serialPort.BytesToRead;

            if (availableBytes <= 0)
                return 0;

            return serialPort.Read(buffer, 0, Math.Min(availableBytes, buffer.Length));
        }

        public void Close()
        {
            if (serialPort == null)
                return;

            try
            {
                if (serialPort.IsOpen)
                    serialPort.Close();

                serialPort.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Error closing serial port: " + e.Message);
            }

            serialPort = null;
        }
    }
}
