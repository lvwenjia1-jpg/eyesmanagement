namespace MainApi.Options;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public string LoginName { get; set; } = "admin";

    public string Password { get; set; } = "123456";

    public string ErpId { get; set; } = "ERP001";

    public string Role { get; set; } = "admin";

    public string MachineCode { get; set; } = "DEMO-PC-001";

    public string MachineDescription { get; set; } = "Ubuntu Server";
}
