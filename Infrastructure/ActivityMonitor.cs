using Npgsql;

namespace Cai.Api.Infrastructure;

// Persistent alerts are shown in the administration panel; no external messages are sent.
public sealed class ActivityMonitor(NpgsqlDataSource source, ILogger<ActivityMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var cmd = source.CreateCommand("""
                    WITH activity AS (SELECT u.id,coalesce(max(s.occurred_at) FILTER(WHERE s.status='APPROVED'),u.created_at) AS last_at
                     FROM users u LEFT JOIN mission_submissions s ON s.user_id=u.id WHERE u.role='SOLDADO_ACTIVE' GROUP BY u.id)
                    INSERT INTO activity_alerts(user_id,last_mission_at,days_inactive)
                    SELECT id,last_at,extract(day FROM now()-last_at)::int FROM activity WHERE last_at<=now()-interval '60 days'
                    ON CONFLICT(user_id) DO UPDATE SET last_mission_at=excluded.last_mission_at,days_inactive=excluded.days_inactive;
                    UPDATE users SET reserve=true WHERE role='SOLDADO_ACTIVE' AND id IN(SELECT user_id FROM activity_alerts WHERE days_inactive>=120);
                    DELETE FROM activity_alerts a WHERE EXISTS(SELECT 1 FROM mission_submissions s WHERE s.user_id=a.user_id AND s.status='APPROVED' AND s.occurred_at>now()-interval '60 days');
                    """);
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
            catch (OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e,"No se pudo revisar la inactividad."); }
        } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
