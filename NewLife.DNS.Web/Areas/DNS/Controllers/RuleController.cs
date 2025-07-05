using NewLife.Cube;
using NewLife.DNS.Entity;

namespace NewLife.DNS.Web.Controllers;

[DNSArea]
[Menu(80, true, Icon = "fa-star")]
public class RuleController : EntityController<Rule>
{
}