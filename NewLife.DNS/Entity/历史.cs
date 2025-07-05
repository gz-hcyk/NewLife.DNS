using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Xml.Serialization;
using NewLife;
using NewLife.Data;
using XCode;
using XCode.Cache;
using XCode.Configuration;
using XCode.DataAccessLayer;

namespace NewLife.DNS.Entity;

/// <summary>历史</summary>
[Serializable]
[DataObject]
[Description("历史")]
[BindIndex("IX_History_Name", false, "Name")]
[BindIndex("IX_History_Type", false, "Type")]
[BindIndex("IX_History_UserIP", false, "UserIP")]
[BindTable("History", Description = "历史", ConnName = "History", DbType = DatabaseType.SqlServer)]
public partial class History
{
    #region 属性
    private Int32 _ID;
    /// <summary>编号</summary>
    [DisplayName("编号")]
    [Description("编号")]
    [DataObjectField(true, true, false, 0)]
    [BindColumn("ID", "编号", "")]
    public Int32 ID { get => _ID; set { if (OnPropertyChanging("ID", value)) { _ID = value; OnPropertyChanged("ID"); } } }

    private Int32 _Type;
    /// <summary>类型</summary>
    [DisplayName("类型")]
    [Description("类型")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Type", "类型", "")]
    public Int32 Type { get => _Type; set { if (OnPropertyChanging("Type", value)) { _Type = value; OnPropertyChanged("Type"); } } }

    private String _Name;
    /// <summary>名称</summary>
    [DisplayName("名称")]
    [Description("名称")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Name", "名称", "", Master = true)]
    public String Name { get => _Name; set { if (OnPropertyChanging("Name", value)) { _Name = value; OnPropertyChanged("Name"); } } }

    private String _Address;
    /// <summary>地址</summary>
    [DisplayName("地址")]
    [Description("地址")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Address", "地址", "")]
    public String Address { get => _Address; set { if (OnPropertyChanging("Address", value)) { _Address = value; OnPropertyChanged("Address"); } } }

    private String _UserIP;
    /// <summary>用户地址</summary>
    [DisplayName("用户地址")]
    [Description("用户地址")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("UserIP", "用户地址", "")]
    public String UserIP { get => _UserIP; set { if (OnPropertyChanging("UserIP", value)) { _UserIP = value; OnPropertyChanged("UserIP"); } } }

    private Int32 _Protocol;
    /// <summary>协议</summary>
    [DisplayName("协议")]
    [Description("协议")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Protocol", "协议", "")]
    public Int32 Protocol { get => _Protocol; set { if (OnPropertyChanging("Protocol", value)) { _Protocol = value; OnPropertyChanged("Protocol"); } } }

    private Int32 _CreateUserID;
    /// <summary>创建者</summary>
    [DisplayName("创建者")]
    [Description("创建者")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("CreateUserID", "创建者", "")]
    public Int32 CreateUserID { get => _CreateUserID; set { if (OnPropertyChanging("CreateUserID", value)) { _CreateUserID = value; OnPropertyChanged("CreateUserID"); } } }

    private DateTime _CreateTime;
    /// <summary>创建时间</summary>
    [DisplayName("创建时间")]
    [Description("创建时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("CreateTime", "创建时间", "")]
    public DateTime CreateTime { get => _CreateTime; set { if (OnPropertyChanging("CreateTime", value)) { _CreateTime = value; OnPropertyChanged("CreateTime"); } } }

    private String _CreateIP;
    /// <summary>创建地址</summary>
    [DisplayName("创建地址")]
    [Description("创建地址")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("CreateIP", "创建地址", "")]
    public String CreateIP { get => _CreateIP; set { if (OnPropertyChanging("CreateIP", value)) { _CreateIP = value; OnPropertyChanged("CreateIP"); } } }
    #endregion

    #region 获取/设置 字段值
    /// <summary>获取/设置 字段值</summary>
    /// <param name="name">字段名</param>
    /// <returns></returns>
    public override Object this[String name]
    {
        get => name switch
        {
            "ID" => _ID,
            "Type" => _Type,
            "Name" => _Name,
            "Address" => _Address,
            "UserIP" => _UserIP,
            "Protocol" => _Protocol,
            "CreateUserID" => _CreateUserID,
            "CreateTime" => _CreateTime,
            "CreateIP" => _CreateIP,
            _ => base[name]
        };
        set
        {
            switch (name)
            {
                case "ID": _ID = value.ToInt(); break;
                case "Type": _Type = value.ToInt(); break;
                case "Name": _Name = Convert.ToString(value); break;
                case "Address": _Address = Convert.ToString(value); break;
                case "UserIP": _UserIP = Convert.ToString(value); break;
                case "Protocol": _Protocol = value.ToInt(); break;
                case "CreateUserID": _CreateUserID = value.ToInt(); break;
                case "CreateTime": _CreateTime = value.ToDateTime(); break;
                case "CreateIP": _CreateIP = Convert.ToString(value); break;
                default: base[name] = value; break;
            }
        }
    }
    #endregion

    #region 关联映射
    #endregion

    #region 扩展查询
    /// <summary>根据名称查找</summary>
    /// <param name="name">名称</param>
    /// <returns>实体列表</returns>
    public static IList<History> FindAllByName(String name)
    {
        if (name.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < 1000) return Meta.Cache.FindAll(e => e.Name.EqualIgnoreCase(name));

        return FindAll(_.Name == name);
    }

    /// <summary>根据类型查找</summary>
    /// <param name="type">类型</param>
    /// <returns>实体列表</returns>
    public static IList<History> FindAllByType(Int32 type)
    {
        if (type < 0) return [];

        // 实体缓存
        if (Meta.Session.Count < 1000) return Meta.Cache.FindAll(e => e.Type == type);

        return FindAll(_.Type == type);
    }

    /// <summary>根据用户地址查找</summary>
    /// <param name="userIP">用户地址</param>
    /// <returns>实体列表</returns>
    public static IList<History> FindAllByUserIP(String userIP)
    {
        if (userIP.IsNullOrEmpty()) return [];

        // 实体缓存
        if (Meta.Session.Count < 1000) return Meta.Cache.FindAll(e => e.UserIP.EqualIgnoreCase(userIP));

        return FindAll(_.UserIP == userIP);
    }
    #endregion

    #region 字段名
    /// <summary>取得历史字段信息的快捷方式</summary>
    public partial class _
    {
        /// <summary>编号</summary>
        public static readonly Field ID = FindByName("ID");

        /// <summary>类型</summary>
        public static readonly Field Type = FindByName("Type");

        /// <summary>名称</summary>
        public static readonly Field Name = FindByName("Name");

        /// <summary>地址</summary>
        public static readonly Field Address = FindByName("Address");

        /// <summary>用户地址</summary>
        public static readonly Field UserIP = FindByName("UserIP");

        /// <summary>协议</summary>
        public static readonly Field Protocol = FindByName("Protocol");

        /// <summary>创建者</summary>
        public static readonly Field CreateUserID = FindByName("CreateUserID");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        /// <summary>创建地址</summary>
        public static readonly Field CreateIP = FindByName("CreateIP");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得历史字段名称的快捷方式</summary>
    public partial class __
    {
        /// <summary>编号</summary>
        public const String ID = "ID";

        /// <summary>类型</summary>
        public const String Type = "Type";

        /// <summary>名称</summary>
        public const String Name = "Name";

        /// <summary>地址</summary>
        public const String Address = "Address";

        /// <summary>用户地址</summary>
        public const String UserIP = "UserIP";

        /// <summary>协议</summary>
        public const String Protocol = "Protocol";

        /// <summary>创建者</summary>
        public const String CreateUserID = "CreateUserID";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";

        /// <summary>创建地址</summary>
        public const String CreateIP = "CreateIP";
    }
    #endregion
}
