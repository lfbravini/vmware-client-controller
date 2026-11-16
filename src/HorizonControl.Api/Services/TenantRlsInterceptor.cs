using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HorizonControl.Api.Services;

/// <summary>
/// Sets Postgres session variable app.current_tenant for RLS when a tenant is in scope.
/// </summary>
public sealed class TenantRlsConnectionInterceptor : DbConnectionInterceptor
{
    public static readonly AsyncLocal<Guid?> CurrentTenant = new();

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private static async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var tenant = CurrentTenant.Value;
        if (tenant is null)
        {
            return;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant', @t, false);";
        var p = cmd.CreateParameter();
        p.ParameterName = "t";
        p.Value = tenant.Value.ToString("D");
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
