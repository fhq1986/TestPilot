namespace AI.TestPlatform.Domain.Entities;

// API 定义（Swagger/OpenAPI 导入，Spec 为 OpenAPI JSON 原文）
public class ApiDefinition
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string Spec { get; set; } = string.Empty;
    public int EndpointCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
