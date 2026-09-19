namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 步骤 <c>config.value</c> 里那些"其实是列表"的值的解析。
///
/// 单独抽出来是为了**可测**：这些解析规则藏在 TestRunner 的 switch 里时，
/// 只有真起一个浏览器才能验证，而它们本身是纯函数。
/// </summary>
public static class ConfigValueParser
{
    /// <summary>
    /// 解析上传文件路径列表：换行或分号分隔（两种都支持，因为用户从 Windows 资源管理器
    /// 复制多选路径时带的是换行，而手写时常顺手用分号）。
    ///
    /// 去空白、丢弃空项；**不做去重**——传同一个文件两次是合法用法
    /// （例如验证"重复上传同一文件"的业务规则）。
    /// </summary>
    public static string[] SplitFilePaths(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
    }
}
