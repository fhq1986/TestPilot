using System.Text.Json;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.AI;

public class DiagnosisService
{
    private readonly TestDbContext _db;
    private readonly AIClient _aiClient;
    private readonly ILogger<DiagnosisService> _logger;

    public DiagnosisService(TestDbContext db, AIClient aiClient, ILogger<DiagnosisService> logger)
    {
        _db = db;
        _aiClient = aiClient;
        _logger = logger;
    }

    public async Task<bool> DiagnoseAsync(Guid executionId, CancellationToken ct)
    {
        var execution = await _db.Executions.AsNoTracking()
            .Include(e => e.Results)
            .Include(e => e.TestCase)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
        if (execution is null || execution.Status is not (ExecutionStatus.Failed or ExecutionStatus.Error))
            return false;

        var failed = execution.Results
            .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
            .OrderBy(r => r.StepOrder)
            .Take(20)
            .ToList();
        if (failed.Count == 0)
            return false;

        var evidence = failed.Select(r => new FailedStepEvidence(
            r.StepOrder,
            r.StepSnapshot is null ? "未知" : JsonSerializer.Serialize(r.StepSnapshot),
            r.ErrorMessage is null ? null : r.ErrorMessage[..Math.Min(300, r.ErrorMessage.Length)],
            r.Log,
            r.StepSnapshot)).ToList();

        var similar = new List<SimilarCaseEvidence>();
        foreach (var f in failed)
        {
            if (string.IsNullOrWhiteSpace(f.ErrorMessage)) continue;
            var prefix = f.ErrorMessage[..Math.Min(80, f.ErrorMessage.Length)];
            similar = await _db.ExecutionResults.AsNoTracking()
                .Where(r => r.Status != ExecutionStatus.Passed && r.ExecutionId != executionId && r.ErrorMessage != null && r.ErrorMessage.StartsWith(prefix))
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
                .Select(r => new SimilarCaseEvidence(r.StepOrder, "历史", r.ErrorMessage!))
                .ToListAsync(ct);
            if (similar.Count > 0) break;
        }

        var testCaseSummary = new Dictionary<string, object?>
        {
            ["name"] = execution.TestCase?.Name,
            ["type"] = execution.TestCase?.Type.ToString(),
            ["browser"] = execution.BrowserVersion,
            ["baseUrl"] = execution.TestCase?.BaseUrl,
        };

        try
        {
            var result = await _aiClient.DiagnoseAsync(testCaseSummary, evidence, similar, ct);
            var tracked = await _db.Executions.FirstOrDefaultAsync(e => e.Id == executionId, ct);
            if (tracked is null) return false;
            tracked.AIDiagnosis = $"{result.Category}: {result.RootCause}";
            tracked.AISuggestedFix = result.SuggestedFix;
            tracked.DiagnosisConfidence = result.Confidence;
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (AIWorkerException ex)
        {
            _logger.LogWarning("执行 {ExecutionId} 诊断失败: {Message}", executionId, ex.Message);
            return false;
        }
    }
}
