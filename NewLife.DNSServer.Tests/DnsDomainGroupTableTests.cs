using System.Net;
using NewLife.DNS.Server;
using Xunit;

namespace NewLife.DNSServer.Tests;

public class DnsDomainGroupTableTests
{
    [Fact(DisplayName = "空配置不匹配任何域名")]
    public void EmptyConfig_MatchesNothing()
    {
        var table = DnsDomainGroupTable.Parse(null!);
        Assert.Empty(table.Groups);
        Assert.Empty(table.Warnings);
        Assert.Null(table.Match("example.com"));

        table = DnsDomainGroupTable.Parse(" \n# comment\n ");
        Assert.Empty(table.Groups);
        Assert.Null(table.Match("example.com"));
        Assert.Null(DnsDomainGroupTable.Parse("example.com=udp://1.1.1.1").Match(null));
        Assert.Null(DnsDomainGroupTable.Parse("example.com=udp://1.1.1.1").Match("."));
    }

    [Fact(DisplayName = "后缀匹配自身和子域，不匹配相邻名字")]
    public void Suffix_MatchesSelfAndChildren_NotSiblings()
    {
        var table = DnsDomainGroupTable.Parse("example.com,example.net=udp://1.1.1.1,udp://1.0.0.1");

        var self = table.Match("example.com");
        Assert.NotNull(self);
        Assert.Equal("example.com,example.net", self!.Domains);
        Assert.Equal(2, self.Servers.Count);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), self.Servers[0].Address);
        Assert.Equal(53, self.Servers[0].Port);

        Assert.Equal(self.Upstream, table.Match("a.b.example.com")!.Upstream);
        Assert.Equal(self.Upstream, table.Match("www.example.net")!.Upstream);
        Assert.Null(table.Match("notexample.com"));
        Assert.Null(table.Match("example.com.evil.net"));
        Assert.Null(table.Match("example.org"));
    }

    [Theory(DisplayName = "忽略大小写和末尾点")]
    [InlineData("example.com")]
    [InlineData("Example.COM")]
    [InlineData("example.com.")]
    [InlineData("EXAMPLE.COM.")]
    [InlineData("WwW.Example.COM.")]
    public void CaseAndTrailingDot_Match(String name)
    {
        var table = DnsDomainGroupTable.Parse("Example.COM.=udp://8.8.8.8");
        Assert.Equal("udp://8.8.8.8", table.Match(name)!.Upstream);
    }

    [Fact(DisplayName = "更长的域名后缀优先，相同长度保留先配置的组")]
    public void LongerSuffixWins_SameLengthKeepsEarlierGroup()
    {
        var table = DnsDomainGroupTable.Parse("example.com=udp://1.1.1.1;www.example.com=udp://3.3.3.3;example.com=udp://9.9.9.9");

        Assert.Equal("udp://3.3.3.3", table.Match("www.example.com")!.Upstream);
        Assert.Equal("udp://3.3.3.3", table.Match("WWW.Example.COM.")!.Upstream);
        Assert.Equal("udp://1.1.1.1", table.Match("api.example.com")!.Upstream);
        Assert.Equal("udp://1.1.1.1", table.Match("example.com")!.Upstream);
    }

    [Fact(DisplayName = "通配只匹配子域，完整匹配不包含子域")]
    public void WildcardAndExact_HaveDistinctScope()
    {
        var table = DnsDomainGroupTable.Parse("*.example.com=udp://1.1.1.1;exact:vpn.example.com=udp://2.2.2.2;exact:example.com=udp://3.3.3.3");

        Assert.Equal("udp://1.1.1.1", table.Match("www.example.com")!.Upstream);
        Assert.Equal("udp://1.1.1.1", table.Match("a.b.example.com")!.Upstream);
        Assert.Equal("udp://2.2.2.2", table.Match("vpn.example.com")!.Upstream);
        Assert.Equal("udp://1.1.1.1", table.Match("a.vpn.example.com")!.Upstream);
        Assert.Equal("udp://3.3.3.3", table.Match("example.com")!.Upstream);
        Assert.Null(DnsDomainGroupTable.Parse("*.example.com=udp://1.1.1.1").Match("example.com"));
        Assert.Null(DnsDomainGroupTable.Parse("exact:example.com=udp://1.1.1.1").Match("www.example.com"));
    }

    [Fact(DisplayName = "通配与同级后缀按配置先后决胜，更长后缀仍优先")]
    public void SameLabelCount_UsesConfigOrder()
    {
        var earlierSuffix = DnsDomainGroupTable.Parse("example.com=udp://2.2.2.2;*.example.com=udp://1.1.1.1");
        Assert.Equal("udp://2.2.2.2", earlierSuffix.Match("www.example.com")!.Upstream);

        var earlierWildcard = DnsDomainGroupTable.Parse("*.example.com=udp://1.1.1.1;example.com=udp://2.2.2.2");
        Assert.Equal("udp://1.1.1.1", earlierWildcard.Match("www.example.com")!.Upstream);
        Assert.Equal("udp://2.2.2.2", earlierWildcard.Match("example.com")!.Upstream);

        var longer = DnsDomainGroupTable.Parse("*.example.com=udp://1.1.1.1;a.example.com=udp://4.4.4.4");
        Assert.Equal("udp://4.4.4.4", longer.Match("a.example.com")!.Upstream);
        Assert.Equal("udp://1.1.1.1", longer.Match("b.example.com")!.Upstream);
    }

    [Fact(DisplayName = "全匹配只兜底，具体域名组优先")]
    public void CatchAll_LosesToSpecificGroup()
    {
        var table = DnsDomainGroupTable.Parse("*=udp://1.1.1.1;example.com=udp://2.2.2.2");
        Assert.Equal("udp://2.2.2.2", table.Match("www.example.com")!.Upstream);
        Assert.Equal("udp://1.1.1.1", table.Match("other.net")!.Upstream);
    }

    [Fact(DisplayName = "外部文件按行载入，并参与最长后缀比较")]
    public void FileList_LoadsLines_AndCompetesBySuffix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dns-group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "ads.txt"), new[]
            {
                "# comment",
                "",
                "ads.example",
                "*.track.test",
                "exact:only.test  # pinned",
                "not a domain"
            });
            var table = DnsDomainGroupTable.Parse("file:ads.txt=udp://1.2.3.4;www.ads.example=udp://9.9.9.9", dir);

            Assert.Equal("udp://1.2.3.4", table.Match("a.ads.example")!.Upstream);
            Assert.Equal("udp://9.9.9.9", table.Match("www.ads.example")!.Upstream);
            Assert.Equal("udp://1.2.3.4", table.Match("x.track.test")!.Upstream);
            Assert.Null(table.Match("track.test"));
            Assert.Equal("udp://1.2.3.4", table.Match("only.test")!.Upstream);
            Assert.Null(table.Match("www.only.test"));
            Assert.Contains(table.Warnings, e => e.Contains("not a domain"));

            var shorthand = DnsDomainGroupTable.Parse("@ads.txt=udp://8.8.8.8", dir);
            Assert.Equal("udp://8.8.8.8", shorthand.Match("ads.example")!.Upstream);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact(DisplayName = "缺失文件和无效片段被跳过")]
    public void InvalidSegment_IsSkipped()
    {
        var table = DnsDomainGroupTable.Parse("file:missing-domains.txt=udp://1.1.1.1;bad domain=udp://2.2.2.2;ok.com=udp://8.8.8.8;# comment");
        Assert.Single(table.Groups);
        Assert.NotEmpty(table.Warnings);
        Assert.Equal("ok.com", table.Match("ok.com")!.Domains);
        Assert.Null(table.Match("missing-domains.txt"));
    }

    [Fact(DisplayName = "换行和中文分号都能分隔域名组")]
    public void NewlineAndFullwidthSeparator_Work()
    {
        var table = DnsDomainGroupTable.Parse("example.com=udp://1.1.1.1\nexample.net=tcp://8.8.8.8；example.org=udp://[2001:4860:4860::8888]");
        Assert.Equal(3, table.Groups.Count);
        Assert.Equal(NewLife.Net.NetType.Tcp, table.Groups[1].Servers[0].Type);
        Assert.Equal(53, table.Match("example.org")!.Servers[0].Port);
    }

    [Fact(DisplayName = "中文域名和下划线标签可以匹配")]
    public void IdnAndUnderscore_Match()
    {
        var table = DnsDomainGroupTable.Parse("中国.cn=udp://1.1.1.1;_acme.example.com=udp://2.2.2.2");
        Assert.Equal("udp://1.1.1.1", table.Match("www.中国.cn")!.Upstream);
        Assert.Equal("udp://1.1.1.1", table.Match("www.xn--fiqs8s.cn")!.Upstream);
        Assert.Equal("udp://2.2.2.2", table.Match("_acme.example.com")!.Upstream);
    }

    [Fact(DisplayName = "报文里的查询名可直接参与匹配")]
    public void QuestionName_FromPacket_MatchesIgnoringCase()
    {
        var name = DnsMessage.TryGetQuestionName(DnsPackets.Query("WwW.Example.COM"));
        Assert.Equal("WwW.Example.COM", name);
        Assert.Null(DnsMessage.TryGetQuestionName(new Byte[11]));

        var table = DnsDomainGroupTable.Parse("example.com=udp://1.1.1.1");
        Assert.Equal("udp://1.1.1.1", table.Match(name)!.Upstream);
    }

    [Fact(DisplayName = "域名组或来源路由命中时隔离全局缓存")]
    public void IsolatesCache_WhenGroupOrSourceRouteMatches()
    {
        using var server = new RoutedDnsServer
        {
            DomainGroups = DnsDomainGroupTable.Parse("example.com=udp://1.1.1.1"),
            Routes = DnsRouteTable.Parse("10.0.0.0/8=udp://8.8.8.8")
        };

        Assert.True(server.IsolatesCache("www.example.com", IPAddress.Parse("11.0.0.1")));
        Assert.True(server.IsolatesCache("Example.COM.", null));
        Assert.True(server.IsolatesCache("other.com", IPAddress.Parse("10.1.1.1")));
        Assert.False(server.IsolatesCache("other.com", IPAddress.Parse("11.0.0.1")));
        Assert.False(server.IsolatesCache("other.com", null));
    }
}
