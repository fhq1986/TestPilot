using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.ApiTesting;

/// <summary>
/// k6 脚本生成的输入（迭代 F·P2-9）。
///
/// 刻意与 EF 解耦：生成器是**纯函数**，输入是已经装配好的 POCO，
/// 这样单测可以直接构造输入断言输出，不必起 Host、不必连库。
/// 装配（读 TestCase/TestStep 或解析 OpenAPI）在 Api 层的 LoadTestScriptBuilder 里做。
/// </summary>
public sealed record K6ScriptInput(
    string ScenarioName,
    /// <summary>靶站根地址，脚本里作为 BASE_URL 的默认值（可被 -e BASE_URL 覆盖）</summary>
    string BaseUrl,
    IReadOnlyDictionary<string, string> Variables,
    LoadTestProfile Profile,
    IReadOnlyList<LoadTestThreshold> Thresholds,
    IReadOnlyList<K6CaseSpec> Cases);

/// <summary>一个用例 → 脚本里的一个 group()</summary>
public sealed record K6CaseSpec(string Name, IReadOnlyList<K6StepSpec> Steps);

/// <summary>
/// 一个步骤。三个可空字段对应接口用例的三个专用动作
/// （Request / AssertResponse / ExtractVariable，见 ActionType）；
/// 非接口动作（Web 动作等）不生成代码，只把动作名塞进 <see cref="UnsupportedAction"/> 由上层汇总成告警。
/// </summary>
public sealed record K6StepSpec(
    int Order,
    K6RequestSpec? Request,
    K6AssertSpec? Assert,
    K6ExtractSpec? Extract,
    string? UnsupportedAction);

/// <summary>一次 HTTP 请求。Endpoint 可以是绝对 URL 或相对路径（相对时拼 BaseUrl）</summary>
public sealed record K6RequestSpec(
    string Method,
    string Endpoint,
    IReadOnlyList<K6HeaderSpec> Headers,
    /// <summary>原始请求体文本（可能含 <c>{变量}</c> 占位符）；为空表示无 body</summary>
    string? Body);

public sealed record K6HeaderSpec(string Name, string Value);

/// <summary>断言。<see cref="StatusExpectation"/> 形如 <c>200</c> / <c>2xx</c>；<see cref="ExpectedBodyJson"/> 是期望的 JSON 子集</summary>
public sealed record K6AssertSpec(string? StatusExpectation, string? ExpectedBodyJson);

/// <summary>变量提取：把响应里的 JSONPath 取值存进上下文，供后续步骤 <c>{变量}</c> 使用</summary>
public sealed record K6ExtractSpec(string VariableName, string JsonPath);

/// <summary>生成结果。<see cref="Warnings"/> 是「有步骤没能翻译成 k6」的说明，要透出给用户而不是静默丢弃</summary>
public sealed record K6GenerateResult(string Script, string Hash, IReadOnlyList<string> Warnings);
