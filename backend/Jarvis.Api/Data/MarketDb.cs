using System.Data;
using Jarvis.Api.Services;
using Microsoft.Data.SqlClient;

namespace Jarvis.Api.Data;

public record StoredTrade(string RedisId, string Symbol, long TradeTimeUs, decimal Price, decimal Size, string Side);
public record CandleRow(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
public record MarketDbStats(long Trades, long Candles1m, long TickerSnapshots, DateTime? FirstTradeUtc, DateTime? LastTradeUtc);

/// <summary>
/// SQL Server persistence for Delta market data (database: JarvisMarket).
/// Creates the database and tables on first use. All times are stored in UTC.
/// </summary>
public class MarketDb(IConfiguration config, ILogger<MarketDb> log)
{
    private readonly string _cs = config.GetConnectionString("MarketDb")
        ?? throw new InvalidOperationException("ConnectionStrings:MarketDb is missing");

    public string DatabaseName => new SqlConnectionStringBuilder(_cs).InitialCatalog;
    public string Server => new SqlConnectionStringBuilder(_cs).DataSource;

    private SqlConnection Open() { var c = new SqlConnection(_cs); c.Open(); return c; }

    // ------------------------------------------------------------------ schema

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        var master = new SqlConnectionStringBuilder(_cs) { InitialCatalog = "master" }.ConnectionString;
        await using (var c = new SqlConnection(master))
        {
            await c.OpenAsync(ct);
            await Exec(c, $"IF DB_ID(N'{DatabaseName}') IS NULL CREATE DATABASE [{DatabaseName}];", ct);
        }

        await using var db = new SqlConnection(_cs);
        await db.OpenAsync(ct);
        await Exec(db, """
            IF OBJECT_ID(N'dbo.DeltaTrades') IS NULL
            BEGIN
                CREATE TABLE dbo.DeltaTrades (
                    Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeltaTrades PRIMARY KEY,
                    Symbol       varchar(20)   NOT NULL,
                    TradeTimeUs  bigint        NOT NULL,   -- Delta timestamp, microseconds since epoch (UTC)
                    TradeTime    datetime2(3)  NOT NULL,   -- UTC
                    Price        decimal(18,2) NOT NULL,
                    Size         decimal(18,4) NOT NULL,   -- contracts (BTCUSD: 1 contract = 0.001 BTC)
                    Side         varchar(4)    NOT NULL,   -- buy / sell (aggressor)
                    RedisId      varchar(40)   NOT NULL,   -- Redis stream entry id, makes re-delivery idempotent
                    InsertedAt   datetime2(3)  NOT NULL CONSTRAINT DF_DeltaTrades_InsertedAt DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_DeltaTrades_RedisId ON dbo.DeltaTrades (Symbol, RedisId) WITH (IGNORE_DUP_KEY = ON);
                CREATE INDEX IX_DeltaTrades_Time ON dbo.DeltaTrades (Symbol, TradeTime) INCLUDE (TradeTimeUs, Price, Size, Side);
            END;

            IF OBJECT_ID(N'dbo.DeltaCandles1m') IS NULL
                CREATE TABLE dbo.DeltaCandles1m (
                    Symbol      varchar(20)   NOT NULL,
                    OpenTime    datetime2(0)  NOT NULL,    -- UTC minute
                    [Open]      decimal(18,2) NOT NULL,
                    High        decimal(18,2) NOT NULL,
                    Low         decimal(18,2) NOT NULL,
                    [Close]     decimal(18,2) NOT NULL,
                    Volume      decimal(18,4) NOT NULL,
                    TradeCount  int           NOT NULL,
                    UpdatedAt   datetime2(3)  NOT NULL CONSTRAINT DF_DeltaCandles1m_UpdatedAt DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT PK_DeltaCandles1m PRIMARY KEY (Symbol, OpenTime)
                );

            IF OBJECT_ID(N'dbo.DeltaTickerSnapshots') IS NULL
            BEGIN
                CREATE TABLE dbo.DeltaTickerSnapshots (
                    Id               bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeltaTickerSnapshots PRIMARY KEY,
                    Symbol           varchar(20)    NOT NULL,
                    SnapshotTime     datetime2(0)   NOT NULL,   -- UTC
                    LastPrice        decimal(18,2)  NOT NULL,
                    MarkPrice        decimal(18,4)  NOT NULL,
                    SpotPrice        decimal(18,4)  NOT NULL,
                    ChangePct24h     decimal(9,4)   NOT NULL,
                    High24h          decimal(18,2)  NOT NULL,
                    Low24h           decimal(18,2)  NOT NULL,
                    Volume24h        decimal(18,4)  NOT NULL,
                    TurnoverUsd24h   decimal(20,2)  NOT NULL,
                    OpenInterest     decimal(18,4)  NOT NULL,
                    OpenInterestUsd  decimal(20,2)  NOT NULL,
                    FundingRate      decimal(18,10) NOT NULL,
                    BestBid          decimal(18,2)  NOT NULL,
                    BestAsk          decimal(18,2)  NOT NULL
                );
                CREATE INDEX IX_DeltaTickerSnapshots_Time ON dbo.DeltaTickerSnapshots (Symbol, SnapshotTime);
            END;
            """, ct);
        log.LogInformation("SQL Server schema ready ({Server} / {Db})", Server, DatabaseName);
    }

    // ------------------------------------------------------------------ writes

    /// <summary>Bulk-inserts trades (duplicates silently ignored), then rebuilds the affected 1-minute candles.</summary>
    public async Task<int> SaveTradesAsync(IReadOnlyList<StoredTrade> trades, CancellationToken ct)
    {
        if (trades.Count == 0) return 0;

        var table = new DataTable();
        table.Columns.Add("Symbol", typeof(string));
        table.Columns.Add("TradeTimeUs", typeof(long));
        table.Columns.Add("TradeTime", typeof(DateTime));
        table.Columns.Add("Price", typeof(decimal));
        table.Columns.Add("Size", typeof(decimal));
        table.Columns.Add("Side", typeof(string));
        table.Columns.Add("RedisId", typeof(string));
        foreach (var t in trades)
            table.Rows.Add(t.Symbol, t.TradeTimeUs, UsToUtc(t.TradeTimeUs), t.Price, t.Size, t.Side, t.RedisId);

        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);

        using (var bulk = new SqlBulkCopy(c, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "dbo.DeltaTrades", BulkCopyTimeout = 60 })
        {
            foreach (DataColumn col in table.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }

        // Recompute every minute touched by this batch from the stored trades (idempotent).
        foreach (var group in trades.GroupBy(t => t.Symbol))
        {
            var from = FloorMinute(UsToUtc(group.Min(t => t.TradeTimeUs)));
            var to = FloorMinute(UsToUtc(group.Max(t => t.TradeTimeUs))).AddMinutes(1);
            await using var cmd = new SqlCommand(RebuildCandlesSql, c, tx);
            cmd.Parameters.Add("@sym", SqlDbType.VarChar, 20).Value = group.Key;
            cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = from;
            cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = to;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return trades.Count;
    }

    private const string RebuildCandlesSql = """
        WITH x AS (
            SELECT Symbol,
                   DATEADD(minute, DATEDIFF(minute, 0, TradeTime), 0) AS OpenTime,
                   Price, Size, TradeTimeUs, Id
            FROM dbo.DeltaTrades
            WHERE Symbol = @sym AND TradeTime >= @from AND TradeTime < @to
        ), agg AS (
            SELECT DISTINCT Symbol, OpenTime,
                   FIRST_VALUE(Price) OVER (PARTITION BY OpenTime ORDER BY TradeTimeUs, Id) AS [Open],
                   LAST_VALUE(Price)  OVER (PARTITION BY OpenTime ORDER BY TradeTimeUs, Id
                                            ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS [Close],
                   MAX(Price)  OVER (PARTITION BY OpenTime) AS High,
                   MIN(Price)  OVER (PARTITION BY OpenTime) AS Low,
                   SUM(Size)   OVER (PARTITION BY OpenTime) AS Volume,
                   COUNT(*)    OVER (PARTITION BY OpenTime) AS TradeCount
            FROM x
        )
        MERGE dbo.DeltaCandles1m AS t
        USING agg AS s ON t.Symbol = s.Symbol AND t.OpenTime = s.OpenTime
        WHEN MATCHED THEN UPDATE SET [Open] = s.[Open], High = s.High, Low = s.Low, [Close] = s.[Close],
                                     Volume = s.Volume, TradeCount = s.TradeCount, UpdatedAt = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN INSERT (Symbol, OpenTime, [Open], High, Low, [Close], Volume, TradeCount)
                              VALUES (s.Symbol, s.OpenTime, s.[Open], s.High, s.Low, s.[Close], s.Volume, s.TradeCount);
        """;

    public async Task SaveTickerSnapshotAsync(DeltaTicker t, CancellationToken ct)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            INSERT dbo.DeltaTickerSnapshots (Symbol, SnapshotTime, LastPrice, MarkPrice, SpotPrice, ChangePct24h, High24h, Low24h,
                                             Volume24h, TurnoverUsd24h, OpenInterest, OpenInterestUsd, FundingRate, BestBid, BestAsk)
            VALUES (@sym, @time, @last, @mark, @spot, @chg, @high, @low, @vol, @turn, @oi, @oiUsd, @fund, @bid, @ask);
            """, c);
        cmd.Parameters.AddWithValue("@sym", t.Symbol);
        cmd.Parameters.AddWithValue("@time", DateTimeOffset.FromUnixTimeMilliseconds(t.Time).UtcDateTime);
        cmd.Parameters.AddWithValue("@last", t.Last);
        cmd.Parameters.AddWithValue("@mark", t.Mark);
        cmd.Parameters.AddWithValue("@spot", t.Spot);
        cmd.Parameters.AddWithValue("@chg", t.ChangePct);
        cmd.Parameters.AddWithValue("@high", t.High);
        cmd.Parameters.AddWithValue("@low", t.Low);
        cmd.Parameters.AddWithValue("@vol", t.Volume);
        cmd.Parameters.AddWithValue("@turn", t.TurnoverUsd);
        cmd.Parameters.AddWithValue("@oi", t.Oi);
        cmd.Parameters.AddWithValue("@oiUsd", t.OiUsd);
        cmd.Parameters.AddWithValue("@fund", t.FundingRate);
        cmd.Parameters.AddWithValue("@bid", t.BestBid);
        cmd.Parameters.AddWithValue("@ask", t.BestAsk);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ------------------------------------------------------------------ reads

    /// <summary>Candles of any size (multiple of 1 minute) aggregated from the stored 1-minute candles. Time = bucket start, unix seconds.</summary>
    public async Task<List<CandleRow>> GetCandlesAsync(string symbol, int intervalSeconds, DateTime fromUtc, CancellationToken ct)
    {
        var minutes = Math.Max(1, intervalSeconds / 60);
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            WITH m AS (
                SELECT OpenTime, [Open], High, Low, [Close], Volume,
                       DATEADD(minute, (DATEDIFF(minute, '1970-01-01', OpenTime) / @m) * @m, '1970-01-01') AS Bucket
                FROM dbo.DeltaCandles1m
                WHERE Symbol = @sym AND OpenTime >= @from
            )
            SELECT DISTINCT
                   DATEDIFF_BIG(second, '1970-01-01', Bucket) AS T,
                   FIRST_VALUE([Open]) OVER (PARTITION BY Bucket ORDER BY OpenTime) AS O,
                   MAX(High) OVER (PARTITION BY Bucket) AS H,
                   MIN(Low)  OVER (PARTITION BY Bucket) AS L,
                   LAST_VALUE([Close]) OVER (PARTITION BY Bucket ORDER BY OpenTime
                                             ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS C,
                   SUM(Volume) OVER (PARTITION BY Bucket) AS V
            FROM m
            ORDER BY T;
            """, c);
        cmd.Parameters.Add("@sym", SqlDbType.VarChar, 20).Value = symbol;
        cmd.Parameters.Add("@m", SqlDbType.Int).Value = minutes;
        cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = fromUtc;
        var list = new List<CandleRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CandleRow(r.GetInt64(0), r.GetDecimal(1), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5)));
        return list;
    }

    /// <summary>When recording started = the first stored trade (the candle for that bucket is partial).</summary>
    public async Task<DateTime?> FirstTradeUtcAsync(string symbol, CancellationToken ct)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT MIN(TradeTime) FROM dbo.DeltaTrades WHERE Symbol = @sym", c);
        cmd.Parameters.Add("@sym", SqlDbType.VarChar, 20).Value = symbol;
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }

    public async Task<List<object>> RecentTradesAsync(string symbol, int take, CancellationToken ct)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (@take) TradeTime, Price, Size, Side, RedisId, InsertedAt
            FROM dbo.DeltaTrades WHERE Symbol = @sym ORDER BY TradeTime DESC, Id DESC;
            """, c);
        cmd.Parameters.Add("@sym", SqlDbType.VarChar, 20).Value = symbol;
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Clamp(take, 1, 1000);
        var list = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new
            {
                tradeTimeUtc = DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc), price = r.GetDecimal(1), size = r.GetDecimal(2),
                side = r.GetString(3), redisId = r.GetString(4), insertedAtUtc = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc)
            });
        return list;
    }

    public async Task<MarketDbStats> StatsAsync(string symbol, CancellationToken ct)
    {
        await using var c = new SqlConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT (SELECT COUNT_BIG(*) FROM dbo.DeltaTrades WHERE Symbol = @sym),
                   (SELECT COUNT_BIG(*) FROM dbo.DeltaCandles1m WHERE Symbol = @sym),
                   (SELECT COUNT_BIG(*) FROM dbo.DeltaTickerSnapshots WHERE Symbol = @sym),
                   (SELECT MIN(TradeTime) FROM dbo.DeltaTrades WHERE Symbol = @sym),
                   (SELECT MAX(TradeTime) FROM dbo.DeltaTrades WHERE Symbol = @sym);
            """, c);
        cmd.Parameters.Add("@sym", SqlDbType.VarChar, 20).Value = symbol;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        DateTime? Utc(int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
        return new MarketDbStats(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), Utc(3), Utc(4));
    }

    // ------------------------------------------------------------------ helpers

    private static async Task Exec(SqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static DateTime UsToUtc(long us) => DateTime.UnixEpoch.AddTicks(us * 10);
    private static DateTime FloorMinute(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, DateTimeKind.Utc);
}
