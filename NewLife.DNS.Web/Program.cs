using NewLife.Cube;
using NewLife.Cube.WebMiddleware;
using NewLife.DNS.Server;
using NewLife.Log;
using Stardust;

XTrace.UseConsole();

var builder = WebApplication.CreateBuilder(args);
ApplyDnsConfigPath(builder.Configuration);
var services = builder.Services;

// 引入星尘，设置监控中间件
var star = services.AddStardust(null);
TracerMiddleware.Tracer = star?.Tracer;

// 启用接口响应压缩
//services.AddResponseCompression();

services.AddControllersWithViews();
services.AddCube();

var app = builder.Build();
app.UseStaticFiles();

app.UseCube(builder.Environment);
app.UseCubeHome();

app.UseAuthorization();
//app.UseResponseCompression();
//app.MapControllerRoute(name: "default", pattern: "{controller=Index}/{action=Index}/{id?}");
//app.MapControllerRoute(name: "default2", pattern: "{area=Admin}/{controller=Index}/{action=Index}/{id?}");

app.Run();

// 管理站与 DNS 服务是两个程序。输出目录分别是 Bin/Web 与 Bin/Server 时，编辑服务那份 Config/DNS.config。
static void ApplyDnsConfigPath(IConfiguration configuration)
{
    var configured = configuration["DNS:ConfigFile"];
    if (!String.IsNullOrWhiteSpace(configured))
    {
        NewLife.DNS.Server.Setting._.ConfigFile = configured;
        return;
    }

    var serverHome = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Server"));
    if (File.Exists(Path.Combine(serverHome, "NewLife.DNSServer.dll")))
        NewLife.DNS.Server.Setting._.ConfigFile = Path.Combine(serverHome, "Config", "DNS.config");
}