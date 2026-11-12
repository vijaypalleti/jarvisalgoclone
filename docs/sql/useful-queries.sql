-- Jarvis demo: handy queries for SQL Server  (server: localhost\SQLEXPRESS · database: JarvisMarket · Windows auth)
-- Run one block at a time (select it and press Ctrl+Shift+E in VS Code / F5 in SSMS).
-- Times: EventTime / SavedAt are UTC; add 5:30 for IST (see the IST examples).
USE JarvisMarket;

-- ===================================================================== 0. What is in the database?
SELECT Topic, COUNT(*) AS Rows, MIN(SavedAt) AS FirstSaved, MAX(SavedAt) AS LastSaved
FROM dbo.RealtimeEvents GROUP BY Topic ORDER BY Rows DESC;

-- ===================================================================== 1. BTC (Delta Exchange, real)
-- last 30 one-minute candles
SELECT TOP 30 OpenTime, DATEADD(MINUTE, 330, OpenTime) AS OpenTimeIst, [Open], High, Low, [Close], Volume, TradeCount
FROM dbo.DeltaCandles1m ORDER BY OpenTime DESC;

-- latest 50 trades (ORDER BY Id uses the primary key: instant; ORDER BY TradeTime scans 200k+ rows)
SELECT TOP 50 TradeTime, Price, Size, Side FROM dbo.DeltaTrades ORDER BY Id DESC;

-- ticker snapshots (every 60 s): price, funding, open interest
SELECT TOP 20 SnapshotTime, LastPrice, MarkPrice, ChangePct24h, FundingRate, OpenInterestUsd, BestBid, BestAsk
FROM dbo.DeltaTickerSnapshots ORDER BY SnapshotTime DESC;

-- hourly high / low / close for today
SELECT DATEADD(HOUR, DATEDIFF(HOUR, 0, OpenTime), 0) AS HourUtc, MAX(High) AS High, MIN(Low) AS Low, SUM(Volume) AS Volume
FROM dbo.DeltaCandles1m WHERE OpenTime >= CAST(SYSUTCDATETIME() AS date)
GROUP BY DATEADD(HOUR, DATEDIFF(HOUR, 0, OpenTime), 0) ORDER BY HourUtc;

-- ===================================================================== 2. Vijay's real Fyers account (only while logged in)
SELECT TOP 20 * FROM dbo.vFyersPnl       ORDER BY EventTime DESC;   -- realized / unrealized / total
SELECT TOP 20 * FROM dbo.vFyersFunds     ORDER BY EventTime DESC;
SELECT TOP 50 * FROM dbo.vFyersPositions ORDER BY EventTime DESC;
SELECT TOP 50 * FROM dbo.vFyersOrders    ORDER BY EventTime DESC;
SELECT TOP 30 * FROM dbo.vFyersQuotes    ORDER BY EventTime DESC;   -- NIFTY 50, BANK NIFTY, SENSEX

-- ===================================================================== 3. Demo simulator (fake OMS book)
-- latest P&L per client
SELECT p.* FROM dbo.vSimPnl p
JOIN (SELECT ClientId, MAX(EventTime) AS T FROM dbo.vSimPnl GROUP BY ClientId) last
  ON last.ClientId = p.ClientId AND last.T = p.EventTime
ORDER BY p.Total;

-- one client's P&L over the last hour (change the id)
SELECT EventTime, DATEADD(MINUTE, 330, EventTime) AS TimeIst, Realized, Unrealized, Total, OpenPositions
FROM dbo.vSimPnl WHERE ClientId = '1023' AND EventTime >= DATEADD(HOUR, -1, SYSUTCDATETIME()) ORDER BY EventTime;

-- simulated NIFTY spot and scenario
SELECT TOP 30 * FROM dbo.vSimSpot ORDER BY EventTime DESC;

-- simulator events (fills, exits, rejections, container changes)
SELECT TOP 50 * FROM dbo.vSimEvents ORDER BY EventTime DESC;
SELECT Kind, COUNT(*) AS Events FROM dbo.vSimEvents
WHERE EventTime >= DATEADD(HOUR, -1, SYSUTCDATETIME()) GROUP BY Kind ORDER BY Events DESC;   -- last hour

-- ===================================================================== 4. AI assistant log
SELECT TOP 30 EventTime, Payload FROM dbo.RealtimeEvents WHERE Topic = 'ai.chat' ORDER BY EventTime DESC;

-- ===================================================================== 5. Raw events (any topic, JSON payload)
SELECT TOP 20 Topic, EntityKey, EventTime, Payload FROM dbo.RealtimeEvents ORDER BY Id DESC;
