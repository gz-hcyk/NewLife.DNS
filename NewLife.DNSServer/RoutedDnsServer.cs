#nullable disable
using System.Net;
using System.Reflection;
using NewLife.Data;
using NewLife.Log;
using NewLife.Net;
using NewLife.Net.DNS;

namespace NewLife.DNS.Server;

/// <summary>
/// 按客户端来源选择上级的 DNS 代理。
/// 引用的 NewLife.Net 中 DNS 报文读写是空实现，因此这里直接转发原始报文：
/// 命中 <see cref="Routes"/> 时发给该规则的上级，否则发给 <see cref="DNSServer.Parents"/>。
/// </summary>
public class RoutedDnsServer : DNSServer
{
    static readonly FieldInfo OnRequestField = typeof(DNSServer).GetField("OnRequest", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo OnResponseField = typeof(DNSServer).GetField("OnResponse", BindingFlags.Instance | BindingFlags.NonPublic);

    [ThreadStatic]
    static IPAddress _client;

    /// <summary>当前线程正在处理的客户端来源地址。</summary>
    public static IPAddress CurrentClient => _client;

    /// <summary>来源路由。空表表示全部走 <see cref="DNSServer.Parents"/>。</summary>
    public DnsRouteTable Routes { get; set; } = DnsRouteTable.Parse(null);

    /// <summary>接收查询，先给本地规则机会，再按来源或默认上级转发原始报文。</summary>
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

            var request = DnsMessage.TryParse(message);
            DNSEntity local = null;
            if (request != null)
            {
                var args = new DNSEventArgs { Request = request, Session = session?.Session };
                Raise(OnRequestField, args);
                local = args.Response;
            }

            var route = Routes?.Match(_client);
            Byte[] answer = null;
            if (local != null)
                answer = DnsMessage.TryWrite(message, local) ?? DnsRelay.ServFail(message);

            if (answer == null)
            {
                var servers = route != null ? route.Servers : Parents;
                if (route == null && (servers == null || servers.Count == 0))
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
