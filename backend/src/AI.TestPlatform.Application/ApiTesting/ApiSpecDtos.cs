using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.ApiTesting;

public record ApiSchemaSpec(
    string? Type, string? Format, bool Required,
    decimal? Minimum, decimal? Maximum, bool ExclusiveMinimum, bool ExclusiveMaximum,
    int? MinLength, int? MaxLength,
    List<string>? Enum, ApiSchemaSpec? Items,
    Dictionary<string, ApiSchemaSpec>? Properties);

public record ApiParameterSpec(string Name, string In, bool Required, ApiSchemaSpec Schema);

public record ApiEndpointSpec(
    string Method, string Path, List<ApiParameterSpec> Parameters,
    ApiSchemaSpec? RequestBody, List<int> ResponseCodes);

public record GeneratedApiCase(
    string Name, string Priority, string Description,
    List<CreateTestStepRequest> Steps);
