using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using NewLife.Cube;
using NewLife.DNS.Server;
using XCode.Membership;
using DnsSetting = NewLife.DNS.Server.Setting;

namespace NewLife.DNS.Web.Controllers;

/// <summary>编辑默认上级、来源路由和域名组，写回 Config/DNS.config。</summary>
[DNSArea]
[DisplayName("转发设置")]
[Menu(90, true, Icon = "fa-exchange")]
public class ForwardSettingController : Controller
{
    /// <summary>查看当前转发配置。返回 ActionResult，Cube 才会把查看权限写进菜单。</summary>
    [EntityAuthorize(PermissionFlags.Detail)]
    public ActionResult Index()
    {
        var set = DnsSetting.Current;
        var draft = DnsForwardConfig.Load(set.DNSServer, set.DNSRoutes, set.DomainGroups, ConfigRoot(set));
        Prepare(set);
        return View(draft);
    }

    /// <summary>校验并保存。失败时停在本页，不改配置文件。</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EntityAuthorize(PermissionFlags.Update)]
    public ActionResult Save([Bind(Prefix = "")] DnsForwardDraft draft)
    {
        draft ??= new DnsForwardDraft();
        var set = DnsSetting.Current;
        var root = ConfigRoot(set);
        if (!DnsForwardConfig.TryBuild(draft, root, out var dnsServer, out var routes, out var groups, out var errors))
        {
            ViewBag.Errors = errors;
            Prepare(set);
            return View("Index", draft);
        }

        set.DNSServer = dnsServer;
        set.DNSRoutes = routes;
        set.DomainGroups = groups;
        set.Save();

        TempData["Success"] = "已保存到配置文件。DNS 服务约 15～30 秒内重新装载，不必重启。";
        return RedirectToAction(nameof(Index));
    }

    void Prepare(DnsSetting set)
    {
        ViewBag.Title = "转发设置";
        ViewBag.ConfigFile = set.ConfigFile;
    }

    /// <summary>域名文件相对的目录，即 Config 文件夹的上一级。</summary>
    static String ConfigRoot(DnsSetting set)
    {
        var file = set?.ConfigFile;
        if (String.IsNullOrWhiteSpace(file)) return AppContext.BaseDirectory;
        var configDir = Path.GetDirectoryName(file);
        var root = String.IsNullOrWhiteSpace(configDir) ? null : Path.GetDirectoryName(configDir);
        return String.IsNullOrWhiteSpace(root) ? AppContext.BaseDirectory : root;
    }
}
