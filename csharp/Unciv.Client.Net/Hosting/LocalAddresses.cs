using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Unciv.Client.Net.Hosting;

/// <summary>Where an address of this machine can be reached from.</summary>
public enum AddressScope
{
    /// <summary>A private range (192.168.x.x, 10.x.x.x, 172.16-31.x.x) or an IPv6 unique-local address: friends on the same network.</summary>
    Lan,

    /// <summary>A routable address on this machine's own interface: friends over the internet, if the port is open.</summary>
    Public,
}

/// <summary>One address the host can put in an invite.</summary>
public sealed record LocalAddress(string Address, string Interface, AddressScope Scope);

/// <summary>
/// C034: the addresses of this machine, for the host screen's list. IPv4 only, loopback and link-local (169.254.x.x) left out: an invite with one of those would
/// never work for a friend. Lan addresses come first, then public ones, each group in interface order.
/// </summary>
public static class LocalAddresses
{
    /// <summary>The usable addresses of every network interface that is up.</summary>
    public static IReadOnlyList<LocalAddress> Enumerate()
    {
        var found = new List<LocalAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (Classify(unicast.Address) is { } scope) found.Add(new LocalAddress(unicast.Address.ToString(), nic.Name, scope));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // No interface information (sandbox, no permission): the host types an address instead.
        }
        return found.Where(it => it.Scope == AddressScope.Lan).Concat(found.Where(it => it.Scope == AddressScope.Public)).ToList();
    }

    /// <summary>The scope of an IPv4 address, or null for one that is no use in an invite (loopback, link-local, unspecified, multicast, IPv6).</summary>
    public static AddressScope? Classify(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return null;
        var b = address.GetAddressBytes();
        if (b[0] == 127 || b[0] == 0 || b[0] >= 224) return null;
        if (b[0] == 169 && b[1] == 254) return null;
        bool isPrivate = b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127);
        return isPrivate ? AddressScope.Lan : AddressScope.Public;
    }
}
