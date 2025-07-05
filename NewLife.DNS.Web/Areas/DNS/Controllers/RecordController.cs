using NewLife.Cube;
using NewLife.DNS.Entity;

namespace NewLife.DNS.Web.Controllers;

[DNSArea]
[Menu(70, true, Icon = "fa-star")]
public class RecordController : EntityController<Record>
{
}