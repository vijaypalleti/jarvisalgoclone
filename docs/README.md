# Jarvis OMS clone: documentation

This folder documents the local clone of the Jarvis Algo OMS admin console (Angular + .NET with fake data), the live data feeds wired into it, and how to run and demo it.

## Start here

| # | Document | Read it when you want to… |
|---|---|---|
| 1 | [Getting started](01-getting-started.md) | install, run, and open the app |
| 2 | [Architecture](02-architecture.md) | understand how the pieces fit together |
| 3 | [Console pages](03-console-pages.md) | know what each cloned tab shows and where its code lives |
| 4 | [Live Signals & Lifecycle](04-live-signals.md) | understand the signal list, the detail modal and the "exit partial" handling |
| 5 | [Broker Live (Fyers)](05-broker-live-fyers.md) | connect a real Fyers account (read-only) |
| 6 | [Crypto Live (Delta Exchange)](06-crypto-live-delta.md) | see the live BTC feed and how it streams |
| 7 | [Storage: Redis → SQL Server](07-storage-redis-sql.md) | set up and inspect Redis and the SQL database |
| 8 | [Demo simulator](08-demo-simulator.md) | make the fake data move (P&L, positions, exits) for demos |
| 9 | [Troubleshooting](09-troubleshooting.md) | fix a known error |
| 10 | [API reference](10-api-reference.md) | look up an endpoint |
| 11 | [Realtime pipeline](11-realtime-pipeline.md) | store live data (simulator, Fyers, your own) in Redis → SQL and build features on it |
| 12 | [AI assistant](12-ai-assistant.md) | ask questions about live data in a floating chat (Groq GPT-OSS 20B + tool calling) |
| 13 | [Assistant question catalogue](13-assistant-questions.md) | see every kind of question the chat can answer, with examples |
| SQL | [sql/useful-queries.sql](sql/useful-queries.sql) | ready-to-run queries for the data in SQL Server (open in SSMS) |
| – | [Changelog](CHANGELOG.md) | see what was built, in order |

## Keeping these docs up to date

When you change something:

1. Add a line to [CHANGELOG.md](CHANGELOG.md) under today's date: what changed and why.
2. Update the guide for that area. If the change adds a new area, add a numbered file and link it in the table above.
3. If you add or change an endpoint, update [10-api-reference.md](10-api-reference.md).
4. If you hit an error and fix it, add it to [09-troubleshooting.md](09-troubleshooting.md) as a symptom, its cause, and the fix.

Never put secrets in these files: no Fyers tokens, API keys or passwords. Credentials go in .NET user-secrets (see [05](05-broker-live-fyers.md)).
