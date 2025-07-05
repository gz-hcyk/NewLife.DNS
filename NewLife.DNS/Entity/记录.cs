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

/// <summary>记录</summary>
[Serializable]
[DataObject]
[Description("记录")]
[BindIndex("IX_Record_Type", false, "Type")]
[BindTable("Record", Description = "记录", ConnName = "DNS", DbType = DatabaseType.SqlServer)]
public partial class Record
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

    private DateTime _Ttl;
    /// <summary>生存时间</summary>
    [DisplayName("生存时间")]
    [Description("生存时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("Ttl", "生存时间", "")]
    public DateTime Ttl { get => _Ttl; set { if (OnPropertyChanging("Ttl", value)) { _Ttl = value; OnPropertyChanged("Ttl"); } } }

    private String _Parent;
    /// <summary>父级</summary>
    [DisplayName("父级")]
    [Description("父级")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("Parent", "父级", "")]
    public String Parent { get => _Parent; set { if (OnPropertyChanging("Parent", value)) { _Parent = value; OnPropertyChanged("Parent"); } } }

    private Int32 _Hits;
    /// <summary>次数</summary>
    [DisplayName("次数")]
    [Description("次数")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("Hits", "次数", "")]
    public Int32 Hits { get => _Hits; set { if (OnPropertyChanging("Hits", value)) { _Hits = value; OnPropertyChanged("Hits"); } } }

    private Int32 _StatID;
    /// <summary>统计</summary>
    [DisplayName("统计")]
    [Description("统计")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("StatID", "统计", "")]
    public Int32 StatID { get => _StatID; set { if (OnPropertyChanging("StatID", value)) { _StatID = value; OnPropertyChanged("StatID"); } } }

    private DateTime _Next;
    /// <summary>下次更新</summary>
    [DisplayName("下次更新")]
    [Description("下次更新")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("Next", "下次更新", "")]
    public DateTime Next { get => _Next; set { if (OnPropertyChanging("Next", value)) { _Next = value; OnPropertyChanged("Next"); } } }

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

    private Int32 _UpdateUserID;
    /// <summary>更新者</summary>
    [DisplayName("更新者")]
    [Description("更新者")]
    [DataObjectField(false, false, false, 0)]
    [BindColumn("UpdateUserID", "更新者", "")]
    public Int32 UpdateUserID { get => _UpdateUserID; set { if (OnPropertyChanging("UpdateUserID", value)) { _UpdateUserID = value; OnPropertyChanged("UpdateUserID"); } } }

    private DateTime _UpdateTime;
    /// <summary>更新时间</summary>
    [DisplayName("更新时间")]
    [Description("更新时间")]
    [DataObjectField(false, false, true, 0)]
    [BindColumn("UpdateTime", "更新时间", "")]
    public DateTime UpdateTime { get => _UpdateTime; set { if (OnPropertyChanging("UpdateTime", value)) { _UpdateTime = value; OnPropertyChanged("UpdateTime"); } } }

    private String _UpdateIP;
    /// <summary>更新地址</summary>
    [DisplayName("更新地址")]
    [Description("更新地址")]
    [DataObjectField(false, false, true, 50)]
    [BindColumn("UpdateIP", "更新地址", "")]
    public String UpdateIP { get => _UpdateIP; set { if (OnPropertyChanging("UpdateIP", value)) { _UpdateIP = value; OnPropertyChanged("UpdateIP"); } } }
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
            "Ttl" => _Ttl,
            "Parent" => _Parent,
            "Hits" => _Hits,
            "StatID" => _StatID,
            "Next" => _Next,
            "CreateUserID" => _CreateUserID,
            "CreateTime" => _CreateTime,
            "CreateIP" => _CreateIP,
            "UpdateUserID" => _UpdateUserID,
            "UpdateTime" => _UpdateTime,
            "UpdateIP" => _UpdateIP,
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
                case "Ttl": _Ttl = value.ToDateTime(); break;
                case "Parent": _Parent = Convert.ToString(value); break;
                case "Hits": _Hits = value.ToInt(); break;
                case "StatID": _StatID = value.ToInt(); break;
                case "Next": _Next = value.ToDateTime(); break;
                case "CreateUserID": _CreateUserID = value.ToInt(); break;
                case "CreateTime": _CreateTime = value.ToDateTime(); break;
                case "CreateIP": _CreateIP = Convert.ToString(value); break;
                case "UpdateUserID": _UpdateUserID = value.ToInt(); break;
                case "UpdateTime": _UpdateTime = value.ToDateTime(); break;
                case "UpdateIP": _UpdateIP = Convert.ToString(value); break;
                default: base[name] = value; break;
            }
        }
    }
    #endregion

    #region 关联映射
    #endregion

    #region 扩展查询
    /// <summary>根据类型查找</summary>
    /// <param name="type">类型</param>
    /// <returns>实体列表</returns>
    public static IList<Record> FindAllByType(Int32 type)
    {
        if (type < 0) return [];

        // 实体缓存
        if (Meta.Session.Count < 1000) return Meta.Cache.FindAll(e => e.Type == type);

        return FindAll(_.Type == type);
    }
    #endregion

    #region 字段名
    /// <summary>取得记录字段信息的快捷方式</summary>
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

        /// <summary>生存时间</summary>
        public static readonly Field Ttl = FindByName("Ttl");

        /// <summary>父级</summary>
        public static readonly Field Parent = FindByName("Parent");

        /// <summary>次数</summary>
        public static readonly Field Hits = FindByName("Hits");

        /// <summary>统计</summary>
        public static readonly Field StatID = FindByName("StatID");

        /// <summary>下次更新</summary>
        public static readonly Field Next = FindByName("Next");

        /// <summary>创建者</summary>
        public static readonly Field CreateUserID = FindByName("CreateUserID");

        /// <summary>创建时间</summary>
        public static readonly Field CreateTime = FindByName("CreateTime");

        /// <summary>创建地址</summary>
        public static readonly Field CreateIP = FindByName("CreateIP");

        /// <summary>更新者</summary>
        public static readonly Field UpdateUserID = FindByName("UpdateUserID");

        /// <summary>更新时间</summary>
        public static readonly Field UpdateTime = FindByName("UpdateTime");

        /// <summary>更新地址</summary>
        public static readonly Field UpdateIP = FindByName("UpdateIP");

        static Field FindByName(String name) => Meta.Table.FindByName(name);
    }

    /// <summary>取得记录字段名称的快捷方式</summary>
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

        /// <summary>生存时间</summary>
        public const String Ttl = "Ttl";

        /// <summary>父级</summary>
        public const String Parent = "Parent";

        /// <summary>次数</summary>
        public const String Hits = "Hits";

        /// <summary>统计</summary>
        public const String StatID = "StatID";

        /// <summary>下次更新</summary>
        public const String Next = "Next";

        /// <summary>创建者</summary>
        public const String CreateUserID = "CreateUserID";

        /// <summary>创建时间</summary>
        public const String CreateTime = "CreateTime";

        /// <summary>创建地址</summary>
        public const String CreateIP = "CreateIP";

        /// <summary>更新者</summary>
        public const String UpdateUserID = "UpdateUserID";

        /// <summary>更新时间</summary>
        public const String UpdateTime = "UpdateTime";

        /// <summary>更新地址</summary>
        public const String UpdateIP = "UpdateIP";
    }
    #endregion
}
