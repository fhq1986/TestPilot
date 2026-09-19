namespace AI.TestPlatform.Domain.Entities;

// JSONB 映射对象
// Web: { Url, Selector: { Type, Description, Value } }
// API: { Method, Endpoint, Headers, Body }
// 注意：Headers 使用 List<HeaderEntry>（而非 Dictionary），Body 使用 string（原始 JSON 文本）——
// EF Core 的 ToJson 自有类型不支持 Dictionary/JsonElement 属性（写入时 NRE），故采用此结构。
public class StepConfig
{
    public string? Url { get; set; }
    public SelectorConfig? Selector { get; set; }
    public string? Method { get; set; }
    public string? Endpoint { get; set; }
    public List<HeaderEntry>? Headers { get; set; }
    public string? Body { get; set; }

    public string? Value { get; set; }  // Fill 输入文本 / Wait 毫秒数 / AssertText 期望文本
                                       // Select 选项值 / UploadFile 文件路径 / PressKey 按键名
                                       // AssertCount 期望个数 / AssertState 状态名
                                       // AssertAttribute/AssertValue 期望片段

    /// <summary>
    /// AssertAttribute 要读取的属性名（如 disabled、aria-label、href、data-status）。
    /// 单独开一个字段而不是塞进 Value，是因为"断言哪个属性"和"期望什么值"是两件事，
    /// 挤在一个字段里就得自己发明分隔符，编辑器也没法给出各自的提示。
    /// </summary>
    public string? Attribute { get; set; }
}

public class SelectorConfig
{
    public string Type { get; set; } = "css";  // css, xpath, ai
    public string? Description { get; set; }   // AI 描述："蓝色登录按钮"
    public string? Value { get; set; }         // 实际选择器值
}

public class HeaderEntry
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
