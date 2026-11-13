import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild, computed, effect, inject, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CandlestickData, ColorType, HistogramData, IChartApi, ISeriesApi, UTCTimestamp, createChart } from 'lightweight-charts';
import { Subscription } from 'rxjs';
import { IconComponent } from '../../shared/icon.component';
import { DeltaMarketService } from '../../core/delta-market.service';

/** NIFTY50 / BANKNIFTY / SENSEX / BTCUSD (fixed tabs) or any Fyers ticker, e.g. NSE:RELIANCE-EQ */
type Sym = string;
interface SearchHit { ticker: string; name: string; symbol: string; exchange: string; series: string; }
interface Bar { time: number; open: number; high: number; low: number; close: number; volume: number; }
interface Quote { last: number; change: number; changePct: number; open: number; high: number; low: number; prevClose: number; }

const IST = 5.5 * 3600;
interface SymMeta { key: Sym; name: string; sub: string; ccy: '₹' | '$'; custom?: boolean; }
const SYMBOLS: SymMeta[] = [
  { key: 'NIFTY50', name: 'NIFTY 50', sub: 'NSE index · Fyers', ccy: '₹' },
  { key: 'BANKNIFTY', name: 'BANK NIFTY', sub: 'NSE index · Fyers', ccy: '₹' },
  { key: 'SENSEX', name: 'SENSEX', sub: 'BSE index · Fyers', ccy: '₹' },
  { key: 'BTCUSD', name: 'BTCUSD', sub: 'Perpetual · Delta Exchange', ccy: '$' },
];

/**
 * Market Charts: candlestick charts for NIFTY 50, BANK NIFTY, SENSEX and ANY NSE/BSE stock (search box; Fyers historical data,
 * read-only) and BTCUSD
 * (Delta Exchange). The last candle updates live: indices from Fyers quotes every 5 s during market hours, BTC from the
 * Delta trade stream. URL: /admin/market-charts?symbol=NIFTY50&tf=5m (the assistant opens charts this way).
 */
@Component({
  selector: 'app-market-charts',
  standalone: true,
  imports: [IconComponent, DecimalPipe, RouterLink],
  templateUrl: './market-charts.component.html',
  styleUrl: './market-charts.component.scss',
})
export class MarketChartsComponent implements AfterViewInit, OnDestroy {
  private http = inject(HttpClient);
  private router = inject(Router);
  private delta = inject(DeltaMarketService);
  @ViewChild('chartEl') chartEl!: ElementRef<HTMLElement>;

  readonly symbols = SYMBOLS;
  readonly tfs = ['1m', '5m', '15m', '1h', '1d'];
  symbol = signal<Sym>('NIFTY50');
  tf = signal('5m');
  /** the searched stock shown as a 5th card (name filled in from the candles response) */
  custom = signal<SymMeta | null>(null);
  meta = computed<SymMeta>(() => SYMBOLS.find(s => s.key === this.symbol()) ?? this.custom() ?? { key: this.symbol(), name: this.symbol(), sub: 'Fyers', ccy: '₹', custom: true });
  isIndex = computed(() => !this.isBtc() && (SYMBOLS.some(s => s.key === this.symbol()) || this.symbol().endsWith('-INDEX')));

  // stock search
  q = signal('');
  hits = signal<SearchHit[]>([]);
  searchOpen = signal(false);
  private searchTimer?: ReturnType<typeof setTimeout>;
  isBtc = computed(() => this.symbol() === 'BTCUSD');

  loading = signal(false);
  error = signal<{ text: string; login?: boolean } | null>(null);
  quote = signal<Quote | null>(null);
  count = signal(0);
  /** latest candle (+ previous session close) for the header fallback */
  lastBar = signal<{ close: number; high: number; low: number; open: number; prevClose: number } | null>(null);
  marketOpen = signal(isMarketOpen());
  updatedAt = signal<number | null>(null);
  btcTicker = this.delta.ticker;

  /** header numbers: BTC from the live Delta ticker, indices from the Fyers quote */
  head = computed(() => {
    if (this.isBtc()) {
      const t = this.btcTicker();
      return t ? { last: t.last, changePct: t.changePct, high: t.high, low: t.low, open: t.open } : null;
    }
    const q = this.quote();
    if (q) return { last: q.last, changePct: q.changePct, high: q.high, low: q.low, open: q.open };
    // no quote yet (or Fyers busy): fall back to the latest candle so the header isn't empty
    const b = this.lastBar();
    return b ? { last: b.close, changePct: b.prevClose ? (b.close - b.prevClose) / b.prevClose * 100 : 0, high: b.high, low: b.low, open: b.open } : null;
  });

  private chart?: IChartApi;
  private candles?: ISeriesApi<'Candlestick'>;
  private volume?: ISeriesApi<'Histogram'>;
  private last?: Bar;
  private intervalSec = 300;
  private poll?: ReturnType<typeof setInterval>;
  private tradeSub?: Subscription;
  private loadSeq = 0;
  private viewReady = signal(false);

  constructor() {
    inject(ActivatedRoute).queryParamMap.pipe(takeUntilDestroyed()).subscribe(q => {
      const rawSym = (q.get('symbol') ?? '').trim();
      const s = rawSym.toUpperCase().replace(/[\s-]/g, '');
      const sym = SYMBOLS.find(x => x.key === s || (s === 'BTC' && x.key === 'BTCUSD') || (s === 'NIFTY' && x.key === 'NIFTY50'))?.key;
      if (sym) this.symbol.set(sym);
      else if (rawSym) {                       // any stock: NSE:RELIANCE-EQ (or a name; the API resolves it)
        const t = rawSym.toUpperCase();
        if (this.custom()?.key !== t) this.custom.set({ key: t, name: t.replace(/^\w+:/, '').replace(/-(EQ|BE|INDEX|[A-Z])$/, ''), sub: `${t.split(':')[0] ?? ''} · Fyers`, ccy: '₹', custom: true });
        this.symbol.set(t);
      }
      const tf = q.get('tf'); if (tf && this.tfs.includes(tf)) this.tf.set(tf);
    });
    this.delta.connect();
    this.tradeSub = this.delta.trade$.subscribe(t => { if (this.isBtc()) this.apply(t.price, t.size, t.time); });
    effect(() => { const s = this.symbol(), tf = this.tf(); if (this.viewReady()) untracked(() => this.load(s, tf)); });
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
    this.candles = this.chart.addCandlestickSeries({ upColor: '#16a34a', downColor: '#dc2626', borderVisible: false, wickUpColor: '#16a34a', wickDownColor: '#dc2626' });
    this.volume = this.chart.addHistogramSeries({ priceScaleId: '', priceFormat: { type: 'volume' } });
    this.volume.priceScale().applyOptions({ scaleMargins: { top: 0.82, bottom: 0 } });
    this.viewReady.set(true);
  }

  pick(s: Sym) { this.go({ symbol: s, tf: this.tf() }); }
  closeCustom() { this.custom.set(null); this.pick('NIFTY50'); }

  onSearch(v: string) {
    this.q.set(v); this.searchOpen.set(true);
    clearTimeout(this.searchTimer);
    if (!v.trim()) { this.hits.set([]); return; }
    this.searchTimer = setTimeout(() => this.http.get<{ results: SearchHit[] }>('/api/market/search', { params: { q: v, limit: 8 } })
      .subscribe({ next: r => this.hits.set(r.results), error: () => this.hits.set([]) }), 180);
  }
  choose(h: SearchHit) {
    this.custom.set({ key: h.ticker, name: h.name, sub: `${h.exchange} · ${h.series === 'INDEX' ? 'index' : h.series} · Fyers`, ccy: '₹', custom: true });
    this.q.set(''); this.hits.set([]); this.searchOpen.set(false);
    this.pick(h.ticker);
  }
  searchEnter() { const h = this.hits()[0]; if (h) this.choose(h); }
  setTf(tf: string) { this.go({ symbol: this.symbol(), tf }); }
  private go(q: { symbol: string; tf: string }) { this.router.navigate([], { queryParams: q, replaceUrl: true }); }
  reload() { this.load(this.symbol(), this.tf()); }

  private load(sym: Sym, tf: string) {
    const seq = ++this.loadSeq;
    clearInterval(this.poll);
    this.loading.set(true); this.error.set(null); this.quote.set(null); this.lastBar.set(null);
    const url = sym === 'BTCUSD' ? `/api/delta/candles?resolution=${tf}&count=300` : `/api/market/candles?symbol=${encodeURIComponent(sym)}&resolution=${tf}&count=300`;
    this.http.get<{ candles: Bar[]; intervalSeconds: number; name?: string; isIndex?: boolean; symbol?: string }>(url).subscribe({
      next: r => {
        if (seq !== this.loadSeq) return;
        this.intervalSec = r.intervalSeconds;
        const c = this.custom();
        if (c && c.key === sym && r.name) this.custom.set({ ...c, name: r.name });
        const daily = tf === '1d';
        // strictly ascending, one bar per time (the chart throws otherwise)
        const byTime = new Map(r.candles.map(c => [c.time, c]));
        const bars = [...byTime.values()].sort((a, b) => a.time - b.time).map(c => ({ ...c, time: c.time + (daily ? 0 : IST) }));
        this.candles?.setData(bars.map(b => ({ time: b.time as UTCTimestamp, open: b.open, high: b.high, low: b.low, close: b.close })));
        this.volume?.setData(!this.isIndex() ? bars.map(b => this.volBar(b)) : []);    // index candles have no volume
        this.last = bars.at(-1) ? { ...bars.at(-1)! } : undefined;
        this.count.set(bars.length);
        const lb = bars.at(-1);
        // previous close = close of the last candle of an earlier day (intraday) or the candle before (daily)
        const day = (t: number) => Math.floor(t / 86400);
        const prev = lb ? (daily ? bars.at(-2) : [...bars].reverse().find(x => day(x.time) < day(lb.time))) : undefined;
        this.lastBar.set(lb ? { close: lb.close, high: lb.high, low: lb.low, open: lb.open, prevClose: prev?.close ?? 0 } : null);
        this.chart?.timeScale().fitContent();
        this.chart?.timeScale().scrollToRealTime();
        this.loading.set(false);
        this.updatedAt.set(Date.now());
        if (sym !== 'BTCUSD') { this.fetchQuote(sym, seq); this.poll = setInterval(() => this.fetchQuote(sym, seq), 5000); }
      },
      error: e => {
        if (seq !== this.loadSeq) return;
        this.candles?.setData([]); this.volume?.setData([]); this.count.set(0);
        this.error.set({ text: e?.error?.error ?? 'Could not load candles', login: !!e?.error?.needsLogin });
        this.loading.set(false);
      },
    });
  }

  private fetchQuote(sym: Sym, seq: number) {
    this.http.get<Quote>(`/api/market/quote?symbol=${encodeURIComponent(sym)}`).subscribe({
      next: q => {
        if (seq !== this.loadSeq) return;
        this.quote.set(q); this.updatedAt.set(Date.now());
        this.marketOpen.set(isMarketOpen());
        if (this.marketOpen()) this.apply(q.last, 0, Date.now());       // outside market hours the index doesn't move
      },
      error: () => {},
    });
  }

  /** Extend / roll the last candle with a live price. */
  private apply(price: number, size: number, timeMs: number) {
    if (!this.candles || !this.last || !price || this.tf() === '1d') return;
    const bucket = Math.floor(timeMs / 1000 / this.intervalSec) * this.intervalSec + IST;
    const l = this.last;
    if (bucket < l.time) return;
    if (bucket > l.time) this.last = { time: bucket, open: l.close, high: Math.max(l.close, price), low: Math.min(l.close, price), close: price, volume: size };
    else { l.high = Math.max(l.high, price); l.low = Math.min(l.low, price); l.close = price; l.volume += size; }
    this.candles.update({ time: this.last.time as UTCTimestamp, open: this.last.open, high: this.last.high, low: this.last.low, close: this.last.close } as CandlestickData);
    if (!this.isIndex()) this.volume?.update(this.volBar(this.last));
  }
  private volBar(c: Bar): HistogramData { return { time: c.time as UTCTimestamp, value: c.volume, color: c.close >= c.open ? 'rgba(22,163,74,.35)' : 'rgba(220,38,38,.35)' }; }

  ngOnDestroy() { clearInterval(this.poll); this.tradeSub?.unsubscribe(); this.delta.disconnect(); this.chart?.remove(); }
}

/** NSE / BSE cash session: Mon–Fri 09:15–15:30 IST (holidays not considered). */
function isMarketOpen(now = new Date()) {
  const ist = new Date(now.getTime() + (now.getTimezoneOffset() + 330) * 60000);
  const d = ist.getDay(), m = ist.getHours() * 60 + ist.getMinutes();
  return d >= 1 && d <= 5 && m >= 555 && m <= 930;
}
