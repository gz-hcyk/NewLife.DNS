using System;
using System.ComponentModel;
using NewLife.Xml;

namespace NewLife.DNS.Server
{
    [XmlConfigFile("Config\\DNS.config", 15000)]
    public class Setting : XmlConfig<Setting>
    {
        /// <summary>调试，默认true</summary>
        [Description("调试，默认true")]
        public Boolean Debug { get; set; } = true;

        /// <summary>上级DNS服务器</summary>
        [Description("上级DNS服务器")]
        public String DNSServer { get; set; } = "udp://223.5.5.5,tcp://8.8.8.8,udp://223.4.4.4";

        /// <summary>
        /// 按客户端来源 IP/网段选择上级 DNS。未命中时仍使用 <see cref="DNSServer"/>。
        /// 多条用分号或换行分隔，格式：来源=上级列表。来源支持单个 IP、CIDR（10.0.0.0/8）或起止范围（192.168.1.1-192.168.1.200）。
        /// 上级列表写法与 <see cref="DNSServer"/> 相同。同时命中时，前缀更长的规则优先。
        /// </summary>
        [Description("按来源IP/网段选择上级DNS。格式：来源=上级列表，多条用分号分隔。例如 10.0.0.0/8=udp://1.1.1.1,udp://1.0.0.1;192.168.1.10=udp://8.8.8.8。未命中时使用DNSServer")]
        public String DNSRoutes { get; set; } = "";
    }
}