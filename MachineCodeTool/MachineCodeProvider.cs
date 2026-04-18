namespace MachineCodeTool;

public static class MachineCodeProvider
{
    public static string GetMachineCode()
    {
        return MachineCodeHelper.GetMacByNetworkInterface();
    }
}
