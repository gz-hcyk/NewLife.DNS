#nullable disable
using System.Net;
using System.Net.Sockets;
using NewLife.Net;

namespace NewLife.DNS.Server;

/// <summary>按客户端来源 IP / CIDR / 起止范围选择上级 DNS。</summary>
/// <remarks>
/// 配置与 <see cref="Setting.DNSServer"/> 相同，写在 <see cref="Setting.DNSRoutes"/>：
/// 多条规则用分号或换行分隔，每条为 <c>来源=上级列表</c>。
/// 同时命中时前缀更长者优先，长度相同则配置靠前者优先。
/// </remarks>
public sealed class DnsRouteTable
{
    /// <summary>解析得到的规则，顺序与配置一致。</summary>
    public IReadOnlyList<DnsRoute> Rules { get; }

    /// <summary>被忽略的配置片段说明。</summary>
    public IReadOnlyList<String> Warnings { get; }

    DnsRouteTable(IReadOnlyList<DnsRoute> rules, IReadOnlyList<String> warnings)
    {
        Rules = rules;
        Warnings = warnings;
    }

    /// <summary>解析路由配置。空白配置得到空表，不抛异常。</summary>
    public static DnsRouteTable Parse(String text)
    {
        var rules = new List<DnsRoute>();
        var warnings = new List<String>();
        if (String.IsNullOrWhiteSpace(text)) return new DnsRouteTable(rules, warnings);

        var segments = text.Split(new[] { '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in segments)
        {
            var segment = raw.Trim();
            if (segment.Length == 0 || segment[0] == '#' || segment.StartsWith("//")) continue;

            var eq = segment.IndexOf('=');
            if (eq <= 0 || eq == segment.Length - 1)
            {
                warnings.Add("忽略无法识别的来源路由：" + segment);
                continue;
            }

            var sourceText = segment.Substring(0, eq).Trim();
            var upstreamText = segment.Substring(eq + 1).Trim();
            if (!TryParseSource(sourceText, out var network, out var prefix, out var end, out var error))
            {
                warnings.Add(error + "：" + segment);
                continue;
            }

            var servers = ParseServers(upstreamText, out var serverError);
            if (serverError != null) warnings.Add(serverError + "：" + segment);
            if (servers.Count == 0)
            {
                warnings.Add("忽略没有可用上级的来源路由：" + segment);
                continue;
            }

            rules.Add(new DnsRoute(sourceText, upstreamText, network, prefix, end, servers));
        }

        return new DnsRouteTable(rules, warnings);
    }

    /// <summary>按来源地址选择最具体的规则。未命中返回 null。</summary>
    public DnsRoute Match(IPAddress address)
    {
        if (address == null || Rules.Count == 0) return null;

        var best = MatchCore(address, address.GetAddressBytes());
        if (address.IsIPv4MappedToIPv6)
        {
            var v4 = address.MapToIPv4();
            var mapped = MatchCore(v4, v4.GetAddressBytes());
            if (mapped != null && (best == null || mapped.PrefixLength > best.PrefixLength))
                best = mapped;
        }
        return best;
    }

    DnsRoute MatchCore(IPAddress address, Byte[] bytes)
    {
        DnsRoute best = null;
        foreach (var rule in Rules)
        {
            if (!rule.Contains(address, bytes)) continue;
            if (best == null || rule.PrefixLength > best.PrefixLength)
                best = rule;
        }
        return best;
    }

    /// <summary>解析上级列表。写法与 <see cref="Setting.DNSServer"/> 相同，逗号分隔。</summary>
    public static List<NetUri> ParseServers(String text, out String error)
    {
        error = null;
        var list = new List<NetUri>();
        if (String.IsNullOrWhiteSpace(text))
        {
            error = "上级 DNS 为空";
            return list;
        }

        var parts = text.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in parts)
        {
            var item = raw.Trim();
            if (item.Length == 0) continue;
            try
            {
                var uri = new NetUri(item);
                if (uri.Port <= 0) uri.Port = 53;
                if (uri.Address != null && (IPAddress.Any.Equals(uri.Address) || IPAddress.IPv6Any.Equals(uri.Address)))
                {
                    error = "任意地址不能作为上级 DNS";
                    continue;
                }
                list.Add(uri);
            }
            catch (Exception ex)
            {
                error = "上级 DNS 无效（" + ex.Message + "）";
            }
        }

        if (list.Count == 0 && error == null) error = "上级 DNS 为空";
        return list;
    }

    static Boolean TryParseSource(String text, out IPAddress network, out Int32 prefix, out IPAddress end, out String error)
    {
        network = null;
        prefix = 0;
        end = null;
        error = null;

        var slash = text.IndexOf('/');
        if (slash > 0)
        {
            var ipText = text.Substring(0, slash).Trim();
            var maskText = text.Substring(slash + 1).Trim();
            if (!IPAddress.TryParse(ipText, out network))
            {
                error = "来源地址无效";
                return false;
            }

            if (Int32.TryParse(maskText, out prefix))
            {
                var max = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                if (prefix < 0 || prefix > max)
                {
                    error = "CIDR 前缀长度无效";
                    return false;
                }
                return true;
            }

            if (!IPAddress.TryParse(maskText, out var mask) || mask.AddressFamily != network.AddressFamily || !TryPrefixFromMask(mask, out prefix))
            {
                error = "来源掩码无效";
                return false;
            }
            return true;
        }

        var dash = text.IndexOf('-');
        if (dash > 0)
        {
            var left = text.Substring(0, dash).Trim();
            var right = text.Substring(dash + 1).Trim();
            if (!IPAddress.TryParse(left, out network) || !IPAddress.TryParse(right, out end) || network.AddressFamily != end.AddressFamily)
            {
                error = "来源地址范围无效";
                return false;
            }

            var startBytes = network.GetAddressBytes();
            var endBytes = end.GetAddressBytes();
            if (Compare(startBytes, endBytes) > 0)
            {
                var swap = network;
                network = end;
                end = swap;
                startBytes = network.GetAddressBytes();
                endBytes = end.GetAddressBytes();
            }
            prefix = CommonPrefix(startBytes, endBytes);
            return true;
        }

        if (!IPAddress.TryParse(text, out network))
        {
            error = "来源地址无效";
            return false;
        }
        prefix = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return true;
    }

    static Boolean TryPrefixFromMask(IPAddress mask, out Int32 prefix)
    {
        prefix = 0;
        var zero = false;
        foreach (var b in mask.GetAddressBytes())
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                var one = (b & (1 << bit)) != 0;
                if (one)
                {
                    if (zero) return false;
                    prefix++;
                }
                else
                    zero = true;
            }
        }
        return true;
    }

    internal static Int32 CommonPrefix(Byte[] left, Byte[] right)
    {
        var bits = 0;
        var n = Math.Min(left.Length, right.Length);
        for (var i = 0; i < n; i++)
        {
            var diff = (Byte)(left[i] ^ right[i]);
            if (diff == 0)
            {
                bits += 8;
                continue;
            }
            for (var bit = 7; bit >= 0; bit--)
            {
                if ((diff & (1 << bit)) != 0) return bits;
                bits++;
            }
        }
        return bits;
    }

    internal static Int32 Compare(Byte[] left, Byte[] right)
    {
        var n = Math.Min(left.Length, right.Length);
        for (var i = 0; i < n; i++)
        {
            var c = left[i].CompareTo(right[i]);
            if (c != 0) return c;
        }
        return left.Length.CompareTo(right.Length);
    }
}

/// <summary>一条来源路由：来源网段对应一组上级 DNS。</summary>
public sealed class DnsRoute
{
    /// <summary>配置中的来源原文，例如 10.0.0.0/8。</summary>
    public String Source { get; }

    /// <summary>配置中的上级原文。</summary>
    public String Upstream { get; }

    /// <summary>用于冲突比较的前缀长度。范围按起止地址的公共前缀计算。</summary>
    public Int32 PrefixLength { get; }

    /// <summary>上级服务器。</summary>
    public IReadOnlyList<NetUri> Servers { get; }

    internal IPAddress Network { get; }
    internal IPAddress End { get; }

    internal DnsRoute(String source, String upstream, IPAddress network, Int32 prefixLength, IPAddress end, IReadOnlyList<NetUri> servers)
    {
        Source = source;
        Upstream = upstream;
        Network = network;
        PrefixLength = prefixLength;
        End = end;
        Servers = servers;
    }

    /// <summary>来源地址是否落在本规则内。</summary>
    public Boolean Contains(IPAddress address)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != Network.AddressFamily) return false;
        return Contains(address, address.GetAddressBytes());
    }

    internal Boolean Contains(IPAddress address, Byte[] bytes)
    {
        if (address.AddressFamily != Network.AddressFamily) return false;
        if (End != null)
        {
            var startBytes = Network.GetAddressBytes();
            var endBytes = End.GetAddressBytes();
            return DnsRouteTable.Compare(startBytes, bytes) <= 0 && DnsRouteTable.Compare(bytes, endBytes) <= 0;
        }
        return InPrefix(bytes, Network.GetAddressBytes(), PrefixLength);
    }

    static Boolean InPrefix(Byte[] address, Byte[] network, Int32 prefixLength)
    {
        if (address.Length != network.Length) return false;
        var full = prefixLength / 8;
        var rem = prefixLength % 8;
        for (var i = 0; i < full; i++)
        {
            if (address[i] != network[i]) return false;
        }
        if (rem == 0) return true;
        if (full >= address.Length) return true;
        var mask = (Byte)(0xFF << (8 - rem));
        return (address[full] & mask) == (network[full] & mask);
    }

    /// <summary>已重载。</summary>
    public override String ToString() => Source + "=" + Upstream;
}
