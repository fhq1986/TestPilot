namespace AI.TestPlatform.Application.Settings;

public record AIModelOption(string Id, string Label, string? Note = null);

public record AIProviderPreset(
    string Id,
    string Name,
    string BaseUrl,
    string KeyUrl,
    string? Note,
    IReadOnlyList<AIModelOption> Models);

/// <summary>
/// 常见 AI 提供商的接入预设（Base URL + 常用模型）。
/// 模型名与地址会随厂商更新，这里给出主流选项，前端允许手动输入其它模型名。
/// </summary>
public static class AIProviderCatalog
{
    public static IReadOnlyList<AIProviderPreset> Providers { get; } = new List<AIProviderPreset>
    {
        new(
            Id: "deepseek",
            Name: "DeepSeek（深度求索）",
            BaseUrl: "https://api.deepseek.com",
            KeyUrl: "https://platform.deepseek.com/api_keys",
            Note: "旧模型名 deepseek-chat / deepseek-reasoner 已停用，请使用 V4 系列",
            Models: new List<AIModelOption>
            {
                new("deepseek-v4-flash", "deepseek-v4-flash", "速度快、价格低，日常首选"),
                new("deepseek-v4-pro", "deepseek-v4-pro", "复杂任务、长链路推理"),
                new("deepseek-v4-flash-vision-exp", "deepseek-v4-flash-vision-exp", "实验版，额外支持图片输入"),
            }),

        new(
            Id: "moonshot",
            Name: "Kimi（月之暗面 / Moonshot）",
            BaseUrl: "https://api.moonshot.cn/v1",
            KeyUrl: "https://platform.moonshot.cn/console/api-keys",
            Note: "长文本与中文理解见长，K2.5 支持视觉",
            Models: new List<AIModelOption>
            {
                new("kimi-k3", "kimi-k3", "最新一代旗舰"),
                new("kimi-k2.6", "kimi-k2.6", "K2.6 旗舰"),
                new("kimi-k2.5", "kimi-k2.5", "K2.5 旗舰"),
                new("kimi-k2.5-thinking", "kimi-k2.5-thinking", "思考模式"),
                new("moonshot-v1-128k", "moonshot-v1-128k", "128K 长上下文"),
                new("kimi-k2-0905-preview", "kimi-k2-0905-preview", "K2 预览版"),
            }),

        new(
            Id: "zhipu",
            Name: "智谱 AI（GLM）",
            BaseUrl: "https://open.bigmodel.cn/api/paas/v4",
            KeyUrl: "https://open.bigmodel.cn/usercenter/apikeys",
            Note: "GLM-4.5-Flash / GLM-4.7-Flash 系列有免费额度",
            Models: new List<AIModelOption>
            {
                new("GLM-5.3", "GLM-5.3", "最新一代旗舰"),
                new("GLM-5.2", "GLM-5.2", "新一代旗舰"),
                new("GLM-5", "GLM-5", "744B MoE 旗舰"),
                new("GLM-5V-Turbo", "GLM-5V-Turbo", "原生多模态"),
                new("GLM-4.7", "GLM-4.7", "编码与长程任务"),
                new("GLM-4.7-FlashX", "GLM-4.7-FlashX", "轻量高速"),
                new("GLM-4.6", "GLM-4.6", "通用能力强"),
                new("GLM-4.5-Air", "GLM-4.5-Air", "高性价比"),
            }),

        new(
            Id: "minimax",
            Name: "MiniMax",
            BaseUrl: "https://api.minimax.chat/v1",
            KeyUrl: "https://platform.minimaxi.com/user-center/api-keys",
            Note: "Agent 与多轮对话场景表现好",
            Models: new List<AIModelOption>
            {
                new("MiniMax-M3", "MiniMax-M3", "最新一代旗舰"),
                new("MiniMax-M2.7", "MiniMax-M2.7", "上一代旗舰"),
                new("MiniMax-M2.5", "MiniMax-M2.5", "上一代旗舰"),
                new("MiniMax-Text-01", "MiniMax-Text-01", "长上下文通用"),
            }),

        new(
            Id: "openai",
            Name: "OpenAI",
            BaseUrl: "https://api.openai.com/v1",
            KeyUrl: "https://platform.openai.com/api-keys",
            Note: "国内直连可能需自备网络/代理",
            Models: new List<AIModelOption>
            {
                new("gpt-5.4", "gpt-5.4", "旗舰通用推理"),
                new("gpt-4o", "gpt-4o", "多模态通用"),
            }),

        new(
            Id: "custom",
            Name: "自定义 / 其它（OpenAI 兼容）",
            BaseUrl: string.Empty,
            KeyUrl: string.Empty,
            Note: "自行填写 Base URL 与模型名，需兼容 OpenAI Chat Completions 协议",
            Models: new List<AIModelOption>()),
    };
}
