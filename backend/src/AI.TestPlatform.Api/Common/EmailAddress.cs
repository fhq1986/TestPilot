using System.Net.Mail;

namespace AI.TestPlatform.Api.Common;

/// <summary>
/// 邮箱地址的解析与校验。
///
/// 用户表单的「邮箱」校验与发信时的收件人过滤**必须用同一把尺子**——
/// 否则会出现「表单说这邮箱合法、发信时却被静默丢掉」这种最难查的问题。
/// </summary>
public static class EmailAddress
{
    /// <summary>单封邮件最多多少收件人，防止一个误填的群发地址把 SMTP 拖死</summary>
    public const int MaxRecipients = 50;

    /// <summary>
    /// 是否是**裸地址**（不含显示名）且域名带点。
    ///
    /// 三个约束各自的理由：
    /// <list type="bullet">
    /// <item>用 <see cref="MailAddress"/> 而不是正则做基础语法判断——地址语法比正则能表达的要绕得多。</item>
    /// <item>拒绝 <c>Foo &lt;a@b.com&gt;</c> 这种带显示名的形式：字段语义是"邮箱"，
    ///   混进显示名后收件人列表里会出现难以察觉的引号与尖括号，排查起来很费劲。</item>
    /// <item>**额外要求域名带点**。这一条不是语法要求（RFC 允许 <c>abc@intranet</c>），
    ///   而是为了和前端表单用同一把尺子：只靠 MailAddress 判定时，
    ///   连 <c>缺@符号</c> 都算"合法"，表单却会拦下来，两边对不上。
    ///   顺带挡住漏写 TLD 这类最常见的笔误。代价是不支持纯主机名地址，
    ///   而本平台的收件人都是公网 / 企业邮，可以接受。</item>
    /// </list>
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim();
        if (candidate.Length > 200) return false;
        if (!MailAddress.TryCreate(candidate, out var parsed)) return false;
        if (!string.Equals(parsed.Address, candidate, StringComparison.OrdinalIgnoreCase)) return false;

        var at = candidate.LastIndexOf('@');
        if (at <= 0 || candidate.EndsWith('.')) return false;
        var dot = candidate.IndexOf('.', at + 1);
        // 点必须落在域名中间（不能是 "@.com" 也不能以点结尾）
        return dot > at + 1 && dot < candidate.Length - 1;
    }

    /// <summary>
    /// 按逗号 / 分号分隔并清洗：去空白、丢掉非法项、去重（忽略大小写）、限量。
    /// 非法项**丢弃而不是报错**——发信是尽力而为的旁路流程，
    /// 不应该因为某人少写一个 @ 就让整封报告发不出去。
    /// </summary>
    public static IReadOnlyList<string> ParseList(string? raw, int max = MaxRecipients)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        return raw.Split(new[] { ',', ';', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsValid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }
}
