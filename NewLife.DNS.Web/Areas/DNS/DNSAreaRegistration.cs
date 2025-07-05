using NewLife.Cube;
using System.ComponentModel;

namespace NewLife.DNS.Web;

[DisplayName("DNS服务器")]
public class DNSArea : AreaBase
{
    public DNSArea() : base(nameof(DNSArea).TrimEnd("Area")) { }
}