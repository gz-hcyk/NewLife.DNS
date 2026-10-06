#nullable disable
using System.Net;
using System.Reflection;
using NewLife.Data;
using NewLife.Log;
using NewLife.Net;
using NewLife.Net.DNS;

namespace NewLife.DNS.Server;

/// <summary>
/// 按域名组和客户端来源选择上级的 DNS 代理。
/// 引用的 NewLife.Net 中 DNS 报文读写是空实现，因此这里直接转发原始报文。
/// 优先级：本地规则，域名组 <see cref="DomainGroups"/>，来源路由 <see cref="Routes"/>，最后 <see cref="DNSServer.Parents"/>。
/// 域名组或来源路由命中后，该组上级全部无应答时返回 SERVFAIL，不再改走下一级。
/// </summary>
public class RoutedDnsServer : DNSServer
{
    static readonly FieldInfo OnRequestField = typeof(DNSServer).GetField("OnRequest", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo OnResponseField = typeof(DNSServer).GetField("OnResponse", BindingFlags.Instance | BindingFlags.NonPublic);

    [ThreadStatic]
    static IPAddress _client;

    /// <summary>当前线程正在处理的客户端来源地址。</summary>
    public static IPAddress CurrentClient => _client;

    /// <summary>来源路由。空表表示不按来源选择上级。</summary>
    public DnsRouteTable Routes { get; set; } = DnsRouteTable.Parse(null);

    /// <summary>域名组。空表表示不按域名选择上级。</summary>
    public DnsDomainGroupTable DomainGroups { get; set; } = DnsDomainGroupTable.Parse(null);

    /// <summary>该查询是否应避开全局记录缓存。</summary>
    /// <param name="name">查询域名。大小写和末尾点不影响判断</param>
    /// <param name="client">客户端来源地址，可为 null</param>
    /// <returns>命中域名组或来源路由时为 true。默认上级仍允许使用全局缓存</returns>
    /// <remarks>不同上级得到的结果不能从同一份全局记录缓存读出。域名组优先于来源路由，两者命中任一即隔离。</remarks>
    public Boolean IsolatesCache(String name, IPAddress client)
    {
        if (DomainGroups != null && DomainGroups.Match(name) != null) return true;
        return Routes != null && Routes.Match(client) != null;
    }

    /// <summary>接收查询。先给本地规则机会，再按域名组、来源或默认上级转发原始报文。</summary>
    protected override void OnReceive(INetSession session, IPacket pk)
    {
        var isTcp = session?.Session?.Local?.IsTcp ?? false;
        var message = DnsRelay.Unwrap(ReadRaw(pk), isTcp);
        var previous = _client;
        _client = ClientAddress(session);
        var calledBase = false;
        try
        {
            if (message == null || message.Length < 12)
            {
                base.OnReceive(session, pk);
                calledBase = true;
                return;
            }

            var qname = DnsMessage.TryGetQuestionName(message);
            var request = DnsMessage.TryParse(message);
            DNSEntity local = null;
            if (request != null)
            {
                var args = new DNSEventArgs { Request = request, Session = session?.Session };
                Raise(OnRequestField, args);
                local = args.Response;
            }

            var group = DomainGroups?.Match(qname);
            var route = group == null ? Routes?.Match(_client) : null;
            Byte[] answer = null;
            if (local != null)
                answer = DnsMessage.TryWrite(message, local) ?? DnsRelay.ServFail(message);

            if (answer == null)
            {
                var pinned = group != null || route != null;
                var servers = group != null ? group.Servers : route != null ? route.Servers : Parents;
                if (!pinned && (servers == null || servers.Count == 0))
                {
                    base.OnReceive(session, pk);
                    calledBase = true;
                    return;
                }

                answer = DnsRelay.Query(servers, message) ?? DnsRelay.ServFail(message);
                if (request != null && answer != null)
                {
                    var parsed = DnsMessage.TryParse(answer);
                    if (parsed != null)
                    {
                        var args = new DNSEventArgs { Request = request, Response = parsed, Session = session?.Session };
                        Raise(OnResponseField, args);
                    }
                }
            }
            else if (request != null)
            {
                var args = new DNSEventArgs { Request = request, Response = local, Session = session?.Session };
                Raise(OnResponseField, args);
            }

            if (answer != null && session != null)
            {
                var wire = DnsRelay.Wrap(answer, isTcp);
                session.Send(wire, 0, wire.Length);
            }
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
        finally
        {
            _client = previous;
            if (!calledBase)
            {
                try { session?.Dispose(); } catch { }
            }
        }
    }

    /// <summary>处理请求。来源地址供仍进入基类流程时使用。</summary>
    protected override DNSEntity Request(INetSession session, DNSEntity request)
    {
        var previous = _client;
        _client = ClientAddress(session) ?? _client;
        try
        {
            return base.Request(session, request);
        }
        finally
        {
            _client = previous;
        }
    }

    static Byte[] ReadRaw(IPacket packet)
    {
        if (packet == null) return null;
        if (packet is Packet owned) return owned.ToArray();
        if (packet is ArrayPacket array)
        {
            if (array.Buffer == null || array.Length <= 0) return null;
            var raw = new Byte[array.Length];
            Buffer.BlockCopy(array.Buffer, array.Offset, raw, 0, array.Length);
            return raw;
        }
        return null;
    }

    void Raise(FieldInfo field, DNSEventArgs args)
    {
        try
        {
            if (field?.GetValue(this) is Delegate handler)
                handler.DynamicInvoke(this, args);
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    static IPAddress ClientAddress(INetSession session)
    {
        var uri = session?.Session?.Remote ?? session?.Remote;
        if (uri == null) return null;
        return uri.Address ?? uri.EndPoint?.Address;
    }
}
