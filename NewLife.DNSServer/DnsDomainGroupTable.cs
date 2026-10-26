#nullable disable
using System.Globalization;
using NewLife.Net;

namespace NewLife.DNS.Server;

/// <summary>按查询域名选择上级 DNS 的域名组表。</summary>
/// <remarks>
/// 配置写在 <see cref="Setting.DomainGroups"/>，分隔方式与 <see cref="Setting.DNSRoutes"/> 相同：
/// 多条用分号或换行分隔，每条为 <c>域名列表=上级列表</c>。
/// 上级列表写法与 <see cref="Setting.DNSServer"/> 相同。
/// <c>example.com</c> 匹配自身和子域；<c>*.example.com</c> 只匹配子域；<c>exact:example.com</c> 只匹配完整域名。
/// 左边可以写 <c>file:路径</c> 或 <c>@路径</c>，文件每行一个域名。
/// 同时命中时标签更多的后缀优先，标签数相同则配置靠前者优先。
/// </remarks>
public sealed class DnsDomainGroupTable
{
    /// <summary>解析得到的域名组，顺序与配置一致。</summary>
    public IReadOnlyList<DnsDomainGroup> Groups { get; }

    /// <summary>被忽略的配置片段说明。</summary>
    public IReadOnlyList<String> Warnings { get; }

    readonly Node _root = new Node();
    Candidate _any;

    DnsDomainGroupTable(IReadOnlyList<DnsDomainGroup> groups, IReadOnlyList<String> warnings)
    {
        Groups = groups;
        Warnings = warnings;
    }

    /// <summary>解析域名组配置。空白配置得到空表，不抛异常。</summary>
    /// <param name="text">配置文本，可为 null</param>
    /// <param name="baseDirectory">解析相对路径时优先使用的目录。为空则用工作目录和程序目录</param>
    /// <returns>域名组表。无效片段记入 <see cref="Warnings"/>，不抛异常</returns>
    public static DnsDomainGroupTable Parse(String text, String baseDirectory = null)
    {
        var groups = new List<DnsDomainGroup>();
        var warnings = new List<String>();
        var table = new DnsDomainGroupTable(groups, warnings);
        if (String.IsNullOrWhiteSpace(text)) return table;

        var order = 0;
        var segments = text.Split(new[] { '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in segments)
        {
            var segment = raw.Trim();
            if (segment.Length == 0 || segment[0] == '#' || segment.StartsWith("//")) continue;

            var eq = segment.IndexOf('=');
            if (eq <= 0 || eq == segment.Length - 1)
            {
                warnings.Add("忽略无法识别的域名组：" + segment);
                continue;
            }

            var domainText = segment.Substring(0, eq).Trim();
            var upstreamText = segment.Substring(eq + 1).Trim();
            var patterns = new List<DnsDomainPattern>();
            var tokens = domainText.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawToken in tokens)
            {
                var token = rawToken.Trim();
                if (token.Length == 0) continue;
                if (IsFileToken(token))
                {
                    LoadFile(token, baseDirectory, patterns, warnings);
                    continue;
                }
                if (!TryParsePattern(token, out var pattern, out var error))
                {
                    warnings.Add(error + "：" + token);
                    continue;
                }
                patterns.Add(pattern);
            }

            if (patterns.Count == 0)
            {
                warnings.Add("忽略没有可用域名的域名组：" + segment);
                continue;
            }

            var servers = DnsRouteTable.ParseServers(upstreamText, out var serverError);
            if (serverError != null) warnings.Add(serverError + "：" + segment);
            if (servers.Count == 0)
            {
                warnings.Add("忽略没有可用上级的域名组：" + segment);
                continue;
            }

            var group = new DnsDomainGroup(domainText, upstreamText, patterns, servers);
            groups.Add(group);
            foreach (var pattern in patterns)
                table.Add(pattern, group, order++);
        }

        return table;
    }

    /// <summary>按查询名选择最具体的域名组。未命中返回 null。</summary>
    /// <param name="name">查询域名。忽略大小写和末尾的点</param>
    /// <returns>命中的域名组；没有命中时返回 null</returns>
    public DnsDomainGroup Match(String name)
    {
        var normalized = Normalize(name);
        if (normalized == null || Groups.Count == 0) return null;
        if (!TryLabels(normalized, out var labels)) return _any?.Group;

        var best = _any;
        var node = _root;
        for (var i = labels.Length - 1; i >= 0; i--)
        {
            if (node.Children == null || !node.Children.TryGetValue(labels[i], out node))
                break;

            var depth = labels.Length - i;
            var atName = i == 0;
            best = Prefer(best, node.Suffix, depth);
            if (!atName) best = Prefer(best, node.Wildcard, depth);
            if (atName) best = Prefer(best, node.Exact, depth);
        }
        return best?.Group;
    }

    void Add(DnsDomainPattern pattern, DnsDomainGroup group, Int32 order)
    {
        var candidate = new Candidate(group, order, pattern.LabelCount);
        if (pattern.Kind == DomainMatchKind.Any)
        {
            _any = PreferEarlier(_any, candidate);
            return;
        }

        var labels = pattern.Suffix.Split('.');
        var node = _root;
        for (var i = labels.Length - 1; i >= 0; i--)
        {
            node.Children ??= new Dictionary<String, Node>(StringComparer.Ordinal);
            if (!node.Children.TryGetValue(labels[i], out var child))
            {
                child = new Node();
                node.Children.Add(labels[i], child);
            }
            node = child;
        }

        if (pattern.Kind == DomainMatchKind.Wildcard)
            node.Wildcard = PreferEarlier(node.Wildcard, candidate);
        else if (pattern.Kind == DomainMatchKind.Exact)
            node.Exact = PreferEarlier(node.Exact, candidate);
        else
            node.Suffix = PreferEarlier(node.Suffix, candidate);
    }

    static Candidate Prefer(Candidate best, Candidate candidate, Int32 depth)
    {
        if (candidate == null) return best;
        if (best == null || depth > best.Depth || (depth == best.Depth && candidate.Order < best.Order))
            return new Candidate(candidate.Group, candidate.Order, depth);
        return best;
    }

    static Candidate PreferEarlier(Candidate current, Candidate next)
    {
        if (current == null || next.Order < current.Order) return next;
        return current;
    }

    static Boolean IsFileToken(String token)
    {
        if (token.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        return token.Length > 1 && token[0] == '@';
    }

    static void LoadFile(String token, String baseDirectory, List<DnsDomainPattern> patterns, List<String> warnings)
    {
        var path = token[0] == '@' ? token.Substring(1).Trim() : token.Substring(token.IndexOf(':') + 1).Trim();
        path = path.Trim().Trim('"');
        if (path.Length == 0)
        {
            warnings.Add("域名列表文件路径为空：" + token);
            return;
        }

        var full = FindFile(path, baseDirectory);
        if (full == null)
        {
            warnings.Add("域名列表文件不存在：" + path);
            return;
        }

        String[] lines;
        try
        {
            lines = File.ReadAllLines(full);
        }
        catch (Exception ex)
        {
            warnings.Add("域名列表文件无法读取（" + ex.Message + "）：" + path);
            return;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("//")) continue;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash).Trim();
            if (line.Length == 0) continue;
            if (!TryParsePattern(line, out var pattern, out var error))
            {
                warnings.Add(error + "：" + line);
                continue;
            }
            patterns.Add(pattern);
        }
    }

    /// <summary>按与转发相同的规则查找域名列表文件。</summary>
    /// <param name="path">file: 或 @ 后面的相对路径，也可直接写路径</param>
    /// <param name="baseDirectory">优先查找的目录，可为 null</param>
    /// <returns>存在的文件完整路径；找不到时返回 null</returns>
    public static String LocateDomainFile(String path, String baseDirectory)
    {
        if (String.IsNullOrWhiteSpace(path)) return null;
        var text = path.Trim().Trim('"');
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(5).Trim();
        else if (text.Length > 1 && text[0] == '@')
            text = text.Substring(1).Trim();
        return FindFile(text, baseDirectory);
    }

    static String FindFile(String path, String baseDirectory)
    {
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;

        var roots = new List<String>();
        if (!String.IsNullOrWhiteSpace(baseDirectory)) roots.Add(baseDirectory);
        roots.Add(Environment.CurrentDirectory);
        roots.Add(AppDomain.CurrentDomain.BaseDirectory);
        foreach (var root in roots)
        {
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (File.Exists(full)) return full;
        }
        return null;
    }

    static Boolean TryParsePattern(String text, out DnsDomainPattern pattern, out String error)
    {
        pattern = null;
        error = null;
        var kind = DomainMatchKind.Suffix;
        var body = text.Trim();
        if (body.Length == 0)
        {
            error = "域名为空";
            return false;
        }

        if (body.Equals("*", StringComparison.Ordinal))
        {
            pattern = new DnsDomainPattern("*", DomainMatchKind.Any, "", 0);
            return true;
        }
        if (StartsWith(body, "exact:"))
        {
            kind = DomainMatchKind.Exact;
            body = body.Substring(6).Trim();
        }
        else if (StartsWith(body, "suffix:"))
        {
            body = body.Substring(7).Trim();
        }
        else if (body.StartsWith("*.", StringComparison.Ordinal))
        {
            kind = DomainMatchKind.Wildcard;
            body = body.Substring(2).Trim();
        }

        var suffix = Normalize(body);
        if (suffix == null || !TryLabels(suffix, out var labels))
        {
            error = "域名无效";
            return false;
        }

        var display = suffix;
        if (kind == DomainMatchKind.Exact) display = "exact:" + suffix;
        else if (kind == DomainMatchKind.Wildcard) display = "*." + suffix;
        pattern = new DnsDomainPattern(display, kind, suffix, labels.Length);
        return true;
    }

    static Boolean StartsWith(String text, String prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    internal static String Normalize(String name)
    {
        if (String.IsNullOrWhiteSpace(name)) return null;
        var text = name.Trim().Trim('.');
        if (text.Length == 0) return null;
        text = text.ToLowerInvariant();
        try
        {
            text = new IdnMapping { UseStd3AsciiRules = false }.GetAscii(text);
            text = text.Trim('.').ToLowerInvariant();
        }
        catch (ArgumentException)
        {
        }
        return text.Length == 0 ? null : text;
    }

    static Boolean TryLabels(String normalized, out String[] labels)
    {
        labels = null;
        if (String.IsNullOrEmpty(normalized) || normalized.Length > 253) return false;
        labels = normalized.Split('.');
        if (labels.Length == 0) return false;
        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > 63) return false;
            foreach (var ch in label)
            {
                if (Char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') continue;
                return false;
            }
        }
        return true;
    }

    sealed class Node
    {
        public Dictionary<String, Node> Children;
        public Candidate Suffix;
        public Candidate Wildcard;
        public Candidate Exact;
    }

    sealed class Candidate
    {
        public DnsDomainGroup Group { get; }
        public Int32 Order { get; }
        public Int32 Depth { get; }

        public Candidate(DnsDomainGroup group, Int32 order, Int32 depth)
        {
            Group = group;
            Order = order;
            Depth = depth;
        }
    }
}

/// <summary>域名组的匹配方式。</summary>
public enum DomainMatchKind
{
    /// <summary>匹配该域名及其子域。</summary>
    Suffix,

    /// <summary>只匹配子域，不匹配域名本身。</summary>
    Wildcard,

    /// <summary>只匹配完整域名。</summary>
    Exact,

    /// <summary>匹配任意查询名。</summary>
    Any
}

/// <summary>一条域名模式。</summary>
public sealed class DnsDomainPattern
{
    /// <summary>规范化后的写法，例如 example.com、*.example.com、exact:example.com。</summary>
    public String Text { get; }

    /// <summary>匹配方式。</summary>
    public DomainMatchKind Kind { get; }

    /// <summary>参与长短比较的域名后缀，不含模式前缀。</summary>
    public String Suffix { get; }

    /// <summary>后缀的标签数。越大越具体，<see cref="DomainMatchKind.Any"/> 为 0。</summary>
    public Int32 LabelCount { get; }

    internal DnsDomainPattern(String text, DomainMatchKind kind, String suffix, Int32 labelCount)
    {
        Text = text;
        Kind = kind;
        Suffix = suffix;
        LabelCount = labelCount;
    }

    /// <summary>已重载。</summary>
    public override String ToString() => Text;
}

/// <summary>一个域名组：若干域名模式共用一组上级 DNS。</summary>
public sealed class DnsDomainGroup
{
    /// <summary>配置中等号左侧的原文。</summary>
    public String Domains { get; }

    /// <summary>配置中的上级原文。</summary>
    public String Upstream { get; }

    /// <summary>本组的域名模式，顺序与配置一致。</summary>
    public IReadOnlyList<DnsDomainPattern> Patterns { get; }

    /// <summary>上级服务器。</summary>
    public IReadOnlyList<NetUri> Servers { get; }

    internal DnsDomainGroup(String domains, String upstream, IReadOnlyList<DnsDomainPattern> patterns, IReadOnlyList<NetUri> servers)
    {
        Domains = domains;
        Upstream = upstream;
        Patterns = patterns;
        Servers = servers;
    }

    /// <summary>已重载。</summary>
    public override String ToString() => Domains + "=" + Upstream;
}
