using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class AIModelVersionRepository : IAIModelVersionRepository
{
    private readonly TradingDbContext _context;
    private readonly ILogger<AIModelVersionRepository> _logger;

    public AIModelVersionRepository(TradingDbContext context, ILogger<AIModelVersionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<AIModelVersion> CreateAsync(AIModelVersion modelVersion, CancellationToken cancellationToken = default)
    {
        _context.AIModelVersions.Add(modelVersion);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created AI model version {Version} of type {Type}", 
            modelVersion.Version, modelVersion.ModelType);
        return modelVersion;
    }

    public async Task<AIModelVersion?> GetByVersionAsync(
        string version,
        string modelType,
        CancellationToken cancellationToken = default)
    {
        return await _context.AIModelVersions
            .FirstOrDefaultAsync(m => m.Version == version && m.ModelType == modelType, cancellationToken);
    }

    public async Task<AIModelVersion?> GetActiveModelAsync(
        string modelType,
        CancellationToken cancellationToken = default)
    {
        return await _context.AIModelVersions
            .Where(m => m.ModelType == modelType && m.IsActive && m.Status == "PRODUCTION")
            .OrderByDescending(m => m.ActivatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<AIModelVersion>> GetAllVersionsAsync(
        string modelType,
        CancellationToken cancellationToken = default)
    {
        return await _context.AIModelVersions
            .Where(m => m.ModelType == modelType)
            .OrderByDescending(m => m.TrainingDate)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(AIModelVersion modelVersion, CancellationToken cancellationToken = default)
    {
        _context.AIModelVersions.Update(modelVersion);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated AI model version {Version}", modelVersion.Version);
    }

    public async Task ActivateModelAsync(int id, CancellationToken cancellationToken = default)
    {
        var model = await _context.AIModelVersions.FindAsync(new object[] { id }, cancellationToken);
        if (model == null) return;

        // Deactivate all other models of same type
        await DeactivateAllAsync(model.ModelType, cancellationToken);

        // Activate this model
        model.IsActive = true;
        model.Status = "PRODUCTION";
        model.ActivatedAt = DateTimeOffset.UtcNow;
        
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Activated AI model version {Version}", model.Version);
    }

    public async Task DeactivateAllAsync(string modelType, CancellationToken cancellationToken = default)
    {
        var activeModels = await _context.AIModelVersions
            .Where(m => m.ModelType == modelType && m.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var model in activeModels)
        {
            model.IsActive = false;
            if (model.Status == "PRODUCTION")
                model.Status = "DEPRECATED";
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}