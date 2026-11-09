import {
  AfterViewInit, Component, ElementRef, OnDestroy, OnInit, ViewChild, computed, effect, inject, signal
} from '@angular/core';
import { DatePipe, DecimalPipe, UpperCasePipe } from '@angular/common';
import {
  CandlestickData, ColorType, HistogramData, IChartApi, ISeriesApi, UTCTimestamp, createChart
} from 'lightweight-charts';
import { Subscription } from 'rxjs';
import { IconComponent } from '../../shared/icon.component';
import { Candle, DeltaMarketService, StorageStatus } from '../../core/delta-market.service';

/** Chart shows IST wall-clock time (lightweight-charts renders timestamps as UTC). */
const IST_OFFSET = 5.5 * 3600;

@Component({
  selector: 'app-crypto-live',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, UpperCasePipe],
  templateUrl: './crypto-live.component.html',
  styleUrl: './crypto-live.component.scss',
})
export class CryptoLiveComponent implements OnInit, AfterViewInit, OnDestroy {
  market = inject(DeltaMarketService);

  @ViewChild('chartEl') chartEl!: ElementRef<HTMLDivElement>;
  private chart?: IChartApi;
  private candleSeries?: ISeriesApi<'Candlestick'>;
  private volumeSeries?: ISeriesApi<'Histogram'>;
  private lastCandle?: CandlestickData & { volume: number };
  private intervalSec = 300;
  private sub?: Subscription;

  resolutions = ['1m', '5m', '15m', '1h', '4h', '1d'];
  resolution = signal('5m');
  chartLoading = signal(false);
  chartError = signal<string | null>(null);

  /** price direction for the flash effect */
  dir = signal<'up' | 'down' | null>(null);
  private prevLast = 0;

  now = signal(Date.now());
  private clock?: ReturnType<typeof setInterval>;

  storage = signal<StorageStatus | null>(null);
  chartSources = signal<{ sql: number; delta: number } | null>(null);
  lastFlushAgo = computed(() => {
    const t = this.storage()?.persister.lastFlushUtc;
    return t ? Math.max(0, Math.round((this.now() - new Date(t).getTime()) / 1000)) : null;
  });

  ticker = this.market.ticker;
  contractValue = computed(() => this.ticker()?.contractValue || 0.001);
  maxBookTotal = computed(() => {
    const b = this.market.book();
    if (!b) return 1;
    return Math.max(b.bids.at(-1)?.total ?? 0, b.asks.at(-1)?.total ?? 0, 1);
  });
  asksDesc = computed(() => [...(this.market.book()?.asks ?? [])].reverse());
  mid = computed(() => {
    const b = this.market.book();
    return b && b.bids[0] && b.asks[0] ? (b.bids[0].price + b.asks[0].price) / 2 : null;
  });
  ageSec = computed(() => {
    const t = this.market.lastUpdate();
    return t ? Math.max(0, Math.round((this.now() - t) / 1000)) : null;
  });
  live = computed(() => this.market.linkState() === 'connected' && !!this.market.feed()?.connected);

  constructor() {
    effect(() => {
      const t = this.ticker();
      if (!t) return;
      if (this.prevLast && t.last !== this.prevLast) {
        this.dir.set(t.last > this.prevLast ? 'up' : 'down');
        setTimeout(() => this.dir.set(null), 600);
      }
      this.prevLast = t.last;
      this.applyPrice(t.last, 0, t.time);
    });
  }

  ngOnInit() {
    this.market.connect();
    let tick = 0;
    this.loadStorage();
    this.clock = setInterval(() => {
      this.now.set(Date.now());
      if (++tick % 5 === 0) this.loadStorage();
    }, 1000);
    this.sub = this.market.trade$.subscribe(tr => this.applyPrice(tr.price, tr.size, tr.time));
  }

  ngAfterViewInit() {
    const css = getComputedStyle(document.documentElement);
    const text = css.getPropertyValue('--text-2').trim() || '#4b5563';
    const grid = css.getPropertyValue('--border').trim() || '#e5e7eb';

    this.chart = createChart(this.chartEl.nativeElement, {
      autoSize: true,
      layout: { background: { type: ColorType.Solid, color: 'transparent' }, textColor: text, fontFamily: 'Inter, system-ui, sans-serif', fontSize: 11 },
      grid: { vertLines: { color: grid }, horzLines: { color: grid } },
      rightPriceScale: { borderColor: grid },
      timeScale: { borderColor: grid, timeVisible: true, secondsVisible: false },
      crosshair: { mode: 1 },
    });
    this.candleSeries = this.chart.addCandlestickSeries({
      upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626',
    });
    this.volumeSeries = this.chart.addHistogramSeries({ priceScaleId: '', priceFormat: { type: 'volume' } });
    this.volumeSeries.priceScale().applyOptions({ scaleMargins: { top: 0.82, bottom: 0 } });
    this.loadCandles(this.resolution());
  }

  private loadStorage() {
    this.market.storageStatus().subscribe({ next: s => this.storage.set(s), error: () => this.storage.set(null) });
  }

  setResolution(r: string) {
    if (r === this.resolution()) return;
    this.resolution.set(r);
    this.loadCandles(r);
  }

  private loadCandles(r: string) {
    this.chartLoading.set(true);
    this.chartError.set(null);
    this.market.candles(r).subscribe({
      next: res => {
        this.intervalSec = res.intervalSeconds;
        this.chartSources.set({ sql: res.fromSql ?? 0, delta: res.fromDelta ?? res.candles.length });
        const candles: (CandlestickData & { volume: number })[] = res.candles.map((c: Candle) => ({
          time: (c.time + IST_OFFSET) as UTCTimestamp, open: c.open, high: c.high, low: c.low, close: c.close, volume: c.volume,
        }));
        this.candleSeries?.setData(candles);
        this.volumeSeries?.setData(candles.map(c => this.volBar(c)));
        this.lastCandle = candles.at(-1) ? { ...candles.at(-1)! } : undefined;
        this.chart?.timeScale().scrollToRealTime();
        this.chartLoading.set(false);
      },
      error: e => { this.chartError.set(e?.error?.error ?? 'Could not load candles'); this.chartLoading.set(false); },
    });
  }

  /** Extend / roll the last candle from live trades and ticks. */
  private applyPrice(price: number, size: number, timeMs: number) {
    if (!this.candleSeries || !this.lastCandle || !price) return;
    const bucket = (Math.floor(timeMs / 1000 / this.intervalSec) * this.intervalSec + IST_OFFSET) as UTCTimestamp;
    const last = this.lastCandle;
    if (bucket < (last.time as number)) return;
    if (bucket > (last.time as number)) {
      this.lastCandle = { time: bucket, open: last.close, high: Math.max(last.close, price), low: Math.min(last.close, price), close: price, volume: size };
    } else {
      last.high = Math.max(last.high, price);
      last.low = Math.min(last.low, price);
      last.close = price;
      last.volume += size;
    }
    this.candleSeries.update(this.lastCandle);
    this.volumeSeries?.update(this.volBar(this.lastCandle));
  }

  private volBar(c: CandlestickData & { volume: number }): HistogramData {
    return { time: c.time, value: c.volume, color: c.close >= c.open ? 'rgba(22,163,74,.35)' : 'rgba(220,38,38,.35)' };
  }

  ngOnDestroy() {
    this.sub?.unsubscribe();
    clearInterval(this.clock);
    this.chart?.remove();
    this.market.disconnect();
  }
}
