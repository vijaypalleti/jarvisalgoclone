import { HttpClient } from '@angular/common/http';
import { Injectable, NgZone, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';

export interface DeltaTicker {
  symbol: string; last: number; mark: number; spot: number; open: number; high: number; low: number;
  changePct: number; volume: number; turnoverUsd: number; oi: number; oiUsd: number; fundingRate: number;
  bestBid: number; bestAsk: number; bidSize: number; askSize: number; contractValue: number; time: number;
}
export interface DeltaTrade { price: number; size: number; side: 'buy' | 'sell'; time: number; }
export interface BookLevel { price: number; size: number; total: number; }
export interface DeltaOrderBook { symbol: string; bids: BookLevel[]; asks: BookLevel[]; spread: number; time: number; }
export interface DeltaFeedStatus {
  connected: boolean; symbol: string; connectedAt: string | null; lastMessageAt: string | null; reconnects: number; lastError: string | null;
}
export interface DeltaSnapshot { status: DeltaFeedStatus; ticker: DeltaTicker | null; orderBook: DeltaOrderBook | null; trades: DeltaTrade[]; }
export interface Candle { time: number; open: number; high: number; low: number; close: number; volume: number; }
export interface CandleResponse {
  symbol: string; resolution: string; intervalSeconds: number; candles: Candle[];
  fromSql: number; fromDelta: number; recordingSinceUtc: string | null;
}
export interface StorageStatus {
  symbol: string;
  redis: { connected: boolean; endpoint: string; streamKey: string; streamLength: number; pendingToSql: number };
  sql: {
    ready: boolean; server: string; database: string; error: string | null;
    stats: { trades: number; candles1m: number; tickerSnapshots: number; firstTradeUtc: string | null; lastTradeUtc: string | null } | null;
  };
  persister: { lastFlushUtc: string | null; tradesWrittenThisRun: number; batches: number; lastSnapshotUtc: string | null; lastError: string | null };
}

/**
 * Live Delta Exchange market data (public, no login) pushed from the .NET API over SignalR.
 * Pages call connect() on init and disconnect() on destroy (ref-counted).
 */
@Injectable({ providedIn: 'root' })
export class DeltaMarketService {
  private http = inject(HttpClient);
  private zone = inject(NgZone);
  private hub?: HubConnection;
  private refs = 0;

  /** browser ↔ our API (SignalR) */
  readonly linkState = signal<'connecting' | 'connected' | 'reconnecting' | 'disconnected'>('disconnected');
  /** our API ↔ Delta WebSocket */
  readonly feed = signal<DeltaFeedStatus | null>(null);
  readonly ticker = signal<DeltaTicker | null>(null);
  readonly book = signal<DeltaOrderBook | null>(null);
  readonly trades = signal<DeltaTrade[]>([]);
  readonly lastUpdate = signal<number | null>(null);
  /** every trade, for live candle building */
  readonly trade$ = new Subject<DeltaTrade>();

  candles(resolution: string, count = 300) {
    return this.http.get<CandleResponse>('/api/delta/candles', { params: { resolution, count } });
  }

  /** Redis → SQL Server pipeline health */
  storageStatus() { return this.http.get<StorageStatus>('/api/market/status'); }

  async connect() {
    if (++this.refs > 1) return;
    this.hub = new HubConnectionBuilder()
      .withUrl('/hubs/market')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    const run = (fn: () => void) => this.zone.run(fn);
    this.hub.on('ticker', (t: DeltaTicker) => run(() => { this.ticker.set(t); this.lastUpdate.set(Date.now()); }));
    this.hub.on('orderbook', (b: DeltaOrderBook) => run(() => { this.book.set(b); this.lastUpdate.set(Date.now()); }));
    this.hub.on('trade', (t: DeltaTrade) => run(() => {
      this.trades.update(list => [t, ...list].slice(0, 60));
      this.lastUpdate.set(Date.now());
      this.trade$.next(t);
    }));
    this.hub.on('status', (s: DeltaFeedStatus) => run(() => this.feed.set(s)));
    this.hub.onreconnecting(() => run(() => this.linkState.set('reconnecting')));
    this.hub.onreconnected(() => run(() => { this.linkState.set('connected'); this.loadSnapshot(); }));
    this.hub.onclose(() => run(() => this.linkState.set('disconnected')));

    await this.start();
  }

  private async start() {
    if (!this.hub || this.refs === 0) return;
    this.linkState.set('connecting');
    try {
      await this.hub.start();
      this.linkState.set('connected');
      await this.loadSnapshot();
    } catch {
      this.linkState.set('disconnected');
      setTimeout(() => this.start(), 3000); // API not up yet — keep trying
    }
  }

  private async loadSnapshot() {
    if (this.hub?.state !== HubConnectionState.Connected) return;
    const s = await this.hub.invoke<DeltaSnapshot>('Snapshot');
    this.zone.run(() => {
      this.feed.set(s.status);
      if (s.ticker) this.ticker.set(s.ticker);
      if (s.orderBook) this.book.set(s.orderBook);
      this.trades.set(s.trades ?? []);
      this.lastUpdate.set(Date.now());
    });
  }

  async disconnect() {
    if (--this.refs > 0) return;
    this.refs = 0;
    const hub = this.hub;
    this.hub = undefined;
    await hub?.stop();
    this.linkState.set('disconnected');
  }
}
