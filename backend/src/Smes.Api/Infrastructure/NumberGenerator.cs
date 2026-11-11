using Microsoft.EntityFrameworkCore;
using Smes.Domain.Identity;
using Smes.Infrastructure;

namespace Smes.Api.Infrastructure;

/// <summary>单号生成：按 SysNumberRule 配置的 前缀+日期+流水号 生成，进程内串行避免并发重号。</summary>
public static class NumberGenerator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<string> NextAsync(SmesDbContext db, string ruleCode, long userId)
    {
        await Gate.WaitAsync();
        try
        {
            var rule = await db.Set<SysNumberRule>().FirstOrDefaultAsync(x => x.Code == ruleCode);
            if (rule == null)
            {
                return DateTime.Now.ToString("yyyyMMddHHmmssfff");
            }
            rule.CurrentSeq++;
            rule.UpdatedAt = DateTime.Now;
            rule.UpdatedBy = userId;
            await db.SaveChangesAsync();
            var date = DateTime.Now.ToString(rule.DateFormat);
            return $"{rule.Prefix}{date}{rule.CurrentSeq.ToString().PadLeft(rule.SeqLength, '0')}";
        }
        finally
        {
            Gate.Release();
        }
    }
}
