import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, NgZone, inject } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Observable } from 'rxjs';

/** One change on a real-time topic. `data` is the payload the publisher sent. */
export interface RtMessage<T = any> { topic: string; key: string; timeUtc: string; data: T; }

export interface RtTopicStatus {
  topic: string; source: string; description: string; keyMeaning: string;
  publishedThisRun: number; lastPublishUtc: string | null; latestKeys: number;
  redisStreamLength: number; pendingToSql: number; sqlRows: number; sqlFirstUtc: string | null; sqlLastUtc: string | null;
}
export interface RtStatus {
  redisConnected: boolean; sqlError: string | null;
  persister: { sqlReady: boolean; redisConnected: boolean; lastFlushUtc: string | null; rowsWrittenThisRun: number; batches: number; lastError: string | null };
  fyersPoller: { enabled: boolean; configured: boolean; state: string; lastPollUtc: string | null; lastChangeUtc: string | null; changes: number; lastError: string | null; pollSeconds: number; quoteSymbols: string };
  topics: RtTopicStatus[];
}

/**
 * Read any real-time topic (sim.*, fyers.*, future ones) without caring where it lives:
 *   latest()  → now (Redis)        recent() → last N changes (Redis stream)
 *   history() → older data (SQL)   live()   → push stream (SignalR)
 *
 * Example (in a component):
 *   rt.live<{ total: number }>('sim.pnl').subscribe(m => console.log(m.key, m.data.total));
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private http = inject(HttpClient);
  private zone = inject(NgZone);
  private hub?: HubConnection;
  private starting?: Promise<void>;
  private refs = new Map<string, number>();

  status() { return this.http.get<RtStatus>('/api/realtime'); }
  latest<T = any>(topic: string) { return this.http.get<RtMessage<T>[]>(`/api/realtime/${topic}/latest`); }
  recent<T = any>(topic: string, count = 50) { return this.http.get<RtMessage<T>[]>(`/api/realtime/${topic}/recent`, { params: { count } }); }
  history<T = any>(topic: string, q: { key?: string; from?: string; to?: string; take?: number } = {}) {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== '') p = p.set(k, String(v));
    return this.http.get<RtMessage<T>[]>(`/api/realtime/${topic}/history`, { params: p });
  }

  /** Live changes for a topic. Shares one SignalR connection; unsubscribes from the topic when the last subscriber leaves. */
  live<T = any>(topic: string): Observable<RtMessage<T>> {
    return new Observable<RtMessage<T>>(sub => {
      const handler = (m: RtMessage<T>) => { if (m.topic === topic) this.zone.run(() => sub.next(m)); };
      let closed = false;
      this.connect().then(async () => {
        if (closed) return;
        this.hub!.on('rt', handler);
        const n = (this.refs.get(topic) ?? 0) + 1;
        this.refs.set(topic, n);
        if (n === 1) await this.hub!.invoke('Subscribe', topic);
      }).catch(err => sub.error(err));

      return () => {
        closed = true;
        this.hub?.off('rt', handler);
        const n = (this.refs.get(topic) ?? 1) - 1;
        this.refs.set(topic, n);
        if (n === 0 && this.hub?.state === HubConnectionState.Connected) this.hub.invoke('Unsubscribe', topic).catch(() => {});
      };
    });
  }

  private connect(): Promise<void> {
    if (this.hub?.state === HubConnectionState.Connected) return Promise.resolve();
    if (this.starting) return this.starting;
    this.hub ??= new HubConnectionBuilder().withUrl('/hubs/market').withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    // after a reconnect the server forgot our groups → re-subscribe
    this.hub.onreconnected(() => { for (const [t, n] of this.refs) if (n > 0) this.hub!.invoke('Subscribe', t).catch(() => {}); });
    this.starting = this.hub.start().finally(() => (this.starting = undefined));
    return this.starting;
  }
}
