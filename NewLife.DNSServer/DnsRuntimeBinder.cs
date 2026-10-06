#nullable disable
using NewLife.Net;

namespace NewLife.DNS.Server;

/// <summary>把 <see cref="Setting"/> 里的转发配置装到正在运行的 <see cref="RoutedDnsServer"/>。</summary>
public static class DnsRuntimeBinder
{
    /// <summary>替换来源路由、域名组和默认上级。已有查询拿到的是替换前的列表。</summary>
    /// <param name="server">正在转发的服务</param>
    /// <param name="dnsServer">默认上级原文</param>
    /// <param name="routes">来源路由原文</param>
    /// <param name="groups">域名组原文</param>
    /// <param name="routeTable">装载后的来源路由</param>
    /// <param name="groupTable">装载后的域名组</param>
    public static void Apply(RoutedDnsServer server, String dnsServer, String routes, String groups, out DnsRouteTable routeTable, out DnsDomainGroupTable groupTable)
    {
        routeTable = DnsRouteTable.Parse(routes);
        groupTable = DnsDomainGroupTable.Parse(groups, AppDomain.CurrentDomain.BaseDirectory);
        server.Routes = routeTable;
        server.DomainGroups = groupTable;

        var parents = new List<NetUri>();
        parents.AddRange(server.GetLocalDNS());
        server.Parents = parents;
        if (!String.IsNullOrWhiteSpace(dnsServer))
            server.SetParents(dnsServer);
    }
}
