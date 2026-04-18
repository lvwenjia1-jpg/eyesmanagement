using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;

namespace MachineCodeTool;

internal static class MachineCodeHelper
{
    public static string GetMacByNetworkInterface(bool isGetUserName = true)
    {
        string mac = GetMacOld();

        if (string.IsNullOrWhiteSpace(mac) || mac.Length != 12)
        {
            mac = GetMacNew();
        }

        return mac;
    }

    private static string GetMacOld()
    {
        return GetBestMacAddress(includeDisabledAdapters: false);
    }

    private static string GetMacNew()
    {
        if (OperatingSystem.IsWindows())
        {
            var registryMac = ReadMacFromRegistry();
            if (!string.IsNullOrWhiteSpace(registryMac) && registryMac.Length == 12)
            {
                return registryMac;
            }
        }

        return GetBestMacAddress(includeDisabledAdapters: true);
    }

    private static string GetBestMacAddress(bool includeDisabledAdapters)
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!includeDisabledAdapters && adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var address = adapter.GetPhysicalAddress()?.ToString();
            if (!string.IsNullOrWhiteSpace(address) && address.Length == 12)
            {
                return address.ToUpperInvariant();
            }
        }

        return string.Empty;
    }

    [SupportedOSPlatform("windows")]
    private static string ReadMacFromRegistry()
    {
        using var baseKey = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}");
        if (baseKey == null)
        {
            return string.Empty;
        }

        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var subKey = baseKey.OpenSubKey(subKeyName);
            var networkAddress = subKey?.GetValue("NetworkAddress") as string;
            if (string.IsNullOrWhiteSpace(networkAddress))
            {
                continue;
            }

            var cleaned = NormalizeMac(networkAddress);
            if (cleaned.Length == 12)
            {
                return cleaned;
            }
        }

        return string.Empty;
    }

    private static string NormalizeMac(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = char.ToUpperInvariant(ch);
            }
        }

        return new string(buffer[..index]);
    }
}

internal static class MacUserInfo
{
    public static string UserName => Environment.UserName.Trim();
}
