using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.AI;

/// <summary>
/// 把一条 <see cref="FixActionDto"/> 应用到「执行期步骤副本」上。
///
/// ⚠ 三条铁律（见 docs/m8-agent-design.md §4.3 / §17）：
/// 1. **只改传入的副本**，绝不触碰数据库里的真实 <see cref="TestStep"/>——错误自愈不能永久篡改用户用例；
/// 2. **服务端校验，不依赖 LLM 自觉**：动作白名单 + 参数校验（定位类型、非法字符、URL 同源），
///    未通过即拒绝该动作（调用方记 Skipped），避免页面文本里的提示注入诱导越权动作；
/// 3. **破坏性动作（增/删/重排/放宽断言）必须走人工审批**（由 AgentLoopService 强制 NeedsApproval，
///    本类只负责"给定动作如何安全落地"）。
///
/// 支持的动作（与 AIWorker 提示词、FixCategory 对应）：
/// update_locator / step_config_patch / wait_strategy / relax_assert / add_step / delete_step / reorder_step。
/// </summary>
public static class FixActionApplier
{
    /// <summary>本类实际支持的动作集合。</summary>
    public static readonly IReadOnlySet<string> SupportedActions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "update_locator", "step_config_patch", "wait_strategy", "relax_assert",
            "add_step", "delete_step", "reorder_step",
        };

    /// <summary>结构性变更（会改变步骤数量/顺序）——调用方应强制人工审批。</summary>
    public static bool IsDestructive(string actionType) => actionType is
        "add_step" or "delete_step" or "reorder_step" or "relax_assert";

    /// <summary>
    /// 尝试把动作应用到副本。成功返回 true 并就地修改副本；失败返回 false 并通过 <paramref name="error"/> 给出原因。
    /// <paramref name="baseUrl"/> 用于校验 Navigate 类 URL 的同源性（为空则跳过该检查）。
    /// </summary>
    public static bool TryApply(IList<TestStep> steps, FixActionDto action, out string? error,
        string? baseUrl = null)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(action.ActionType) || !SupportedActions.Contains(action.ActionType))
        {
            error = $"动作 {action.ActionType} 不受支持，已跳过";
            return false;
        }

        switch (action.ActionType)
        {
            case "update_locator":
                return TryUpdateLocator(steps, action, out error);
            case "step_config_patch":
                return TryPatchConfig(steps, action, out error, baseUrl);
            case "wait_strategy":
                return TryApplyWaitStrategy(steps, action, out error);
            case "relax_assert":
                return TryRelaxAssert(steps, action, out error);
            case "add_step":
                return TryAddStep(steps, action, out error, baseUrl);
            case "delete_step":
                return TryDeleteStep(steps, action, out error);
            case "reorder_step":
                return TryReorderStep(steps, action, out error);
            default:
                error = $"未处理的动作 {action.ActionType}";
                return false;
        }
    }

    // ------------------------------------------------------------------ 各动作实现

    private static bool TryUpdateLocator(IList<TestStep> steps, FixActionDto action, out string? error)
    {
        error = null;
        if (!TryFindStep(steps, action, out var step, out error)) return false;

        if (!TryGetString(action.Params, out var value, "locator_value", "selector_value", "selector", "value"))
        {
            error = "update_locator 缺少 locator_value";
            return false;
        }
        value = value.Trim();
        if (ContainsInjectionChars(value))
        {
            error = "定位器包含非法字符，已拒绝";
            return false;
        }

        var type = TryGetString(action.Params, out var t, "locator_type", "selector_type") && !string.IsNullOrWhiteSpace(t)
            ? t.Trim().ToLowerInvariant()
            : "css";
        if (type is not ("css" or "xpath"))
        {
            error = $"不支持的定位类型 {type}";
            return false;
        }

        step.Config ??= new StepConfig();
        step.Config.Selector ??= new SelectorConfig();
        step.Config.Selector.Type = type;
        step.Config.Selector.Value = value;
        return true;
    }

    private static bool TryPatchConfig(IList<TestStep> steps, FixActionDto action, out string? error,
        string? baseUrl)
    {
        error = null;
        if (!TryFindStep(steps, action, out var step, out error)) return false;

        if (!TryGetString(action.Params, out var field, "field", "key", "config_field"))
        {
            error = "step_config_patch 缺少 field";
            return false;
        }
        if (!TryGetString(action.Params, out var value, "value", "new_value", "config_value"))
        {
            error = "step_config_patch 缺少 value";
            return false;
        }
        if (ContainsInjectionChars(value))
        {
            error = "配置值包含非法字符，已拒绝";
            return false;
        }

        step.Config ??= new StepConfig();
        switch (field.Trim().ToLowerInvariant())
        {
            case "url":
                if (!IsUrlSafe(value, baseUrl, out var urlError)) { error = urlError; return false; }
                step.Config.Url = value.Trim();
                return true;
            case "endpoint":
                step.Config.Endpoint = value.Trim();
                return true;
            case "method":
                step.Config.Method = value.Trim().ToUpperInvariant();
                return true;
            case "body":
                step.Config.Body = value;
                return true;
            case "value":
                step.Config.Value = value;
                return true;
            case "attribute":
                step.Config.Attribute = value.Trim();
                return true;
            default:
                error = $"不支持的配置字段 {field}";
                return false;
        }
    }

    private static bool TryApplyWaitStrategy(IList<TestStep> steps, FixActionDto action, out string? error)
    {
        error = null;
        if (!TryGetInt(action.Params, out var ms, "timeout_ms", "milliseconds", "ms", "wait_ms"))
        {
            error = "wait_strategy 缺少 timeout_ms";
            return false;
        }
        ms = Math.Clamp(ms, 100, 120_000);

        // 目标步骤本身是 Wait：直接改等待时长
        if (TryFindStep(steps, action, out var step, out _))
        {
            if (step.ActionType == ActionType.Wait)
            {
                step.Config ??= new StepConfig();
                step.Config.Value = ms.ToString();
                return true;
            }
            // 否则在目标步骤**之前**插入一个 Wait（保持原有顺序语义）
            InsertAt(steps, action.StepOrder ?? step.StepOrder, NewWaitStep(ms));
            Renumber(steps);
            return true;
        }

        // 没给 step_order（或找不到）：追加一个 Wait 到末尾
        steps.Add(NewWaitStep(ms));
        Renumber(steps);
        return true;
    }

    private static bool TryRelaxAssert(IList<TestStep> steps, FixActionDto action, out string? error)
    {
        error = null;
        if (!TryFindStep(steps, action, out var step, out error)) return false;
        if (!TryGetString(action.Params, out var value, "expected_value", "value", "assert_value"))
        {
            error = "relax_assert 缺少 expected_value";
            return false;
        }
        if (ContainsInjectionChars(value))
        {
            error = "断言值包含非法字符，已拒绝";
            return false;
        }
        step.Config ??= new StepConfig();
        step.Config.Value = value;
        return true;
    }

    private static bool TryAddStep(IList<TestStep> steps, FixActionDto action, out string? error,
        string? baseUrl)
    {
        error = null;
        if (!TryGetString(action.Params, out var actionTypeRaw, "action_type", "action"))
        {
            error = "add_step 缺少 action_type";
            return false;
        }
        if (!Enum.TryParse<ActionType>(actionTypeRaw, ignoreCase: true, out var actionType))
        {
            error = $"add_step 的动作类型无效：{actionTypeRaw}";
            return false;
        }

        var config = new StepConfig();
        if (TryGetString(action.Params, out var url, "url") && !string.IsNullOrWhiteSpace(url))
        {
            if (!IsUrlSafe(url, baseUrl, out var urlError)) { error = urlError; return false; }
            config.Url = url.Trim();
        }
        if (TryGetString(action.Params, out var value, "value")) config.Value = value;
        if (TryGetString(action.Params, out var attribute, "attribute")) config.Attribute = attribute;
        if (TryGetString(action.Params, out var selValue, "selector_value", "locator_value") &&
            !string.IsNullOrWhiteSpace(selValue))
        {
            if (ContainsInjectionChars(selValue)) { error = "selector_value 含非法字符"; return false; }
            config.Selector = new SelectorConfig
            {
                Type = TryGetString(action.Params, out var selType, "selector_type", "locator_type") &&
                       !string.IsNullOrWhiteSpace(selType) ? selType.Trim().ToLowerInvariant() : "css",
                Value = selValue.Trim(),
                Description = TryGetString(action.Params, out var selDesc, "selector_description") ? selDesc : null,
            };
            if (config.Selector.Type is not ("css" or "xpath"))
            {
                error = $"不支持的定位类型 {config.Selector.Type}";
                return false;
            }
        }

        var newStep = new TestStep
        {
            ActionType = actionType,
            Config = config,
            AIElementDescription = TryGetString(action.Params, out var desc, "description") ? desc : null,
        };

        if (TryGetInt(action.Params, out var position, "position", "before_step_order", "step_order"))
            InsertAt(steps, position, newStep);
        else
            steps.Add(newStep);
        Renumber(steps);
        return true;
    }

    private static bool TryDeleteStep(IList<TestStep> steps, FixActionDto action, out string? error)
    {
        error = null;
        if (!TryFindStep(steps, action, out var step, out error)) return false;
        steps.Remove(step);
        Renumber(steps);
        return true;
    }

    private static bool TryReorderStep(IList<TestStep> steps, FixActionDto action, out string? error)
    {
        error = null;
        if (!TryFindStep(steps, action, out var step, out error)) return false;
        if (!TryGetInt(action.Params, out var toOrder, "to_order", "to_step_order", "target_order"))
        {
            error = "reorder_step 缺少 to_order";
            return false;
        }

        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        ordered.Remove(step);
        var index = Math.Clamp(toOrder, 0, ordered.Count);
        ordered.Insert(index, step);
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].StepOrder = i;
        return true;
    }

    // ------------------------------------------------------------------ 工具

    private static bool TryFindStep(IList<TestStep> steps, FixActionDto action, out TestStep step, out string? error)
    {
        error = null;
        step = null!;
        if (action.StepOrder is not { } order)
        {
            error = "缺少 step_order";
            return false;
        }
        var found = steps.FirstOrDefault(s => s.StepOrder == order);
        if (found is null)
        {
            error = $"找不到 StepOrder={order} 的步骤";
            return false;
        }
        step = found;
        return true;
    }

    private static void InsertAt(IList<TestStep> steps, int beforeOrder, TestStep newStep)
    {
        var index = 0;
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].StepOrder >= beforeOrder) { index = i; break; }
            index = i + 1;
        }
        steps.Insert(index, newStep);
    }

    /// <summary>结构变更后统一重排 StepOrder，保证与执行器的顺序语义、结果映射一致。</summary>
    private static void Renumber(IList<TestStep> steps)
    {
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].StepOrder = i;
    }

    private static TestStep NewWaitStep(int ms) => new()
    {
        ActionType = ActionType.Wait,
        Config = new StepConfig { Value = ms.ToString() },
    };

    /// <summary>定位器/配置值不得含标签或脚本注入字符。</summary>
    private static bool ContainsInjectionChars(string value) =>
        value.Contains('<') || value.Contains('>') || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase);

    /// <summary>URL 守卫：禁止 javascript: 之类的 scheme；给了 baseUrl 时还要求同源。</summary>
    private static bool IsUrlSafe(string url, string? baseUrl, out string? error)
    {
        error = null;
        var trimmed = url.Trim();
        if (ContainsInjectionChars(trimmed))
        {
            error = "URL 含非法字符";
            return false;
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            // 相对路径（如 /login）是合法的
            if (trimmed.StartsWith('/')) return true;
            error = $"URL 无效：{url}";
            return false;
        }
        if (uri.Scheme is not ("http" or "https"))
        {
            error = $"URL scheme 不受支持：{uri.Scheme}";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            error = $"URL 越出了被测站点（{baseUri.Host}），已拒绝";
            return false;
        }
        return true;
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object> parameters, out string value,
        params string[] keys)
    {
        value = string.Empty;
        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var raw) || raw is null) continue;
            var text = raw as string ?? raw.ToString();
            if (!string.IsNullOrEmpty(text)) { value = text; return true; }
        }
        return false;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object> parameters, out int value, params string[] keys)
    {
        value = 0;
        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var raw) || raw is null) continue;
            switch (raw)
            {
                case int i: value = i; return true;
                case long l: value = (int)l; return true;
                case double d: value = (int)d; return true;
                case float f: value = (int)f; return true;
            }
            var text = raw as string ?? raw.ToString();
            if (int.TryParse(text, out var parsed)) { value = parsed; return true; }
        }
        return false;
    }
}
