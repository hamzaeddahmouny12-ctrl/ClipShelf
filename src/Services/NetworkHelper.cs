using System.Net;
using System.Net.Sockets;

namespace ClipShelf.Services;

public static class NetworkHelper
{
    /// <summary>Gets the local IPv4 address used for the default route (same network as your router/phone).</summary>
    public static string GetLocalLanIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
                return ep.Address.ToString();
        }
        catch { }
        return GetLocalIpFromHost();
    }

    private static string GetLocalIpFromHost()
    {
        try
        {
            string hostName = Dns.GetHostName();
            var entry = Dns.GetHostEntry(hostName);
            return entry.AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    /// <summary>Tries to add a Windows Firewall rule for the given port. Returns true only if run as admin.</summary>
    public static bool TryAddFirewallRule(int port, string ruleName = "ClipShelf Phone Sync")
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(start);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
