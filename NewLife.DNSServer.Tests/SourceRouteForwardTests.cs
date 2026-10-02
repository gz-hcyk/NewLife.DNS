using System.Net;
using System.Net.Sockets;
using System.Text;
using NewLife.DNS.Server;
using NewLife.Net;
using NewLife.Net.DNS;
using Xunit;

namespace NewLife.DNSServer.Tests;

public class SourceRouteForwardTests
{
    [Fact]
    public void QueryPacket_ExposesQuestionName()
    {
        var query = DnsPackets.Query("routed.example");
        var entity = DnsMessage.TryParse(query);
        Assert.NotNull(entity);
        Assert.Equal("routed.example", entity!.Questions[0].Name);
    }

    [Fact]
    public void MatchingClient_UsesRouteUpstream_OtherClientKeepsDefault()
    {
        using var routeUpstream = new FakeDns(IPAddress.Parse("1.2.3.4"));
        using var defaultUpstream = new FakeDns(IPAddress.Parse("9.9.9.9"));

        using (var matched = StartProxy(
            $"udp://127.0.0.1:{defaultUpstream.Port}",
            $"127.0.0.1/32=udp://127.0.0.1:{routeUpstream.Port}"))
        {
            var answer = Query(matched.Port, "routed.example");
            Assert.Equal(IPAddress.Parse("1.2.3.4"), answer);
            Assert.Equal(0, defaultUpstream.Hits);
            Assert.True(routeUpstream.Hits > 0);
        }

        using (var unmatched = StartProxy(
            $"udp://127.0.0.1:{defaultUpstream.Port}",
            "10.0.0.0/8=udp://127.0.0.1:" + routeUpstream.Port))
        {
            var fallback = Query(unmatched.Port, "default.example");
            Assert.Equal(IPAddress.Parse("9.9.9.9"), fallback);
            Assert.True(defaultUpstream.Hits > 0);
        }
    }

    [Fact]
    public void LocalAnswer_WinsAndDoesNotQueryRouteUpstream()
    {
        using var upstream = new FakeDns(IPAddress.Parse("1.2.3.4"));
        using var proxy = StartProxy(
            "udp://127.0.0.1:" + upstream.Port,
            "127.0.0.1/32=udp://127.0.0.1:" + upstream.Port);
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

        var answer = Query(proxy.Port, "local.example");
        Assert.Equal(IPAddress.Parse("7.7.7.7"), answer);
        Assert.Equal(0, upstream.Hits);
    }

    [Fact]
    public void MultipleUpstreams_LastSuccessfulAnswerWins()
    {
        using var first = new FakeDns(IPAddress.Parse("1.1.1.1"));
        using var second = new FakeDns(IPAddress.Parse("2.2.2.2"));
        using var proxy = StartProxy(
            "udp://127.0.0.1:" + first.Port,
            $"127.0.0.1=udp://127.0.0.1:{first.Port},udp://127.0.0.1:{second.Port}");

        var answer = Query(proxy.Port, "multi.example");
        Assert.Equal(IPAddress.Parse("2.2.2.2"), answer);
        Assert.True(first.Hits > 0);
        Assert.True(second.Hits > 0);
    }

    static Proxy StartProxy(String parents, String routes)
    {
        var server = new RoutedDnsServer
        {
            Port = 0,
            ProtocolType = NetType.Udp,
            AddressFamily = AddressFamily.InterNetwork,
            Routes = DnsRouteTable.Parse(routes)
        };
        server.SetParents(parents);
        server.Start();

        var port = BoundUdpPort(server);
        if (port <= 0) throw new InvalidOperationException("DNS 代理没有监听端口");
        return new Proxy(port, server);
    }

    /// <summary>当前 NewLife.Core 在端口为 0 时不会把系统分配的端口写回 NetUri，从套接字读取。</summary>
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
        var query = DnsPackets.Query(name);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.ReceiveTimeout = 1500;
                udp.Client.SendTimeout = 1500;
                udp.Connect(IPAddress.Loopback, port);
                udp.Send(query, query.Length);
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var response = udp.Receive(ref remote);
                var answer = DnsPackets.FirstA(response);
                if (answer != null) return answer;
                last = new InvalidOperationException("响应中没有 A 记录");
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new TimeoutException("查询 " + name + " 失败", last);
    }
}

sealed class FakeDns : IDisposable
{
    readonly UdpClient _udp;
    readonly CancellationTokenSource _cts = new();
    public Int32 Port { get; }
    Int32 _hits;
    public Int32 Hits => Volatile.Read(ref _hits);
    readonly IPAddress _answer;

    public FakeDns(IPAddress answer)
    {
        _answer = answer;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _ = Task.Run(Loop);
    }

    async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync();
            }
            catch
            {
                break;
            }
            Interlocked.Increment(ref _hits);
            var response = DnsPackets.Response(result.Buffer, _answer);
            try
            {
                await _udp.SendAsync(response, response.Length, result.RemoteEndPoint);
            }
            catch
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }
}

static class DnsPackets
{
    public static Byte[] Query(String name)
    {
        using var ms = new MemoryStream();
        Write16(ms, 0x1234);
        Write16(ms, 0x0100);
        Write16(ms, 1);
        Write16(ms, 0);
        Write16(ms, 0);
        Write16(ms, 0);
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((Byte)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.WriteByte(0);
        Write16(ms, 1);
        Write16(ms, 1);
        return ms.ToArray();
    }

    public static Byte[] Response(Byte[] query, IPAddress answer)
    {
        var bytes = answer.GetAddressBytes();
        using var ms = new MemoryStream();
        ms.Write(query, 0, query.Length);
        var buf = ms.ToArray();
        buf[2] = 0x81;
        buf[3] = 0x80;
        buf[6] = 0;
        buf[7] = 1;
        using var answerStream = new MemoryStream();
        answerStream.Write(buf, 0, buf.Length);
        answerStream.WriteByte(0xC0);
        answerStream.WriteByte(0x0C);
        Write16(answerStream, 1);
        Write16(answerStream, 1);
        answerStream.WriteByte(0);
        answerStream.WriteByte(0);
        answerStream.WriteByte(0);
        answerStream.WriteByte(60);
        Write16(answerStream, (UInt16)bytes.Length);
        answerStream.Write(bytes, 0, bytes.Length);
        return answerStream.ToArray();
    }

    public static IPAddress? FirstA(Byte[] buf)
    {
        if (buf.Length < 12) return null;
        var ancount = (buf[6] << 8) | buf[7];
        var i = SkipName(buf, 12);
        i += 4;
        for (var a = 0; a < ancount && i < buf.Length; a++)
        {
            i = SkipName(buf, i);
            if (i + 10 > buf.Length) return null;
            var type = (buf[i] << 8) | buf[i + 1];
            var rdlen = (buf[i + 8] << 8) | buf[i + 9];
            i += 10;
            if (i + rdlen > buf.Length) return null;
            if (type == 1 && rdlen == 4)
                return new IPAddress(new[] { buf[i], buf[i + 1], buf[i + 2], buf[i + 3] });
            i += rdlen;
        }
        return null;
    }

    static Int32 SkipName(Byte[] buf, Int32 i)
    {
        while (i < buf.Length)
        {
            var len = buf[i];
            if (len == 0) return i + 1;
            if ((len & 0xC0) == 0xC0) return i + 2;
            i += 1 + len;
        }
        return i;
    }

    static void Write16(Stream stream, UInt16 value)
    {
        stream.WriteByte((Byte)(value >> 8));
        stream.WriteByte((Byte)value);
    }
}
