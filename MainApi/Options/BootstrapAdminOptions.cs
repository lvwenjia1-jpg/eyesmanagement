namespace MainApi.Options;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public string LoginName { get; set; } = "admin";

    public string DisplayName { get; set; } = "系统管理员";

    public string Password { get; set; } = "123456";

    public string ErpId { get; set; } = "ERP001";

    public string WecomId { get; set; } = "wecom_admin";

    public string Role { get; set; } = "admin";

    public string MachineCode { get; set; } = "DEMO-PC-001";

    public string MachineDescription { get; set; } = "开发环境演示机器";
}
