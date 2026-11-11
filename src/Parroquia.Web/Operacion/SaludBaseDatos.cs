using Microsoft.Extensions.Diagnostics.HealthChecks;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Web.Operacion;

/// <summary>Chequeo de <c>/salud</c>: la aplicación llega a la base de datos. No expone detalles.</summary>
public sealed class SaludBaseDatos(ParroquiaDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
}
