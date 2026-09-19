namespace AI.TestPlatform.Api.Execution;

public class StepExecutionException : Exception
{
    public StepExecutionException(string message) : base(message) { }
}

/// <summary>
/// 选择器未能定位到元素（区别于断言内容不匹配）。
/// 抛出此异常会触发 AI 自愈链：缓存 → LLM 定位 → 重试。
/// </summary>
public class LocatorNotResolvedException : StepExecutionException
{
    public LocatorNotResolvedException(string message) : base(message) { }
}
