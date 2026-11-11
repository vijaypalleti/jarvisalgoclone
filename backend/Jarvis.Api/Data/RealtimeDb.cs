using System.Data;
using System.Text.Json;
using Jarvis.Api.Services;
using Microsoft.Data.SqlClient;

namespace Jarvis.Api.Data;

public record RtRow(string Topic, string Key, DateTime TimeUtc, string Payload, string RedisId);
public record RtSqlStats(string Topic, long Rows, DateTime? FirstUtc, DateTime? LastUtc);

/// <summary>
/// SQL history for every real-time topic (database JarvisMarket, same as the Delta tables).
///
///   dbo.RealtimeEvents   one row per published change: Topic, EntityKey, EventTime (UTC), Payload (JSON)
///   dbo.v*               typed views per topic (vSimPnl, vFyersOrders…) built with JSON_VALUE, so you can
///                        query real columns without knowing the JSON shape
///
/// New topics need no schema change: they land in RealtimeEvents automatically; add a view when you want columns.
/// </summary>
public class RealtimeDb(IConfiguration config)
{
    private readonly string _cs = config.GetConnectionString("MarketDb")
        ?? throw new InvalidOperationException("ConnectionStrings:MarketDb is missing");

    public string DatabaseName => new SqlConnectionStringBuilder(_cs).InitialCatalog;

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        var master = new SqlConnectionStringBuilder(_cs) { InitialCatalog = "master" }.ConnectionString;
        await using (var m = new SqlConnection(master))
        {
            await m.OpenAsync(ct);
            await Exec(m, $"IF DB_ID(N'{DatabaseName}') IS NULL CREATE DATABASE [{DatabaseName}];", ct);
        }

        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await Exec(c, """
            IF OBJECT_ID(N'dbo.RealtimeEvents') IS NULL
            BEGIN
                CREATE TABLE dbo.RealtimeEvents (
                    Id         bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RealtimeEvents PRIMARY KEY,
                    Topic      varchar(64)    NOT NULL,
                    EntityKey  nvarchar(200)  NOT NULL,
                    EventTime  datetime2(3)   NOT NULL,   -- UTC
                    Payload    nvarchar(max)  NOT NULL CONSTRAINT CK_RealtimeEvents_Json CHECK (ISJSON(Payload) = 1),
                    RedisId    varchar(40)    NOT NULL,   -- Redis stream entry id → re-delivery is idempotent
                    SavedAt    datetime2(3)   NOT NULL CONSTRAINT DF_RealtimeEvents_SavedAt DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_RealtimeEvents_Redis ON dbo.RealtimeEvents (Topic, RedisId) WITH (IGNORE_DUP_KEY = ON);
                CREATE INDEX IX_RealtimeEvents_Topic_Time ON dbo.RealtimeEvents (Topic, EventTime);
                CREATE INDEX IX_RealtimeEvents_Topic_Key_Time ON dbo.RealtimeEvents (Topic, EntityKey, EventTime);
            END
            """, ct);

        foreach (var view in Views) await Exec(c, view, ct);
    }

    // Typed views. Times are UTC; add 5:30 for IST (e.g. DATEADD(minute, 330, EventTime)).
    private static readonly string[] Views =
    [
        """
        CREATE OR ALTER VIEW dbo.vSimSpot AS
        SELECT EventTime, EntityKey AS Underlying,
               CAST(JSON_VALUE(Payload, '$.spot') AS decimal(18,2))      AS Spot,
               CAST(JSON_VALUE(Payload, '$.changePct') AS decimal(9,3))  AS ChangePct,
               JSON_VALUE(Payload, '$.scenario')                          AS Scenario
        FROM dbo.RealtimeEvents WHERE Topic = 'sim.spot'
        """,
        """
        CREATE OR ALTER VIEW dbo.vSimPnl AS
        SELECT EventTime, EntityKey AS ClientId,
               JSON_VALUE(Payload, '$.name')                                AS Name,
               JSON_VALUE(Payload, '$.broker')                              AS Broker,
               CAST(JSON_VALUE(Payload, '$.realized') AS decimal(18,2))     AS Realized,
               CAST(JSON_VALUE(Payload, '$.unrealized') AS decimal(18,2))   AS Unrealized,
               CAST(JSON_VALUE(Payload, '$.total') AS decimal(18,2))        AS Total,
               CAST(JSON_VALUE(Payload, '$.openPositions') AS int)          AS OpenPositions
        FROM dbo.RealtimeEvents WHERE Topic = 'sim.pnl'
        """,
        """
        CREATE OR ALTER VIEW dbo.vSimEvents AS
        SELECT EventTime, JSON_VALUE(Payload, '$.kind') AS Kind, JSON_VALUE(Payload, '$.text') AS Text,
               JSON_VALUE(Payload, '$.clientId') AS ClientId, JSON_VALUE(Payload, '$.symbol') AS Symbol,
               CAST(JSON_VALUE(Payload, '$.amount') AS decimal(18,2)) AS Amount
        FROM dbo.RealtimeEvents WHERE Topic = 'sim.event'
        """,
        """
        CREATE OR ALTER VIEW dbo.vFyersPnl AS
        SELECT EventTime, EntityKey AS FyersId,
               CAST(JSON_VALUE(Payload, '$.realized') AS decimal(18,2))   AS Realized,
               CAST(JSON_VALUE(Payload, '$.unrealized') AS decimal(18,2)) AS Unrealized,
               CAST(JSON_VALUE(Payload, '$.total') AS decimal(18,2))      AS Total,
               CAST(JSON_VALUE(Payload, '$.openPositions') AS int)        AS OpenPositions,
               CAST(JSON_VALUE(Payload, '$.totalPositions') AS int)       AS TotalPositions
        FROM dbo.RealtimeEvents WHERE Topic = 'fyers.pnl'
        """,
        """
        CREATE OR ALTER VIEW dbo.vFyersFunds AS
        SELECT EventTime, EntityKey AS Title,
               CAST(JSON_VALUE(Payload, '$.equity') AS decimal(18,2))    AS Equity,
               CAST(JSON_VALUE(Payload, '$.commodity') AS decimal(18,2)) AS Commodity
        FROM dbo.RealtimeEvents WHERE Topic = 'fyers.funds'
        """,
        """
        CREATE OR ALTER VIEW dbo.vFyersPositions AS
        SELECT EventTime, EntityKey AS PositionId,
               JSON_VALUE(Payload, '$.symbol') AS Symbol, JSON_VALUE(Payload, '$.productType') AS ProductType,
               CAST(JSON_VALUE(Payload, '$.netQty') AS int)               AS NetQty,
               CAST(JSON_VALUE(Payload, '$.netAvg') AS decimal(18,2))     AS NetAvg,
               CAST(JSON_VALUE(Payload, '$.ltp') AS decimal(18,2))        AS Ltp,
               CAST(JSON_VALUE(Payload, '$.realized') AS decimal(18,2))   AS Realized,
               CAST(JSON_VALUE(Payload, '$.unrealized') AS decimal(18,2)) AS Unrealized,
               CAST(JSON_VALUE(Payload, '$.pl') AS decimal(18,2))         AS Pl
        FROM dbo.RealtimeEvents WHERE Topic = 'fyers.positions'
        """,
        """
        CREATE OR ALTER VIEW dbo.vFyersOrders AS
        SELECT EventTime, EntityKey AS OrderId,
               JSON_VALUE(Payload, '$.symbol') AS Symbol, JSON_VALUE(Payload, '$.side') AS Side,
               CAST(JSON_VALUE(Payload, '$.qty') AS int) AS Qty, CAST(JSON_VALUE(Payload, '$.filledQty') AS int) AS FilledQty,
               JSON_VALUE(Payload, '$.status') AS Status,
               CAST(JSON_VALUE(Payload, '$.price') AS decimal(18,2)) AS Price,
               JSON_VALUE(Payload, '$.message') AS Message, JSON_VALUE(Payload, '$.orderTime') AS OrderTime
        FROM dbo.RealtimeEvents WHERE Topic = 'fyers.orders'
        """,
        """
        CREATE OR ALTER VIEW dbo.vFyersTrades AS
        SELECT EventTime, EntityKey AS TradeNumber,
               JSON_VALUE(Payload, '$.orderId') AS OrderId, JSON_VALUE(Payload, '$.symbol') AS Symbol,
               JSON_VALUE(Payload, '$.side') AS Side, CAST(JSON_VALUE(Payload, '$.qty') AS int) AS Qty,
               CAST(JSON_VALUE(Payload, '$.price') AS decimal(18,2)) AS Price,
               CAST(JSON_VALUE(Payload, '$.value') AS decimal(18,2)) AS Value,
               JSON_VALUE(Payload, '$.tradeTime') AS TradeTime
        FROM dbo.RealtimeEvents WHERE Topic = 'fyers.trades'
        """,
        """
        CREATE OR ALTER VIEW dbo.vFyersHoldings AS
        SELECT EventTime, EntityKey AS Symbol,
               CAST(JSON_VALUE(Payload, '$.qty') AS int)                AS Qty,
               CAST(JSON_VALUE(Payload, '$.costPrice') AS decimal(18,2)) AS CostPrice,
               CAST(JSON_VALUE(Payload, '$.ltp') AS decimal(18,2))      AS Ltp,
               CAST(JSON_VALUE(Payload, '$.marketValue') AS decimal(18,2)) AS MarketValue,
               CAST(JSON_VALUE(Payload, '$.pl') AS decimal(18,2))       AS Pl
        FROM dbo.RealtimeEvents WHERE Topic = 'fyers.holdings'
        """,
        """
        CREATE OR ALTER VIEW dbo.vFyersQuotes AS
        SELECT EventTime, EntityKey AS Symbol,
               CAST(JSON_VALUE(Payload, '$.ltp') AS decimal(18,2))       AS Ltp,
               CAST(JSON_VALUE(Payload, '$.change') AS decimal(18,2))    AS Change,
               CAST(JSON_VALUE(Payload, '$.changePct') AS decimal(9,3))  AS ChangePct,
               CAST(JSON_VALUE(Payload, '$.volume') AS bigint)           AS Volume
        FROM dbo.RealtimeEvents WHERE Topic = 'fyers.quotes'
        """,
    ];

    public async Task<int> SaveAsync(IReadOnlyList<RtRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return 0;
        var table = new DataTable();
        table.Columns.Add("Topic", typeof(string));
        table.Columns.Add("EntityKey", typeof(string));
        table.Columns.Add("EventTime", typeof(DateTime));
        table.Columns.Add("Payload", typeof(string));
        table.Columns.Add("RedisId", typeof(string));
        foreach (var r in rows) table.Rows.Add(r.Topic, r.Key.Length > 200 ? r.Key[..200] : r.Key, r.TimeUtc, r.Payload, r.RedisId);

        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        using var bulk = new SqlBulkCopy(c) { DestinationTableName = "dbo.RealtimeEvents", BatchSize = 5000 };
        foreach (DataColumn col in table.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulk.WriteToServerAsync(table, ct);
        return rows.Count;
    }

    public async Task<List<RtMessage>> HistoryAsync(string topic, string? key, DateTime? fromUtc, DateTime? toUtc, int take, CancellationToken ct)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (@take) EntityKey, EventTime, Payload FROM dbo.RealtimeEvents
            WHERE Topic = @topic AND (@key IS NULL OR EntityKey = @key)
              AND (@from IS NULL OR EventTime >= @from) AND (@to IS NULL OR EventTime <= @to)
            ORDER BY EventTime DESC, Id DESC
            """, c);
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Clamp(take, 1, 5000);
        cmd.Parameters.Add("@topic", SqlDbType.VarChar, 64).Value = topic;
        cmd.Parameters.Add("@key", SqlDbType.NVarChar, 200).Value = (object?)key ?? DBNull.Value;
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = (object?)fromUtc ?? DBNull.Value;
        cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = (object?)toUtc ?? DBNull.Value;
        var list = new List<RtMessage>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            using var doc = JsonDocument.Parse(r.GetString(2));
            list.Add(new RtMessage(topic, r.GetString(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc), doc.RootElement.Clone()));
        }
        return list;
    }

    public async Task<List<RtSqlStats>> StatsAsync(CancellationToken ct)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Topic, COUNT_BIG(*), MIN(EventTime), MAX(EventTime) FROM dbo.RealtimeEvents GROUP BY Topic", c);
        var list = new List<RtSqlStats>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new RtSqlStats(r.GetString(0), r.GetInt64(1),
                r.IsDBNull(2) ? null : DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                r.IsDBNull(3) ? null : DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)));
        return list;
    }

    /// <summary>Deletes history older than N days (in chunks so the log doesn't balloon).</summary>
    public async Task<int> PurgeAsync(int days, CancellationToken ct)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        int total = 0, n;
        do
        {
            await using var cmd = new SqlCommand("DELETE TOP (20000) FROM dbo.RealtimeEvents WHERE EventTime < DATEADD(day, -@d, SYSUTCDATETIME())", c);
            cmd.Parameters.Add("@d", SqlDbType.Int).Value = days;
            n = await cmd.ExecuteNonQueryAsync(ct);
            total += n;
        } while (n > 0 && !ct.IsCancellationRequested);
        return total;
    }

    private static async Task Exec(SqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
