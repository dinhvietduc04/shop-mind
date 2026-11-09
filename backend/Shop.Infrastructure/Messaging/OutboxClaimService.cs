using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Messaging;

/// <summary>
/// B4: atomically claims a batch of Pending outbox rows for publishing and
/// resolves their fate. The claim is ONE statement
/// (UPDATE ... WHERE Id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING):
/// concurrent pollers never get the same row, Attempts++ happens at claim so a
/// publisher that crashes between delivery and marking still converges toward
/// MaxAttempts, and un-acked rows stay Pending for the next round
/// (at-least-once — consumers dedupe by eventId).
/// </summary>
public sealed class OutboxClaimService(AppDbContext db)
{
    private const string ClaimSql = """
        UPDATE "OutboxMessages" o
        SET "Attempts" = o."Attempts" + 1
        WHERE o."Id" IN (
            SELECT p."Id"
            FROM "OutboxMessages" p
            WHERE p."Status" = @pending
            ORDER BY p."OccurredAt"
            FOR UPDATE SKIP LOCKED
            LIMIT @batch)
        RETURNING o."Id", o."AggregateType", o."AggregateId", o."Type", o."Payload",
                  o."OccurredAt", o."PublishedAt", o."Attempts", o."Status";
        """;

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int batch, CancellationToken ct = default)
    {
        var enlisted = db.Database.CurrentTransaction is not null;
        if (!enlisted)
            await db.Database.OpenConnectionAsync(ct); // outside a round: manage the connection ourselves
        try
        {
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            if (enlisted)
                cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); // participate in the round's tx
            cmd.CommandText = ClaimSql;
            Add(cmd, "@batch", batch);
            Add(cmd, "@pending", (short)OutboxStatus.Pending);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = new List<OutboxMessage>();
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new OutboxMessage
                {
                    Id = reader.GetGuid(0),
                    AggregateType = reader.GetString(1),
                    AggregateId = reader.GetGuid(2),
                    Type = reader.GetString(3),
                    Payload = reader.GetString(4),
                    OccurredAt = DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                    PublishedAt = reader.IsDBNull(6)
                        ? null
                        : DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                    Attempts = reader.GetInt32(7),
                    Status = (OutboxStatus)reader.GetInt16(8),
                });
            }
            return rows;
        }
        finally
        {
            if (!enlisted)
                await db.Database.CloseConnectionAsync();
        }
    }

    public async Task MarkSentAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        await db.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Sent)
                .SetProperty(m => m.PublishedAt, DateTime.UtcNow), ct);
    }

    public async Task MarkFailedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        await db.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Failed), ct);
    }

    private static void Add(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
