using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Scripts;

/// <summary>解析出的单个步骤</summary>
public record ParsedStepDto(
    int StepOrder, ActionType ActionType, StepConfig Config,
    string? Instruction, string? Description,
    /// <summary>原始脚本行（便于对照与人工修正）</summary>
    string SourceLine,
    /// <summary>映射说明或降级提示（例如 press → Fill 的近似处理）</summary>
    string? Note);

public record ScriptParseResult(
    bool Ok, string? Error,
    List<ParsedStepDto> Steps,
    List<string> Warnings,
    /// <summary>从脚本推断的用例名（describe/test 的第一个字符串参数）</summary>
    string? SuggestedName,
    /// <summary>从脚本推断的类型：有 request/API 调用则为 Api，否则 Web</summary>
    TestType SuggestedType,
    /// <summary>从脚本推断的起始地址（第一个 goto 的绝对 URL 的 origin）</summary>
    string? SuggestedBaseUrl);

public record ParseScriptRequest(string Script);

public record ImportScriptRequest(
    Guid ProjectId, string Name, string Script,
    string? Module = null, string? Priority = null,
    string? BaseUrl = null, string? Description = null,
    /// <summary>true 时脚本解析失败的行会被跳过（默认跳过并返回告警）</summary>
    bool SkipUnsupportedLines = true,
    /// <summary>可选：创建的用例自动加入该测试计划范围（必须属于同一项目）</summary>
    Guid? TestPlanId = null);

public record ScriptImportResult(
    Guid TestCaseId, string Name, int StepCount, List<string> Warnings, TestType Type,
    // 可选的测试计划关联：新加入计划范围的用例数 / 已在范围内被跳过的用例数
    int PlanLinked = 0, int PlanSkipped = 0);

public record ScriptExportDto(Guid TestCaseId, string Name, string Language, string Script);
