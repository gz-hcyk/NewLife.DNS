using System.Net;
using System.Net.Sockets;
using NewLife.DNS.Server;
using NewLife.Net;
using Xunit;

namespace NewLife.DNSServer.Tests;

public class DnsRouteTableTests
{
    [Fact]
    public void EmptyConfig_MatchesNothing()
    {
        var table = DnsRouteTable.Parse(null!);
        Assert.Empty(table.Rules);
        Assert.Empty(table.Warnings);
        Assert.Null(table.Match(IPAddress.Parse("10.1.1.1")));

        table = DnsRouteTable.Parse("  \n  ");
        Assert.Empty(table.Rules);
    }

    [Fact]
    public void SingleAddressAndCidr_MatchExpectedUpstream()
    {
        var table = DnsRouteTable.Parse("10.0.0.0/8=udp://1.1.1.1,udp://1.0.0.1;192.168.1.10=udp://8.8.8.8");

        var wide = table.Match(IPAddress.Parse("10.2.3.4"));
        Assert.NotNull(wide);
        Assert.Equal("10.0.0.0/8", wide.Source);
        Assert.Equal(2, wide.Servers.Count);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), wide.Servers[0].Address);
        Assert.Equal(53, wide.Servers[0].Port);
        Assert.Equal(NetType.Udp, wide.Servers[0].Type);

        var exact = table.Match(IPAddress.Parse("192.168.1.10"));
        Assert.Equal("192.168.1.10", exact!.Source);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), exact.Servers[0].Address);

        Assert.Null(table.Match(IPAddress.Parse("11.0.0.1")));
    }

    [Fact]
    public void LongerPrefixWins_SameLengthKeepsEarlierRule()
    {
        var table = DnsRouteTable.Parse("10.0.0.0/8=udp://1.1.1.1;10.1.0.0/16=udp://8.8.8.8;10.0.0.0/8=udp://9.9.9.9");

        Assert.Equal("10.1.0.0/16", table.Match(IPAddress.Parse("10.1.2.3"))!.Source);
        Assert.Equal("10.0.0.0/8", table.Match(IPAddress.Parse("10.2.2.3"))!.Source);
    }

    [Fact]
    public void Range_IsInclusive()
    {
        var table = DnsRouteTable.Parse("192.168.1.20-192.168.1.10=udp://1.1.1.1");
        Assert.NotNull(table.Match(IPAddress.Parse("192.168.1.10")));
        Assert.NotNull(table.Match(IPAddress.Parse("192.168.1.15")));
        Assert.NotNull(table.Match(IPAddress.Parse("192.168.1.20")));
        Assert.Null(table.Match(IPAddress.Parse("192.168.1.21")));
    }

    [Fact]
    public void IPv6Cidr_AndMappedIPv4()
    {
        var table = DnsRouteTable.Parse("2001:db8::/32=udp://[2001:4860:4860::8888];10.0.0.0/8=udp://1.1.1.1");

        Assert.Equal("2001:db8::/32", table.Match(IPAddress.Parse("2001:db8::1"))!.Source);
        Assert.Null(table.Match(IPAddress.Parse("2001:db9::1")));

        var mapped = IPAddress.Parse("::ffff:10.1.2.3");
        Assert.Equal(AddressFamily.InterNetworkV6, mapped.AddressFamily);
        Assert.Equal("10.0.0.0/8", table.Match(mapped)!.Source);
    }

    [Fact]
    public void DottedMask_AndDefaultPort()
    {
        var table = DnsRouteTable.Parse("192.168.0.0/255.255.255.0=223.5.5.5");
        var route = table.Match(IPAddress.Parse("192.168.0.8"));
        Assert.NotNull(route);
        Assert.Equal(24, route.PrefixLength);
        Assert.Equal(53, route.Servers[0].Port);
        Assert.Null(table.Match(IPAddress.Parse("192.168.1.8")));
    }

    [Fact]
    public void InvalidSegment_IsSkipped()
    {
        var table = DnsRouteTable.Parse("nope=udp://1.1.1.1;10.0.0.1=udp://8.8.8.8;# comment");
        Assert.Single(table.Rules);
        Assert.NotEmpty(table.Warnings);
        Assert.Equal("10.0.0.1", table.Match(IPAddress.Parse("10.0.0.1"))!.Source);
    }

    [Fact]
    public void NewlineSeparator_Works()
    {
        var table = DnsRouteTable.Parse("10.0.0.0/8=udp://1.1.1.1\n172.16.0.0/12=tcp://8.8.8.8");
        Assert.Equal(2, table.Rules.Count);
        Assert.Equal(NetType.Tcp, table.Rules[1].Servers[0].Type);
    }
}
