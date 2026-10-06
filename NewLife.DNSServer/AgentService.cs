using NewLife.Agent;
using NewLife.DNS.Entity;
using NewLife.Log;
using NewLife.Net.DNS;
using NewLife.Threading;
using XCode;

namespace NewLife.DNS.Server;

public class AgentService : ServiceBase
{
    #region 属性
    #endregion

    #region 构造函数
    public AgentService()
    {
        ServiceName = "NewLife.DNS";
        Description = "DNS服务器";
    }
    #endregion

    #region 核心
    DNSServer Server;
    TimerX _reload = null!;
    String _appliedServer = "";
    String _appliedRoutes = "";
    String _appliedGroups = "";

    public override void StartWork(String reason)
    {
        // 修改数据库默认目录
        var xcode = XCodeSetting.Current;
        if (xcode.IsNew)
        {
            xcode.ShowSQL = false;
            //xcode.SQLiteDbPath = "..\\Data";
            xcode.Save();
        }

        // 初始化数据库
        Task.Run(() =>
        {
            var n = 0;
            n = Rule.Meta.Count;
            n = Record.Meta.Count;
            n = Visitor.Meta.Count;
        });

        var set = Setting.Current;

        // 启动服务器。RoutedDnsServer 在 OnRequest 期间提供客户端来源地址。
        var svr = new RoutedDnsServer();
        if (set.Debug) svr.Log = XTrace.Log;
        ApplyForward(svr, set, false);

        svr.OnRequest += Server_OnRequest;
        svr.OnResponse += Server_OnResponse;
        svr.OnNew += Server_OnNew;

        svr.Start();

        Server = svr;
        // 与 Setting 的 ReloadTime 一致。文件变更后，第一次访问仍得到旧值并排队加载，下一次才装到转发器。
        _reload = new TimerX(ReloadForward, null, 15_000, 15_000);

        base.StartWork(reason);
    }

    void ApplyForward(RoutedDnsServer svr, Setting set, Boolean reload)
    {
        DnsRuntimeBinder.Apply(svr, set.DNSServer, set.DNSRoutes, set.DomainGroups, out var routes, out var groups);
        foreach (var line in routes.Warnings)
            XTrace.WriteLine(line);
        foreach (var line in groups.Warnings)
            XTrace.WriteLine(line);
        if (reload)
            XTrace.WriteLine("转发配置已重新装载，来源路由 {0} 条，域名组 {1} 个", routes.Rules.Count, groups.Groups.Count);
        else
        {
            if (routes.Rules.Count > 0)
                XTrace.WriteLine("已启用来源路由 {0} 条", routes.Rules.Count);
            if (groups.Groups.Count > 0)
                XTrace.WriteLine("已启用域名组 {0} 个", groups.Groups.Count);
        }

        _appliedServer = set.DNSServer ?? "";
        _appliedRoutes = set.DNSRoutes ?? "";
        _appliedGroups = set.DomainGroups ?? "";
    }

    void ReloadForward(Object state)
    {
        try
        {
            var set = Setting.Current;
            var server = set.DNSServer ?? "";
            var routes = set.DNSRoutes ?? "";
            var groups = set.DomainGroups ?? "";
            if (server == _appliedServer && routes == _appliedRoutes && groups == _appliedGroups) return;

            var svr = Server as RoutedDnsServer;
            if (svr == null) return;
            ApplyForward(svr, set, true);
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    public override void StopWork(String reason)
    {
        var timer = _reload;
        _reload = null!;
        timer.TryDispose();

        base.StopWork(reason);

        var svr = Server;
        svr.Stop(reason);
        svr.OnRequest -= Server_OnRequest;
        svr.OnResponse -= Server_OnResponse;
        svr.OnNew -= Server_OnNew;
    }
    #endregion

    #region 业务
    void Server_OnRequest(object sender, DNSEventArgs e)
    {
        var dns = e.Request;
        if (dns == null) return;

        // 本地指定规则优先：某个域名解析到哪个 IP，由规则说了算
        var rs = CheckRule(dns);

        // 命中域名组或来源路由时不使用全局记录缓存，避免别的上级的结果串进来。
        var name = dns.Questions != null && dns.Questions.Length > 0 ? dns.Questions[0].Name : null;
        var isolated = rs == null && sender is RoutedDnsServer routed && routed.IsolatesCache(name, RoutedDnsServer.CurrentClient);
        if (rs == null && !isolated) rs = CheckRecord(dns);

        if (rs != null) e.Response = rs;
    }

    DNSEntity CheckRule(DNSEntity dns)
    {
        var rq = dns.Questions[0];
        var list = Rule.FindAllByQueryTypeAndName((Int32)rq.Type, rq.Name);
        if (list == null || list.Count == 0) return null;

        var rs = new DNSEntity();
        rs.Questions = dns.Questions;
        var drs = new List<DNSRecord>();
        foreach (var item in list)
        {
            if (item.QueryType <= 0) continue;

            var r = DNSEntity.CreateRecord((DNSQueryType)item.QueryType);
            r.Name = item.Name;
            if (r.Name[0] == '*') r.Name = r.Name.Substring(1);
            if (r.Name[0] == '.') r.Name = r.Name.Substring(1);
            r.Text = item.Address;
            // 生存时间3分钟
            r.TTL = new TimeSpan(0, 3, 0);
            drs.Add(r);

            item.Hits++;
            item.SaveAsync();
        }
        rs.Answers = drs.ToArray();

        return rs;
    }

    DNSEntity CheckRecord(DNSEntity dns)
    {
        var rq = dns.Questions[0];
        var list = Record.FindAllByQueryTypeAndName((Int32)rq.Type, rq.Name);
        if (list == null || list.Count == 0) return null;

        var rs = new DNSEntity();
        rs.Questions = dns.Questions;

        var drs = new List<DNSRecord>();
        var now = DateTime.Now;
        foreach (var item in list)
        {
            if (item.QueryType <= 0) continue;

            var dr = DNSEntity.CreateRecord((DNSQueryType)item.QueryType);
            dr.Name = item.Name;
            dr.Text = item.Address;

            // 生产时间过期，并且最后更新时间也过期，才去更新
            if (item.Ttl < now && item.Next < now)
            {
                // 生存时间3分钟
                dr.TTL = new TimeSpan(0, 3, 0);

                // 更新数据库记录，3分钟内不要再次去找
                item.Next = now.AddMinutes(3);

                item.Hits++;
                item.SaveAsync();
            }
            else
            {
                dr.TTL = item.Ttl - now;
                if (dr.TTL.TotalSeconds < 60) dr.TTL = new TimeSpan(0, 10, 0);
                drs.Add(dr);
            }
        }
        // 没有任何满足条件的返回，让它去更新吧
        if (drs.Count < 1) return null;

        rs.Answers = drs.ToArray();

        return rs;
    }

    void Server_OnResponse(object sender, DNSEventArgs e)
    {
        var rs = e.Response;
        if (rs == null) return;

        var remote = e.Session?.Remote;
        var rq = rs.Questions[0];

        // 记录历史
        var hi = new History();
        hi.Type = (Int32)rq.Type;
        hi.Name = rq.Name;
        if (remote != null)
        {
            hi.UserIP = remote.EndPoint.Address + "";
            hi.ProtocolType = remote.Type;
        }
        if (rs.Answers != null && rs.Answers.Length > 0)
        {
            //entity.Address = rs.Answers[0].Text;

            foreach (var item in rs.Answers)
            {
                //var dr = hi.CloneEntity(true);
                var dr = new History();
                dr.UserIP = hi.UserIP;
                dr.ProtocolType = hi.ProtocolType;
                dr.Type = (Int32)item.Type;
                dr.Name = item.Name;
                dr.Address = item.Text;
                dr.SaveAsync();
            }
        }
        else
            hi.SaveAsync();

        // 记录访问者
        var vt = Visitor.Check(remote?.Host);
        if (vt != null)
        {
            vt.LastDomainName = rq.Name;
            vt.Hits++;
            vt.LastVisit = DateTime.Now;
            // 单对象缓存会自动保存
            vt.SaveAsync();
        }
    }

    void Server_OnNew(object sender, DNSEventArgs e)
    {
        var rs = e.Response;
        if (rs == null) return;

        var list = new List<DNSRecord>();
        if (rs.Answers != null) list.AddRange(rs.Answers);
        if (rs.Authoritis != null) list.AddRange(rs.Authoritis);
        if (rs.Additionals != null) list.AddRange(rs.Additionals);

        var rq = rs.Questions[0];

        var now = DateTime.Now;
        foreach (var dr in list)
        {
            var entity = Record.FindByQueryTypeAndNameAndAddress((Int32)dr.Type, rq.Name, dr.Text);
            if (entity == null)
            {
                entity = new Record();
                entity.Name = rq.Name;
                entity.Type = (Int32)dr.Type;
                entity.Address = dr.Text;
            }

            entity.Ttl = now.Add(dr.TTL);
            entity.Parent = e.Session.Remote + "";
            entity.UpdateTime = DateTime.Now;

            entity.SaveAsync();
        }
    }
    #endregion
}