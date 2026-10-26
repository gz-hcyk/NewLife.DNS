#nullable disable
using System.Text;

namespace NewLife.DNS.Server;

/// <summary>把来源路由和域名组编辑成 <see cref="Setting"/> 能保存的文本。</summary>
/// <remarks>
/// 生成的文本交给 <see cref="DnsRouteTable"/> 和 <see cref="DnsDomainGroupTable"/> 解析。
/// 域名文件只允许写在配置根目录之内的相对路径。
/// </remarks>
public static class DnsForwardConfig
{
    const Int32 MaxFileChars = 512 * 1024;

    /// <summary>从当前配置读出可编辑的草稿。</summary>
    /// <param name="dnsServer">默认上级原文</param>
    /// <param name="routes">来源路由原文</param>
    /// <param name="groups">域名组原文</param>
    /// <param name="baseDirectory">查找域名文件的目录</param>
    /// <returns>编辑草稿。无效片段记在 <see cref="DnsForwardDraft.Warnings"/></returns>
    public static DnsForwardDraft Load(String dnsServer, String routes, String groups, String baseDirectory)
    {
        var draft = new DnsForwardDraft { DNSServer = dnsServer ?? "" };
        var routeTable = DnsRouteTable.Parse(routes);
        foreach (var warning in routeTable.Warnings)
            draft.Warnings.Add(warning);
        foreach (var rule in routeTable.Rules)
            draft.Routes.Add(new DnsRouteDraft { Source = rule.Source, Servers = rule.Upstream });

        var groupTable = DnsDomainGroupTable.Parse(groups, baseDirectory);
        foreach (var warning in groupTable.Warnings)
            draft.Warnings.Add(warning);
        foreach (var group in groupTable.Groups)
        {
            var item = new DnsGroupDraft { Servers = group.Upstream };
            var tokens = (group.Domains ?? "").Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in tokens)
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                item.Patterns.Add(ReadPattern(token, baseDirectory));
            }
            if (item.Patterns.Count == 0)
            {
                foreach (var pattern in group.Patterns)
                    item.Patterns.Add(FromPattern(pattern));
            }
            draft.Groups.Add(item);
        }
        return draft;
    }

    /// <summary>校验草稿并生成配置文本。通过时才把域名文件写到 <paramref name="baseDirectory"/>。</summary>
    /// <param name="draft">页面提交的草稿</param>
    /// <param name="baseDirectory">域名文件相对的根目录</param>
    /// <param name="dnsServer">可写入 <see cref="Setting.DNSServer"/> 的文本</param>
    /// <param name="routes">可写入 <see cref="Setting.DNSRoutes"/> 的文本</param>
    /// <param name="groups">可写入 <see cref="Setting.DomainGroups"/> 的文本</param>
    /// <param name="errors">校验失败原因。成功时为空</param>
    /// <returns>可以保存时为 true</returns>
    public static Boolean TryBuild(DnsForwardDraft draft, String baseDirectory, out String dnsServer, out String routes, out String groups, out IReadOnlyList<String> errors)
    {
        dnsServer = NormalizeServers(draft?.DNSServer);
        routes = "";
        groups = "";
        var list = new List<String>();
        if (dnsServer.Length > 0)
        {
            DnsRouteTable.ParseServers(dnsServer, out var serverError);
            if (serverError != null) list.Add("默认上级：" + serverError);
        }

        var routeLines = new List<String>();
        var index = 0;
        foreach (var route in draft?.Routes ?? new List<DnsRouteDraft>())
        {
            index++;
            var source = (route?.Source ?? "").Trim();
            var servers = NormalizeServers(route?.Servers);
            if (source.Length == 0 && servers.Length == 0) continue;
            if (source.Length == 0 || servers.Length == 0)
            {
                list.Add("来源路由第 " + index + " 行需要同时填写来源和上级");
                continue;
            }
            routeLines.Add(source + "=" + servers);
        }
        routes = String.Join("\n", routeLines);
        var routeTable = DnsRouteTable.Parse(routes);
        foreach (var warning in routeTable.Warnings)
            list.Add(warning);
        if (routeLines.Count != routeTable.Rules.Count && routeTable.Warnings.Count == 0 && routeLines.Count > 0)
            list.Add("有来源路由未能识别");

        var groupLines = new List<String>();
        var files = new List<PendingFile>();
        var groupIndex = 0;
        foreach (var group in draft?.Groups ?? new List<DnsGroupDraft>())
        {
            groupIndex++;
            var servers = NormalizeServers(group?.Servers);
            var patterns = (group?.Patterns ?? new List<DnsPatternDraft>()).Where(e => e != null && !IsBlank(e)).ToList();
            if (servers.Length == 0 && patterns.Count == 0) continue;
            if (servers.Length == 0 || patterns.Count == 0)
            {
                list.Add("域名组第 " + groupIndex + " 组需要同时填写域名和上级");
                continue;
            }
            DnsRouteTable.ParseServers(servers, out var serverError);
            if (serverError != null) list.Add("域名组第 " + groupIndex + " 组：" + serverError);

            var tokens = new List<String>();
            var patternIndex = 0;
            foreach (var pattern in patterns)
            {
                patternIndex++;
                if (!TryToken(pattern, groupIndex, patternIndex, baseDirectory, tokens, files, list))
                    continue;
            }
            if (tokens.Count > 0)
                groupLines.Add(String.Join(",", tokens) + "=" + servers);
        }

        if (list.Count > 0)
        {
            errors = list;
            return false;
        }

        var root = String.IsNullOrWhiteSpace(baseDirectory) ? AppDomain.CurrentDomain.BaseDirectory : baseDirectory;
        var temp = Path.Combine(Path.GetTempPath(), "dns-forward-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in files)
            {
                var tempFile = Path.GetFullPath(Path.Combine(temp, file.Relative));
                Directory.CreateDirectory(Path.GetDirectoryName(tempFile));
                File.WriteAllText(tempFile, file.Body ?? "", new UTF8Encoding(false));
            }
            groups = String.Join("\n", groupLines);
            var groupTable = DnsDomainGroupTable.Parse(groups, temp);
            foreach (var warning in groupTable.Warnings)
                list.Add(warning);
            if (list.Count > 0)
            {
                errors = list;
                return false;
            }

            foreach (var file in files)
            {
                var target = Path.GetFullPath(Path.Combine(root, file.Relative));
                if (!IsUnder(root, target))
                {
                    errors = new[] { "域名文件必须位于配置目录之内：" + file.Relative };
                    return false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllText(target, file.Body ?? "", new UTF8Encoding(false));
            }
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }

        errors = list;
        return true;
    }

    static Boolean TryToken(DnsPatternDraft pattern, Int32 groupIndex, Int32 patternIndex, String baseDirectory, List<String> tokens, List<PendingFile> files, List<String> errors)
    {
        var kind = (pattern.Kind ?? "Suffix").Trim();
        var name = (pattern.Name ?? "").Trim().Trim('"');
        var where = "域名组第 " + groupIndex + " 组第 " + patternIndex + " 条";
        if (kind.Equals("Any", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add("*");
            return true;
        }
        if (kind.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            if (name.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) name = name.Substring(5).Trim();
            else if (name.Length > 0 && name[0] == '@') name = name.Substring(1).Trim();
            if (!TryRelative(name, out var error))
            {
                errors.Add(where + "：" + error);
                return false;
            }
            var body = pattern.FileBody ?? "";
            if (body.Length > MaxFileChars)
            {
                errors.Add(where + "：域名文件超过 512KB，请直接编辑文件");
                return false;
            }
            if (body.Trim().Length == 0)
            {
                var existing = DnsDomainGroupTable.LocateDomainFile(name, baseDirectory);
                if (existing == null)
                {
                    errors.Add(where + "：域名文件不存在，请填写内容或改成页面内的域名");
                    return false;
                }
                body = File.ReadAllText(existing);
            }
            if (!ValidateFileBody(body, where, errors)) return false;
            files.Add(new PendingFile { Relative = name.Replace('\\', '/'), Body = NormalizeNewlines(body) });
            tokens.Add("file:" + name.Replace('\\', '/'));
            return true;
        }

        name = StripKnownPrefix(name);
        if (name.Length == 0)
        {
            errors.Add(where + "：请填写域名");
            return false;
        }
        var token = kind.Equals("Exact", StringComparison.OrdinalIgnoreCase) ? "exact:" + name
            : kind.Equals("Wildcard", StringComparison.OrdinalIgnoreCase) ? "*." + name
            : name;
        var probe = DnsDomainGroupTable.Parse(token + "=udp://127.0.0.1");
        if (probe.Warnings.Count > 0 || probe.Groups.Count != 1)
        {
            errors.Add(where + "：" + (probe.Warnings.FirstOrDefault() ?? "域名无效"));
            return false;
        }
        tokens.Add(probe.Groups[0].Patterns[0].Text);
        return true;
    }

    static Boolean ValidateFileBody(String body, String where, List<String> errors)
    {
        var lines = new List<String>();
        foreach (var raw in (body ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("//")) continue;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash).Trim();
            if (line.Length == 0) continue;
            lines.Add(line + "=udp://127.0.0.1");
        }
        if (lines.Count == 0)
        {
            errors.Add(where + "：域名文件没有可用域名");
            return false;
        }
        var table = DnsDomainGroupTable.Parse(String.Join("\n", lines));
        if (table.Warnings.Count > 0)
        {
            foreach (var warning in table.Warnings)
                errors.Add(where + "：" + warning);
            return false;
        }
        return true;
    }

    static DnsPatternDraft ReadPattern(String token, String baseDirectory)
    {
        if (token.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || (token.Length > 1 && token[0] == '@'))
        {
            var path = token[0] == '@' ? token.Substring(1).Trim() : token.Substring(token.IndexOf(':') + 1).Trim().Trim('"');
            var body = "";
            var full = DnsDomainGroupTable.LocateDomainFile(path, baseDirectory);
            if (full != null)
            {
                try
                {
                    var info = new FileInfo(full);
                    body = info.Length > MaxFileChars ? "" : File.ReadAllText(full);
                }
                catch { }
            }
            return new DnsPatternDraft { Kind = "File", Name = path, FileBody = body };
        }
        if (token == "*") return new DnsPatternDraft { Kind = "Any", Name = "*" };
        if (token.StartsWith("exact:", StringComparison.OrdinalIgnoreCase))
            return new DnsPatternDraft { Kind = "Exact", Name = token.Substring(6).Trim() };
        if (token.StartsWith("*.", StringComparison.Ordinal))
            return new DnsPatternDraft { Kind = "Wildcard", Name = token.Substring(2).Trim() };
        if (token.StartsWith("suffix:", StringComparison.OrdinalIgnoreCase))
            return new DnsPatternDraft { Kind = "Suffix", Name = token.Substring(7).Trim() };
        return new DnsPatternDraft { Kind = "Suffix", Name = token };
    }

    static DnsPatternDraft FromPattern(DnsDomainPattern pattern)
    {
        var kind = pattern.Kind switch
        {
            DomainMatchKind.Exact => "Exact",
            DomainMatchKind.Wildcard => "Wildcard",
            DomainMatchKind.Any => "Any",
            _ => "Suffix"
        };
        return new DnsPatternDraft { Kind = kind, Name = pattern.Kind == DomainMatchKind.Any ? "*" : pattern.Suffix };
    }

    /// <summary>去掉页面上可能重复填写的前缀，匹配方式以所选类型为准。</summary>
    static String StripKnownPrefix(String name)
    {
        name = (name ?? "").Trim();
        if (name.StartsWith("exact:", StringComparison.OrdinalIgnoreCase)) name = name.Substring(6).Trim();
        else if (name.StartsWith("suffix:", StringComparison.OrdinalIgnoreCase)) name = name.Substring(7).Trim();
        else if (name.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) name = name.Substring(5).Trim();
        else if (name.StartsWith("*.", StringComparison.Ordinal)) name = name.Substring(2).Trim();
        return name.Trim().Trim('.');
    }

    static String NormalizeServers(String text) => (text ?? "").Trim().Replace('，', ',');

    static Boolean IsBlank(DnsPatternDraft pattern)
    {
        var kind = pattern.Kind ?? "";
        if (kind.Equals("Any", StringComparison.OrdinalIgnoreCase)) return false;
        return String.IsNullOrWhiteSpace(pattern.Name) && String.IsNullOrWhiteSpace(pattern.FileBody);
    }

    static Boolean TryRelative(String path, out String error)
    {
        error = null;
        if (String.IsNullOrWhiteSpace(path))
        {
            error = "请填写域名文件的相对路径";
            return false;
        }
        if (Path.IsPathRooted(path) || path.IndexOf(':') >= 0)
        {
            error = "域名文件只允许相对路径";
            return false;
        }
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Any(e => e == ".."))
        {
            error = "域名文件路径不能包含 ..";
            return false;
        }
        return true;
    }

    static Boolean IsUnder(String root, String full)
    {
        var baseFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fileFull = Path.GetFullPath(full);
        return fileFull.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase);
    }

    static String NormalizeNewlines(String text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    sealed class PendingFile
    {
        public String Relative;
        public String Body;
    }
}

/// <summary>转发设置草稿，字段名与页面表单一致。</summary>
public sealed class DnsForwardDraft
{
    /// <summary>默认上级，写法与 <see cref="Setting.DNSServer"/> 相同。</summary>
    public String DNSServer { get; set; } = "";

    /// <summary>来源路由。</summary>
    public List<DnsRouteDraft> Routes { get; set; } = new();

    /// <summary>域名组。</summary>
    public List<DnsGroupDraft> Groups { get; set; } = new();

    /// <summary>读取配置时跳过的片段。</summary>
    public List<String> Warnings { get; set; } = new();
}

/// <summary>一条来源路由。</summary>
public sealed class DnsRouteDraft
{
    /// <summary>来源 IP、CIDR 或起止范围。</summary>
    public String Source { get; set; } = "";

    /// <summary>上级列表，逗号分隔。</summary>
    public String Servers { get; set; } = "";
}

/// <summary>一个域名组。</summary>
public sealed class DnsGroupDraft
{
    /// <summary>上级列表，逗号分隔。</summary>
    public String Servers { get; set; } = "";

    /// <summary>域名或文件引用。</summary>
    public List<DnsPatternDraft> Patterns { get; set; } = new();
}

/// <summary>域名组里的一条匹配。</summary>
public sealed class DnsPatternDraft
{
    /// <summary>Suffix、Wildcard、Exact、Any 或 File。</summary>
    public String Kind { get; set; } = "Suffix";

    /// <summary>域名，或 file 的相对路径。</summary>
    public String Name { get; set; } = "";

    /// <summary>Kind 为 File 时的文件内容，每行一个域名。</summary>
    public String FileBody { get; set; } = "";
}
