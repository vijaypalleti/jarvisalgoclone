import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Subscription, interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { RealtimeService, RtMessage, RtStatus } from '../../core/realtime.service';

/** Developer view of the real-time pipeline: every topic's flow (published → Redis → SQL), latest values and a live tail. */
@Component({
  selector: 'app-realtime-page',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe],
  templateUrl: './realtime-page.component.html',
  styleUrl: './realtime-page.component.scss',
})
export class RealtimePageComponent implements OnInit {
  private rt = inject(RealtimeService);
  private destroy = inject(DestroyRef);

  status = signal<RtStatus | null>(null);
  topic = signal('sim.pnl');
  view = signal<'latest' | 'live' | 'history'>('latest');
  latest = signal<RtMessage[]>([]);
  feed = signal<RtMessage[]>([]);
  history = signal<RtMessage[]>([]);
  historyKey = '';
  liveSub?: Subscription;

  current = computed(() => this.status()?.topics.find(t => t.topic === this.topic()));
  /** Column names = union of payload fields (payloads are flat objects). */
  columns = computed(() => {
    const rows = this.view() === 'latest' ? this.latest() : this.view() === 'live' ? this.feed() : this.history();
    const set = new Set<string>();
    for (const r of rows.slice(0, 50)) if (r.data && typeof r.data === 'object') Object.keys(r.data).forEach(k => set.add(k));
    return [...set];
  });

  ngOnInit() {
    this.loadStatus();
    interval(3000).pipe(takeUntilDestroyed(this.destroy)).subscribe(() => {
      this.loadStatus();
      if (this.view() === 'latest') this.loadLatest();
    });
    this.select(this.topic());
    this.destroy.onDestroy(() => this.liveSub?.unsubscribe());
  }

  loadStatus() { this.rt.status().subscribe({ next: s => this.status.set(s), error: () => this.status.set(null) }); }
  loadLatest() { this.rt.latest(this.topic()).subscribe(x => this.latest.set(x)); }
  loadHistory() { this.rt.history(this.topic(), { key: this.historyKey.trim() || undefined, take: 200 }).subscribe(x => this.history.set(x)); }

  select(topic: string) {
    this.topic.set(topic);
    this.latest.set([]); this.feed.set([]); this.history.set([]);
    this.liveSub?.unsubscribe();
    this.loadLatest();
    // live tail always on for the selected topic (newest first, max 100)
    this.liveSub = this.rt.live(topic).subscribe(m => this.feed.update(f => [m, ...f].slice(0, 100)));
    if (this.view() === 'history') this.loadHistory();
  }

  setView(v: 'latest' | 'live' | 'history') {
    this.view.set(v);
    if (v === 'history') this.loadHistory();
    if (v === 'latest') this.loadLatest();
  }

  isNum(v: unknown) { return typeof v === 'number'; }
  tone(col: string, v: unknown) {
    if (typeof v !== 'number' || !/total|pl|realized|unrealized|change|amount/i.test(col)) return '';
    return v > 0 ? 'pos' : v < 0 ? 'neg' : '';
  }
  sqlFor(topic: string) {
    const view = ({
      'sim.spot': 'vSimSpot', 'sim.pnl': 'vSimPnl', 'sim.event': 'vSimEvents', 'fyers.pnl': 'vFyersPnl', 'fyers.funds': 'vFyersFunds',
      'fyers.positions': 'vFyersPositions', 'fyers.orders': 'vFyersOrders', 'fyers.trades': 'vFyersTrades',
      'fyers.holdings': 'vFyersHoldings', 'fyers.quotes': 'vFyersQuotes',
    } as Record<string, string>)[topic];
    return view ? `SELECT TOP 100 * FROM dbo.${view} ORDER BY EventTime DESC;`
                : `SELECT TOP 100 * FROM dbo.RealtimeEvents WHERE Topic = '${topic}' ORDER BY EventTime DESC;`;
  }
}
