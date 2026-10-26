#nullable disable
using System.Net;
using System.Net.Sockets;
using NewLife.Net;

namespace NewLife.DNS.Server;

/// <summary>把原始 DNS 报文转发到一组上级，取配置顺序中最后一个成功响应。</summary>
static class DnsRelay
{
    public static Byte[] Query(IReadOnlyList<NetUri> servers, Byte[] message, Int32 timeout = 1000)
    {
        if (servers == null || servers.Count == 0 || message == null || message.Length == 0) return null;

        var tasks = new Task<Byte[]>[servers.Count];
        for (var i = 0; i < servers.Count; i++)
        {
            var server = servers[i];
            tasks[i] = Task.Run(() => QueryOne(server, message, timeout));
        }
        try
        {
            Task.WaitAll(tasks, timeout);
        }
        catch (AggregateException)
        {
        }

        Byte[] last = null;
        foreach (var task in tasks)
        {
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null && task.Result.Length > 0)
                last = task.Result;
        }
        return last;
    }

    public static Byte[] Unwrap(Byte[] raw, Boolean tcp)
    {
        if (!tcp || raw == null || raw.Length < 2) return raw;
        var length = (raw[0] << 8) | raw[1];
        if (length <= 0 || raw.Length < 2 + length) return raw;
        var message = new Byte[length];
        Buffer.BlockCopy(raw, 2, message, 0, length);
        return message;
    }

    public static Byte[] Wrap(Byte[] message, Boolean tcp)
    {
        if (!tcp || message == null) return message;
        var framed = new Byte[message.Length + 2];
        framed[0] = (Byte)(message.Length >> 8);
        framed[1] = (Byte)message.Length;
        Buffer.BlockCopy(message, 0, framed, 2, message.Length);
        return framed;
    }

    /// <summary>沿用查询的问题段，返回 SERVFAIL，避免改走别的上级。</summary>
    public static Byte[] ServFail(Byte[] query)
    {
        if (query == null || query.Length < 12) return null;
        var response = new Byte[query.Length];
        Buffer.BlockCopy(query, 0, response, 0, query.Length);
        response[2] |= 0x80;
        response[3] = (Byte)((response[3] & 0xF0) | 0x02);
        response[6] = response[7] = 0;
        response[8] = response[9] = 0;
        response[10] = response[11] = 0;
        return response;
    }

    static Byte[] QueryOne(NetUri server, Byte[] message, Int32 timeout)
    {
        try
        {
            if (server == null) return null;
            if (timeout < 1) timeout = 1;
            return server.IsTcp ? QueryTcp(server, message, timeout) : QueryUdp(server, message, timeout);
        }
        catch
        {
            return null;
        }
    }

    static Byte[] QueryUdp(NetUri server, Byte[] message, Int32 timeout)
    {
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = timeout;
        udp.Client.SendTimeout = timeout;
        udp.Send(message, message.Length, server.EndPoint);
        var remote = new IPEndPoint(IPAddress.Any, 0);
        return udp.Receive(ref remote);
    }

    static Byte[] QueryTcp(NetUri server, Byte[] message, Int32 timeout)
    {
        using var tcp = new TcpClient();
        var connect = tcp.ConnectAsync(server.Address, server.Port);
        if (!connect.Wait(timeout)) return null;
        var stream = tcp.GetStream();
        stream.ReadTimeout = timeout;
        stream.WriteTimeout = timeout;
        var framed = Wrap(message, true);
        stream.Write(framed, 0, framed.Length);
        var head = new Byte[2];
        if (ReadExact(stream, head, 0, 2) < 2) return null;
        var length = (head[0] << 8) | head[1];
        if (length <= 0 || length > 65535) return null;
        var body = new Byte[length];
        return ReadExact(stream, body, 0, length) == length ? body : null;
    }

    static Int32 ReadExact(NetworkStream stream, Byte[] buffer, Int32 offset, Int32 count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, offset + read, count - read);
            if (n <= 0) break;
            read += n;
        }
        return read;
    }
}
