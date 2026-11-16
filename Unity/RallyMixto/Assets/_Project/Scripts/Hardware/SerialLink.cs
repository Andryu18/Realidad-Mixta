using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace RallyMixto
{
    public interface ILink
    {
        bool IsOpen { get; }
        string Label { get; }
        bool TryRead(out string line);
        void Send(string line);
        string Error { get; }
        void Close();
    }

    // comunicacion serial con el control (usb o bluetooth spp)
    public class SerialLink : ILink
    {
        public const int Baud = 115200;

        readonly ConcurrentQueue<string> rx = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> tx = new ConcurrentQueue<string>();
        SerialPort port;
        Thread thread;
        volatile bool running;
        volatile bool open;
        volatile string error;

        public string Label { get; private set; }
        public bool IsOpen => open;
        public bool Opening => running && !open && error == null;
        public string Error => error;

        public SerialLink(string portName) { Label = portName; }

        // se abre en otro hilo: bluetooth puede tardar varios segundos
        public void OpenAsync()
        {
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "serial-" + Label };
            thread.Start();
        }

        void Run()
        {
            try
            {
                port = new SerialPort(Label, Baud, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 40,
                    WriteTimeout = 500,
                    DtrEnable = false,
                    RtsEnable = false,
                    NewLine = "\n",
                    Encoding = Encoding.ASCII
                };
                port.Open();
                open = true;
            }
            catch (UnauthorizedAccessException) { error = "busy"; running = false; return; }
            catch (Exception e) { error = e.Message; running = false; return; }

            var buf = new byte[512];
            var sb = new StringBuilder();
            while (running)
            {
                try
                {
                    while (tx.TryDequeue(out var o)) port.Write(o + "\n");

                    int n = 0;
                    try { n = port.Read(buf, 0, buf.Length); }
                    catch (TimeoutException) { }

                    for (int i = 0; i < n; i++)
                    {
                        char c = (char)buf[i];
                        if (c == '\n')
                        {
                            if (sb.Length > 0) rx.Enqueue(sb.ToString());
                            sb.Length = 0;
                        }
                        else if (c != '\r' && sb.Length < 200) sb.Append(c);
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                    break;
                }
            }
            open = false;
            try { port?.Close(); } catch { }
        }

        public bool TryRead(out string line) => rx.TryDequeue(out line);

        public void Send(string line)
        {
            if (open) tx.Enqueue(line);
        }

        public void Close()
        {
            running = false;
            try { thread?.Join(600); } catch { }
            try { port?.Close(); } catch { }
            open = false;
        }
    }
}
