using MainApi.Data;
using MainApi.Options;
using MainApi.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.Configure<MockOrderSeedOptions>(builder.Configuration.GetSection(MockOrderSeedOptions.SectionName));
builder.Services.Configure<DashboardSeedOptions>(builder.Configuration.GetSection(DashboardSeedOptions.SectionName));
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<MySqlConnectionFactory>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<MockOrderDataSeeder>();
builder.Services.AddSingleton<DashboardSeedDataSeeder>();
builder.Services.AddSingleton<CatalogPricingSeedDataSeeder>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<MachineRepository>();
builder.Services.AddScoped<BusinessGroupRepository>();
builder.Services.AddScoped<DashboardOrderRepository>();
builder.Services.AddScoped<UploadRepository>();
builder.Services.AddScoped<PriceRuleRepository>();
builder.Services.AddScoped<ProductCatalogRepository>();
builder.Services.AddScoped<SystemRepository>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MainApi",
        Version = "v1",
        Description = "Dashboard backend APIs for users, machine codes, business groups, and orders."
    });

});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", true);

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
await app.Services.GetRequiredService<DashboardSeedDataSeeder>().SeedAsync();
await app.Services.GetRequiredService<CatalogPricingSeedDataSeeder>().SeedAsync();
await app.Services.GetRequiredService<MockOrderDataSeeder>().SeedAsync();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MainApi v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "MainApi Swagger UI";
    });
}

var dashboardPathCandidates = new[]
{
    Path.Combine(app.Environment.ContentRootPath, "Dasbord"),
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Dasbord"))
};

var dashboardPath = dashboardPathCandidates.FirstOrDefault(Directory.Exists);
if (Directory.Exists(dashboardPath))
{
    var dashboardProvider = new PhysicalFileProvider(dashboardPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = dashboardProvider,
        RequestPath = "/dashboard"
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = dashboardProvider,
        RequestPath = "/dashboard"
    });
}

app.UseForwardedHeaders();

if (builder.Configuration.GetValue("UseHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseCors("Dashboard");

app.MapGet("/", () => swaggerEnabled
    ? Results.Redirect("/swagger", permanent: false)
    : Results.Ok(new { name = "MainApi", status = "running" }));
app.MapControllers();

app.Run();
