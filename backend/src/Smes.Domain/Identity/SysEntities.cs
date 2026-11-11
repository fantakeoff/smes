using System.Text.Json.Serialization;
using Smes.Domain.Common;

namespace Smes.Domain.Identity;

public class SysUser : BaseEntity
{
    public string Username { get; set; } = "";
    [JsonIgnore] public string PasswordHash { get; set; } = "";
    public string? Nickname { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Avatar { get; set; }
    public long? DeptId { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class SysRole : BaseEntity
{
    public int Sort { get; set; }
    /// <summary>数据范围：1 全部 / 2 本部门 / 3 本人</summary>
    public int DataScope { get; set; } = 1;
}

public class SysUserRole
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long UserId { get; set; }
    public long RoleId { get; set; }
}

/// <summary>菜单/路由/按钮权限。Type: 1 目录 2 菜单 3 按钮</summary>
public class SysMenu : BaseEntity
{
    public long ParentId { get; set; }
    public string Title { get; set; } = "";
    public string? Path { get; set; }
    public string? Component { get; set; }
    public string? Icon { get; set; }
    public int Type { get; set; } = 2;
    public string? Permission { get; set; }
    public int Sort { get; set; }
    public bool Visible { get; set; } = true;
    public bool KeepAlive { get; set; }
}

public class SysRoleMenu
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long RoleId { get; set; }
    public long MenuId { get; set; }
}

public class SysDept : BaseEntity
{
    public long ParentId { get; set; }
    public string? Leader { get; set; }
    public string? Phone { get; set; }
    public int Sort { get; set; }
}

public class SysDictType : BaseEntity
{
    public string? Description { get; set; }
}

public class SysDictItem : BaseEntity
{
    public long DictTypeId { get; set; }
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public int Sort { get; set; }
    public string? TagType { get; set; }
}

public class SysConfig : BaseEntity
{
    public string Value { get; set; } = "";
    public string? Group { get; set; }
    public string? Description { get; set; }
}

public class SysLog
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public long? UserId { get; set; }
    public string? Username { get; set; }
    public string? Module { get; set; }
    public string? Action { get; set; }
    public string? Method { get; set; }
    public string? Path { get; set; }
    public string? Ip { get; set; }
    public string? Params { get; set; }
    public int ElapsedMs { get; set; }
    public int Success { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class SysNotice : BaseEntity
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    /// <summary>1 通知 / 2 公告</summary>
    public int Type { get; set; } = 1;
    public long? PublisherId { get; set; }
    public DateTime PublishTime { get; set; } = DateTime.Now;
}

public class SysMessage : BaseEntity
{
    public long ReceiverId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    /// <summary>1 系统通知 / 2 站内信 / 3 业务提醒</summary>
    public int Type { get; set; } = 2;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? Link { get; set; }
}

/// <summary>编号规则：前缀 + 日期 + 流水号</summary>
public class SysNumberRule : BaseEntity
{
    public string Prefix { get; set; } = "";
    public string DateFormat { get; set; } = "yyyyMMdd";
    public int SeqLength { get; set; } = 4;
    public int CurrentSeq { get; set; }
    public int ResetCycle { get; set; }
    public string? Example { get; set; }
}
