# NewLife.DNS
新生命DNS代理服务器，你懂的！

## 项目源码位置
国内 https://git.NewLifeX.com/NewLife/NewLife.DNS  
国外 https://github.com/NewLifeX/NewLife.DNS  

## 按来源 IP 选择上级 DNS

默认上级仍是 `Config/DNS.config` 里的 `DNSServer`。需要按客户端来源转发时，增加 `DNSRoutes`（写法与 `DNSServer` 相同，多条规则用分号或换行分隔）：

```xml
<DNSServer>udp://223.5.5.5,udp://223.4.4.4</DNSServer>
<DNSRoutes>10.0.0.0/8=udp://1.1.1.1,udp://1.0.0.1;192.168.1.10=udp://8.8.8.8;192.168.1.1-192.168.1.200=udp://9.9.9.9</DNSRoutes>
```

- 来源可以是单个 IP、CIDR（如 `192.168.0.0/16`、`2001:db8::/32`）或起止范围（如 `192.168.1.1-192.168.1.200`）。
- 等号右侧是该来源使用的上级，多个用逗号分隔，例如 `udp://1.1.1.1,tcp://1.0.0.1`。
- 同时命中多条时，前缀更长的规则优先；长度相同则配置里靠前的规则优先。
- 没有规则命中时，仍走原来的 `DNSServer`。完整优先级见下一节。

## 按域名组选择上级 DNS

`DomainGroups` 与 `DNSRoutes`、`DNSServer` 写在同一份 `Config/DNS.config`，转发仍走原来的原始报文通道。一组域名共用一组上级，上级写法与 `DNSServer` 相同（逗号分隔多个）。

```xml
<DNSServer>udp://223.5.5.5,udp://223.4.4.4</DNSServer>
<DNSRoutes>10.0.0.0/8=udp://1.1.1.1,udp://1.0.0.1</DNSRoutes>
<DomainGroups>
example.com,example.net=udp://8.8.8.8,udp://8.8.4.4
*.corp.local=udp://10.0.0.53
exact:vpn.example.com=udp://10.1.1.1
file:Config/dns-groups/ads.txt=udp://127.0.0.1
</DomainGroups>
```

- `example.com` 匹配自身以及任意子域（如 `www.example.com`），不匹配 `notexample.com`，也不匹配只是字符串碰巧接在后面的名字。
- `*.example.com` 只匹配子域，不匹配 `example.com` 本身。子域不限一层，`a.b.example.com` 同样命中。
- `exact:example.com` 只匹配这个完整域名。`suffix:example.com` 与不写前缀相同。
- 单独一个 `*` 匹配其余所有查询名，具体程度最低。
- 比较时忽略大小写，也忽略末尾的点。`Example.COM.` 与 `example.com` 是同一条。
- 同时命中多个组时，匹配到的域名后缀更长（标签更多）者优先；标签数相同则配置里靠前的组优先。
- 域名很多时，用 `file:路径` 或 `@路径` 引用外部文件，每行一个域名，可用上面的三种写法，`#` 开头是注释。相对路径先按程序工作目录查找，找不到再按程序所在目录查找。路径里不要写逗号、等号或分号。
- 命中某一组后，只向该组上级转发。这一组全部无应答时返回 SERVFAIL，不再改走来源路由或默认上级。

### 优先级

1. 本地规则（规则表里的指定解析）
2. 域名组 `DomainGroups`
3. 来源 IP 路由 `DNSRoutes`
4. 默认上级 `DNSServer`

命中域名组或来源路由时，不读取全局记录缓存。这条转发路径也不会把应答写入全局记录，因此某个上级的结果不会被另一个上级的客户端或域名复用。两边都没命中时，仍按原来的方式读取全局记录缓存。

## 管理后台编辑转发

DNS 管理站是 Cube 里的「DNS服务器」区域。登录后打开 **转发设置**：

`/DNS/ForwardSetting`

页面上可以改默认上级 `DNSServer`、来源路由 `DNSRoutes`、域名组 `DomainGroups`（后缀、`*.`、`exact:`、`*`，以及 `file:` 的相对路径和文件内容）。只有具备该菜单查看、更新权限的管理员能打开和保存，未登录会转到原来的登录页。

保存仍写入 `Config/DNS.config`，域名文件写在这份配置所在目录之内的相对路径。DNS 服务大约每 15 秒读一次该文件：发现修改后先异步载入，下一次检查再装到转发器，通常 15～30 秒内生效，不必重启。管理站输出在 `Bin/Web`、服务输出在 `Bin/Server` 时，页面默认改服务那份配置；两边部署在同一目录时，把 `appsettings.json` 的 `DNS:ConfigFile` 设为 `Config\DNS.config`。

## 新生命开源项目矩阵
各项目默认支持net4.5/net4.0/netstandard2.0  

|                               项目                               | 年份  |  状态  | .NET Core | 说明                                               |
| :--------------------------------------------------------------: | :---: | :----: | :-------: | -------------------------------------------------- |
|                             基础组件                             |       |        |           | 支撑其它中间件以及产品项目                         |
|          [NewLife.Core](https://github.com/NewLifeX/X)           | 2002  | 维护中 |     √     | 算法、日志、网络、RPC、序列化、缓存、多线程        |
|              [XCode](https://github.com/NewLifeX/X)              | 2005  | 维护中 |     √     | 数据中间件，MySQL、SQLite、SqlServer、Oracle       |
|      [NewLife.Net](https://github.com/NewLifeX/NewLife.Net)      | 2005  | 维护中 |     √     | 网络库，千万级吞吐率，学习gRPC、Thrift             |
|     [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube)     | 2010  | 维护中 |     √     | Web魔方，权限基础框架，集成OAuth                   |
|                              中间件                              |       |        |           | 对接各知名中间件平台                               |
|    [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis)    | 2017  | 维护中 |     √     | Redis客户端，微秒级延迟，百亿级项目验证            |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | 2018  | 维护中 |     √     | 支持Apache RocketMQ和阿里云消息队列                |
|   [NewLife.Thrift](https://github.com/NewLifeX/NewLife.Thrift)   | 2019  | 维护中 |     √     | Thrift协议实现                                     |
|     [NewLife.Hive](https://github.com/NewLifeX/NewLife.Hive)     | 2019  | 维护中 |     √     | 纯托管读写Hive，Hadoop数据仓库，基于Thrift协议     |
|       [NewLife.MQ](https://github.com/NewLifeX/NewLife.MQ)       | 2016  | 维护中 |     √     | 轻量级消息队列                                     |
|             [NoDb](https://github.com/NewLifeX/NoDb)             | 2017  | 开发中 |     √     | NoSQL数据库，百万级kv读写性能，持久化              |
|    [NewLife.Cache](https://github.com/NewLifeX/NewLife.Cache)    | 2018  | 维护中 |     √     | 自定义缓存服务器                                   |
|      [NewLife.Ftp](https://github.com/NewLifeX/NewLife.Ftp)      | 2008  | 维护中 |     √     | Ftp客户端实现                                      |
|    [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql)    | 2018  | 开发中 |     √     | MySql驱动                                          |
|                             产品平台                             |       |        |           | 产品平台级，编译部署即用，个性化自定义             |
|           [AntJob](https://github.com/NewLifeX/AntJob)           | 2019  | 开发中 |     √     | 蚂蚁调度系统，大数据实时计算平台                   |
|         [Stardust](https://github.com/NewLifeX/Stardust)         | 2018  | 开发中 |     √     | 星尘，微服务平台，分布式平台                       |
|            [XLink](https://github.com/NewLifeX/XLink)            | 2016  | 维护中 |     √     | 物联网云平台                                       |
|           [XProxy](https://github.com/NewLifeX/XProxy)           | 2005  | 维护中 |     √     | 产品级反向代理                                     |
|          [XScript](https://github.com/NewLifeX/XScript)          | 2010  | 维护中 |     ×     | C#脚本引擎                                         |
|      [NewLife.DNS](https://github.com/NewLifeX/NewLife.DNS)      | 2011  | 维护中 |     ×     | DNS代理服务器                                      |
|      [NewLife.CMX](https://github.com/NewLifeX/NewLife.CMX)      | 2013  | 维护中 |     ×     | 内容管理系统                                       |
|          [SmartOS](https://github.com/NewLifeX/SmartOS)          | 2014  | 保密中 |   C++11   | 嵌入式操作系统，完全独立自主，ARM Cortex-M芯片架构 |
|         [GitCandy](https://github.com/NewLifeX/GitCandy)         | 2015  | 维护中 |     ×     | Git管理系统                                        |
|                               其它                               |       |        |           |                                                    |
|           [XCoder](https://github.com/NewLifeX/XCoder)           | 2006  | 维护中 |     ×     | 码神工具，开发者必备                               |
|        [XTemplate](https://github.com/NewLifeX/XTemplate)        | 2008  | 维护中 |     ×     | 模版引擎，T4(Text Template)语法                    |
|       [X组件 .NET2.0](https://github.com/NewLifeX/X_NET20)       | 2002  | 存档中 |  .NET2.0  | 日志、网络、RPC、序列化、缓存、Windows服务、多线程 |
|       [X组件 .NET4.0](https://github.com/NewLifeX/X_NET40)       | 2002  | 存档中 |  .NET4.0  | 日志、网络、RPC、序列化、缓存、Windows服务、多线程 |
