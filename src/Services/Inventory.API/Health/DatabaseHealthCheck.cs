using Inventory.API.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inventory.API.Health;

/// <summary>
/// /health svarer kun "Healthy", hvis databasen kan nås. RabbitMQ-forbindelsen tjekker MassTransit selv.
/// Gatewayen og Docker kan bruge svaret til at se, om servicen er klar.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly InventoryDbContext _db;

    public DatabaseHealthCheck(InventoryDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await _db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the inventory database");
    }
}
