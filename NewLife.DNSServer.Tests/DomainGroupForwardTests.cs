using System.Net;
using System.Net.Sockets;
using NewLife.DNS.Server;
using NewLife.Net;
using NewLife.Net.DNS;
using Xunit;

namespace NewLife.DNSServer.Tests;

[Collection("LoopbackDns")]
public class DomainGroupForwardTests
{
    [Fact(DisplayName = "命中域名组只访问组上级，未命中走来源路由")]
    public void HitUsesGroupUpstream_MissUsesSourceRoute()
    {
        using var groupUpstream = new FakeDns(IPAddress.Parse("1.1.1.1"));
        using var routeUpstream = new FakeDns(IPAddress.Parse("2.2.2.2"));
        using var defaultUpstream = new FakeDns(IPAddress.Parse("3.3.3.3"));
        using var proxy = StartProxy(
            $"udp://127.0.0.1:{defaultUpstream.Port}",
            $"127.0.0.1/32=udp://127.0.0.1:{routeUpstream.Port}",
            $"example.com=udp://127.0.0.1:{groupUpstream.Port}");

        var grouped = Query(proxy.Port, "WwW.Example.COM");
        Assert.Equal(IPAddress.Parse("1.1.1.1"), grouped);
        Assert.True(groupUpstream.Hits > 0);
        Assert.Equal(0, routeUpstream.Hits);
        Assert.Equal(0, defaultUpstream.Hits);

        var groupHits = groupUpstream.Hits;
        var routed = Query(proxy.Port, "other.test");
        Assert.Equal(IPAddress.Parse("2.2.2.2"), routed);
        Assert.Equal(groupHits, groupUpstream.Hits);
        Assert.True(routeUpstream.Hits > 0);
        Assert.Equal(0, defaultUpstream.Hits);
    }

    [Fact(DisplayName = "域名组和来源路由都未命中时走默认上级")]
    public void Miss_UsesDefaultParent()
    {
        using var groupUpstream = new FakeDns(IPAddress.Parse("1.1.1.1"));
        using var routeUpstream = new FakeDns(IPAddress.Parse("2.2.2.2"));
        using var defaultUpstream = new FakeDns(IPAddress.Parse("3.3.3.3"));
        using var proxy = StartProxy(
            $"udp://127.0.0.1:{defaultUpstream.Port}",
            $"10.0.0.0/8=udp://127.0.0.1:{routeUpstream.Port}",
            $"example.com=udp://127.0.0.1:{groupUpstream.Port}");

        Assert.Equal(IPAddress.Parse("1.1.1.1"), Query(proxy.Port, "api.example.com"));
        Assert.Equal(0, routeUpstream.Hits);
        Assert.Equal(0, defaultUpstream.Hits);

        Assert.Equal(IPAddress.Parse("3.3.3.3"), Query(proxy.Port, "other.test"));
        Assert.True(defaultUpstream.Hits > 0);
        Assert.Equal(0, routeUpstream.Hits);
    }

    [Fact(DisplayName = "本地规则优先于域名组，且不再访问组上级")]
    public void LocalAnswer_WinsOverDomainGroup()
    {
        using var groupUpstream = new FakeDns(IPAddress.Parse("1.1.1.1"));
        using var proxy = StartProxy(
            "udp://127.0.0.1:" + groupUpstream.Port,
            "127.0.0.1/32=udp://127.0.0.1:" + groupUpstream.Port,
            "example.com=udp://127.0.0.1:" + groupUpstream.Port);
        proxy.Server.OnRequest += (_, e) =>
        {
            if (e.Request?.Questions == null || e.Request.Questions.Length == 0) return;
            var record = DNSEntity.CreateRecord(DNSQueryType.A);
            record.Name = e.Request.Questions[0].Name;
            record.Text = "7.7.7.7";
            record.TTL = TimeSpan.FromSeconds(30);
            e.Response = new DNSEntity
            {
                Questions = e.Request.Questions,
                Answers = new[] { record }
            };
        };

        Assert.Equal(IPAddress.Parse("7.7.7.7"), Query(proxy.Port, "www.example.com"));
        Assert.Equal(0, groupUpstream.Hits);
    }

    [Fact(DisplayName = "域名组上级都无应答时返回 SERVFAIL，不改走其他上级")]
    public void SilentGroupUpstream_ReturnsServFail()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var silentPort = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
        using var fallback = new FakeDns(IPAddress.Parse("9.9.9.9"));
        using var proxy = StartProxy(
            $"udp://127.0.0.1:{fallback.Port}",
            $"127.0.0.1/32=udp://127.0.0.1:{fallback.Port}",
            $"down.example=udp://127.0.0.1:{silentPort}");

        var response = QueryRaw(proxy.Port, "a.down.example");
        Assert.True(response.Length >= 12);
        Assert.Equal(0x80, response[2] & 0x80);
        Assert.Equal(2, response[3] & 0x0F);
        Assert.Equal(0, fallback.Hits);
    }

    [Fact(DisplayName = "域名组多个上级时沿用最后一条成功应答")]
    public void MultipleUpstreams_LastSuccessfulAnswerWins()
    {
        using var first = new FakeDns(IPAddress.Parse("1.1.1.1"));
        using var second = new FakeDns(IPAddress.Parse("2.2.2.2"));
        using var proxy = StartProxy(
            "udp://127.0.0.1:" + first.Port,
            "",
            $"example.com=udp://127.0.0.1:{first.Port},udp://127.0.0.1:{second.Port}");

        Assert.Equal(IPAddress.Parse("2.2.2.2"), Query(proxy.Port, "www.example.com"));
        Assert.True(first.Hits > 0);
        Assert.True(second.Hits > 0);
    }

    static Proxy StartProxy(String parents, String routes, String groups)
    {
        var server = new RoutedDnsServer
        {
            Port = 0,
            ProtocolType = NetType.Udp,
            AddressFamily = AddressFamily.InterNetwork,
            Routes = DnsRouteTable.Parse(routes),
            DomainGroups = DnsDomainGroupTable.Parse(groups)
        };
        server.SetParents(parents);
        server.Start();

        var port = BoundUdpPort(server);
        if (port <= 0) throw new InvalidOperationException("DNS 代理没有监听端口");
        return new Proxy(port, server);
    }

    static Int32 BoundUdpPort(NewLife.Net.DNS.DNSServer server)
    {
        if (server.Port > 0) return server.Port;
        foreach (var item in server.Servers)
        {
            var socket = item?.GetType().GetProperty("Client")?.GetValue(item) as Socket;
            if (socket?.LocalEndPoint is IPEndPoint ep && ep.Port > 0 && item!.Local.IsUdp)
                return ep.Port;
        }
        return 0;
    }

    sealed class Proxy : IDisposable
    {
        public Int32 Port { get; }
        public RoutedDnsServer Server { get; }

        public Proxy(Int32 port, RoutedDnsServer server)
        {
            Port = port;
            Server = server;
        }

        public void Dispose()
        {
            try { Server.Stop("test"); } catch { }
            try { Server.Dispose(); } catch { }
        }
    }

    static IPAddress Query(Int32 port, String name)
    {
        var response = QueryRaw(port, name);
        var answer = DnsPackets.FirstA(response);
        if (answer == null) throw new InvalidOperationException("响应中没有 A 记录");
        return answer;
    }

    static Byte[] QueryRaw(Int32 port, String name)
    {
        var query = DnsPackets.Query(name);
        var deadline = DateTime.UtcNow.AddSeconds(8);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.ReceiveTimeout = 2500;
                udp.Client.SendTimeout = 1500;
                udp.Connect(IPAddress.Loopback, port);
                udp.Send(query, query.Length);
                var remote = new IPEndPoint(IPAddress.Any, 0);
                return udp.Receive(ref remote);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new TimeoutException("查询 " + name + " 失败", last);
    }
}
