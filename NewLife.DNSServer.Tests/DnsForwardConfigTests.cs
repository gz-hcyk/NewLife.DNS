using System.Net;
using NewLife.DNS.Server;
using Xunit;

namespace NewLife.DNSServer.Tests;

public class DnsForwardConfigTests
{
    [Fact(DisplayName = "来源路由经页面草稿往返后仍然匹配")]
    public void Routes_RoundTrip_KeepsLongestPrefix()
    {
        var source = "10.0.0.0/8=udp://1.1.1.1,udp://1.0.0.1\n192.168.1.10=udp://8.8.8.8\n192.168.1.1-192.168.1.200=udp://9.9.9.9";
        var draft = DnsForwardConfig.Load("", source, "", Path.GetTempPath());
        Assert.Equal(3, draft.Routes.Count);
        Assert.Empty(draft.Warnings);

        var root = TempRoot();
        try
        {
            Assert.True(DnsForwardConfig.TryBuild(draft, root, out var dns, out var routes, out var groups, out var errors), String.Join(";", errors));
            Assert.Equal("", dns);
            Assert.Equal("", groups);

            var table = DnsRouteTable.Parse(routes);
            Assert.Empty(table.Warnings);
            Assert.Equal("10.0.0.0/8", table.Match(IPAddress.Parse("10.2.3.4"))!.Source);
            Assert.Equal(2, table.Match(IPAddress.Parse("10.2.3.4"))!.Servers.Count);
            Assert.Equal("192.168.1.10", table.Match(IPAddress.Parse("192.168.1.10"))!.Source);
            Assert.Equal("192.168.1.1-192.168.1.200", table.Match(IPAddress.Parse("192.168.1.15"))!.Source);
            Assert.Null(table.Match(IPAddress.Parse("11.0.0.1")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact(DisplayName = "域名组往返后仍按更长后缀优先，并忽略大小写")]
    public void Groups_RoundTrip_KeepsSpecificityAndCase()
    {
        var text = "Example.COM.=udp://1.1.1.1;*.example.com=udp://2.2.2.2;exact:vpn.example.com=udp://3.3.3.3;*=udp://4.4.4.4";
        var draft = DnsForwardConfig.Load("udp://223.5.5.5，udp://223.4.4.4", "", text, Path.GetTempPath());
        Assert.Equal(4, draft.Groups.Count);
        Assert.Equal("Suffix", draft.Groups[0].Patterns[0].Kind);
        Assert.Equal("Example.COM.", draft.Groups[0].Patterns[0].Name);
        Assert.Equal("Wildcard", draft.Groups[1].Patterns[0].Kind);
        Assert.Equal("Exact", draft.Groups[2].Patterns[0].Kind);
        Assert.Equal("Any", draft.Groups[3].Patterns[0].Kind);

        var root = TempRoot();
        try
        {
            Assert.True(DnsForwardConfig.TryBuild(draft, root, out var dns, out _, out var groups, out var errors), String.Join(";", errors));
            Assert.Equal("udp://223.5.5.5,udp://223.4.4.4", dns);

            var table = DnsDomainGroupTable.Parse(groups);
            Assert.Empty(table.Warnings);
            Assert.Equal("udp://3.3.3.3", table.Match("VPN.Example.COM.")!.Upstream);
            // 后缀与 *. 标签数相同，配置靠前者优先，所以先写的 example.com 覆盖子域。
            Assert.Equal("udp://1.1.1.1", table.Match("www.example.com")!.Upstream);
            Assert.Equal("udp://1.1.1.1", table.Match("example.com")!.Upstream);
            Assert.Equal("udp://4.4.4.4", table.Match("other.net")!.Upstream);

            var longer = new DnsForwardDraft
            {
                Groups =
                {
                    new DnsGroupDraft
                    {
                        Servers = "udp://1.1.1.1",
                        Patterns = { new DnsPatternDraft { Kind = "Suffix", Name = "example.com" } }
                    },
                    new DnsGroupDraft
                    {
                        Servers = "udp://2.2.2.2",
                        Patterns = { new DnsPatternDraft { Kind = "Wildcard", Name = "example.com" } }
                    },
                    new DnsGroupDraft
                    {
                        Servers = "udp://8.8.8.8",
                        Patterns = { new DnsPatternDraft { Kind = "Suffix", Name = "www.example.com" } }
                    }
                }
            };
            Assert.True(DnsForwardConfig.TryBuild(longer, root, out _, out _, out var longerText, out var longerErrors), String.Join(";", longerErrors));
            var longerTable = DnsDomainGroupTable.Parse(longerText);
            Assert.Equal("udp://8.8.8.8", longerTable.Match("www.example.com")!.Upstream);
            Assert.Equal("udp://1.1.1.1", longerTable.Match("example.com")!.Upstream);
            Assert.Equal("udp://1.1.1.1", longerTable.Match("api.example.com")!.Upstream);
            Assert.Null(DnsDomainGroupTable.Parse("*.example.com=udp://2.2.2.2").Match("example.com"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact(DisplayName = "file: 可内联保存，再次读取仍是文件引用")]
    public void FilePattern_WritesBody_AndLoadsBack()
    {
        var root = TempRoot();
        try
        {
            var draft = new DnsForwardDraft
            {
                DNSServer = "udp://223.5.5.5",
                Groups =
                {
                    new DnsGroupDraft
                    {
                        Servers = "udp://127.0.0.1",
                        Patterns =
                        {
                            new DnsPatternDraft
                            {
                                Kind = "File",
                                Name = "Config/dns-groups/ads.txt",
                                FileBody = "# comment\nads.example\n*.track.test\nexact:only.test\n"
                            }
                        }
                    }
                }
            };

            Assert.True(DnsForwardConfig.TryBuild(draft, root, out _, out _, out var groups, out var errors), String.Join(";", errors));
            Assert.Contains("file:Config/dns-groups/ads.txt", groups);
            Assert.True(File.Exists(Path.Combine(root, "Config", "dns-groups", "ads.txt")));

            var table = DnsDomainGroupTable.Parse(groups, root);
            Assert.Empty(table.Warnings);
            Assert.Equal("udp://127.0.0.1", table.Match("a.ads.example")!.Upstream);
            Assert.Equal("udp://127.0.0.1", table.Match("x.track.test")!.Upstream);
            Assert.Null(table.Match("track.test"));
            Assert.Equal("udp://127.0.0.1", table.Match("only.test")!.Upstream);
            Assert.Null(table.Match("www.only.test"));

            var again = DnsForwardConfig.Load("", "", groups, root);
            Assert.Equal("File", again.Groups[0].Patterns[0].Kind);
            Assert.Equal("Config/dns-groups/ads.txt", again.Groups[0].Patterns[0].Name);
            Assert.Contains("ads.example", again.Groups[0].Patterns[0].FileBody);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact(DisplayName = "无效来源、越级路径和空文件不会写入")]
    public void InvalidDraft_IsRejected()
    {
        var root = TempRoot();
        try
        {
            var badRoute = new DnsForwardDraft
            {
                Routes = { new DnsRouteDraft { Source = "not-an-ip", Servers = "udp://1.1.1.1" } }
            };
            Assert.False(DnsForwardConfig.TryBuild(badRoute, root, out _, out _, out _, out var routeErrors));
            Assert.Contains(routeErrors, e => e.Contains("无效"));

            var escape = new DnsForwardDraft
            {
                Groups =
                {
                    new DnsGroupDraft
                    {
                        Servers = "udp://1.1.1.1",
                        Patterns = { new DnsPatternDraft { Kind = "File", Name = "../secret.txt", FileBody = "example.com" } }
                    }
                }
            };
            Assert.False(DnsForwardConfig.TryBuild(escape, root, out _, out _, out _, out var pathErrors));
            Assert.Contains(pathErrors, e => e.Contains(".."));
            Assert.False(File.Exists(Path.Combine(Directory.GetParent(root)!.FullName, "secret.txt")));

            var emptyFile = new DnsForwardDraft
            {
                Groups =
                {
                    new DnsGroupDraft
                    {
                        Servers = "udp://1.1.1.1",
                        Patterns = { new DnsPatternDraft { Kind = "File", Name = "Config/missing.txt", FileBody = "   " } }
                    }
                }
            };
            Assert.False(DnsForwardConfig.TryBuild(emptyFile, root, out _, out _, out _, out var fileErrors));
            Assert.Contains(fileErrors, e => e.Contains("不存在"));
            Assert.False(File.Exists(Path.Combine(root, "Config", "missing.txt")));

            var junk = new DnsForwardDraft
            {
                Groups =
                {
                    new DnsGroupDraft
                    {
                        Servers = "udp://1.1.1.1",
                        Patterns = { new DnsPatternDraft { Kind = "File", Name = "Config/junk.txt", FileBody = "not a domain" } }
                    }
                }
            };
            Assert.False(DnsForwardConfig.TryBuild(junk, root, out _, out _, out _, out _));
            Assert.False(File.Exists(Path.Combine(root, "Config", "junk.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact(DisplayName = "装载到转发器时替换来源路由、域名组和默认上级")]
    public void Apply_ReplacesRoutesGroupsAndParents()
    {
        var server = new RoutedDnsServer
        {
            Routes = DnsRouteTable.Parse("192.168.0.0/16=udp://4.4.4.4"),
            DomainGroups = DnsDomainGroupTable.Parse("old.com=udp://5.5.5.5")
        };

        DnsRuntimeBinder.Apply(server, "udp://9.9.9.9", "10.0.0.0/8=udp://1.1.1.1", "example.com=udp://8.8.8.8", out _, out _);

        Assert.Equal("10.0.0.0/8", server.Routes.Match(IPAddress.Parse("10.1.1.1"))!.Source);
        Assert.Null(server.Routes.Match(IPAddress.Parse("192.168.1.1")));
        Assert.Equal("udp://8.8.8.8", server.DomainGroups.Match("www.example.com")!.Upstream);
        Assert.Null(server.DomainGroups.Match("old.com"));
        Assert.Contains(server.Parents, item => item.Address.Equals(IPAddress.Parse("9.9.9.9")));
    }

    static String TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "dns-forward-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
