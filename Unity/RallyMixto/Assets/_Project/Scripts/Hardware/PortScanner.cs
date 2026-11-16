using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace RallyMixto
{
    public enum PortKind { Usb, Bluetooth, System, Unknown }

    public struct PortInfo
    {
        public string name;
        public PortKind kind;
        public string device;
    }

    // lista de puertos com con su tipo
    public static class PortScanner
    {
        public static List<PortInfo> Scan()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    if (key != null)
                        foreach (var v in key.GetValueNames())
                            if (key.GetValue(v) is string com) map[com] = v;
                }
            }
            catch { }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var n in SerialPort.GetPortNames()) names.Add(n.Trim()); } catch { }
            foreach (var n in map.Keys) names.Add(n);

            var list = new List<PortInfo>();
            foreach (var n in names)
            {
                map.TryGetValue(n, out var dev);
                list.Add(new PortInfo { name = n, device = dev ?? "", kind = Classify(dev) });
            }
            return list.OrderBy(p => p.kind == PortKind.System ? 1 : 0)
                       .ThenBy(p => Num(p.name)).ToList();
        }

        static PortKind Classify(string dev)
        {
            if (string.IsNullOrEmpty(dev)) return PortKind.Unknown;
            var d = dev.ToLowerInvariant();
            if (d.Contains("bth")) return PortKind.Bluetooth;
            if (d.Contains("usb") || d.Contains("silab") || d.Contains("ch34") || d.Contains("ch9") || d.Contains("vcp") || d.Contains("ftdi") || d.Contains("prolific"))
                return PortKind.Usb;
            if (d.Contains("\\serial")) return PortKind.System;
            return PortKind.Unknown;
        }

        static int Num(string n)
        {
            var digits = new string(n.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var v) ? v : 999;
        }

        public static string KindLabel(PortKind k)
        {
            switch (k)
            {
                case PortKind.Usb: return "USB";
                case PortKind.Bluetooth: return "BLUETOOTH";
                case PortKind.System: return "SISTEMA";
                default: return "SERIAL";
            }
        }
    }
}
