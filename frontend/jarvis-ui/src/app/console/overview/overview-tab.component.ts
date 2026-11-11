import {
  AfterViewInit, Component, ElementRef, HostListener, NgZone, OnDestroy, QueryList, ViewChild, ViewChildren, computed, effect,
  inject, signal
} from '@angular/core';
import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Router } from '@angular/router';
import { IconComponent } from '../../shared/icon.component';
import { ApiService } from '../../core/api.service';
import { BrokerSessionHealth, PagedResult, SubscriptionItem } from '../../core/models';
import { ConsoleStateService } from '../console-state.service';
import { RecentAlertsComponent } from './recent-alerts.component';
import { OpsService } from '../../core/ops.service';
import { TodayActivity } from '../../core/ops.models';

interface HubCard {
  key: string; title: string; value: string; icon: string; color: string;
  tags: { label: string; tone: string }[]; live?: boolean; highlight?: boolean; tab?: string; route?: string;
  sub?: string; subTab?: string; anchor?: string;
  /** optional rich value, e.g. S/U letters tinted in "S 31 · U 82" */
  segments?: { t: string; accent?: boolean }[];
}

/**
 * One rail: cubic Bézier from a port on the core edge (x1,y1) to the tile's inner edge (x2,y2).
 * `d` runs core → tile, so both the bead flow (stroke-dashoffset) and the spark travel into the tile.
 */
interface Connector {
  id: string; d: string; color: string; live: boolean;
  x1: number; y1: number; x2: number; y2: number;
  begin: number;
}

@Component({
  selector: 'app-overview-tab',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, NgTemplateOutlet, RecentAlertsComponent],
  templateUrl: './overview-tab.component.html',
  styleUrl: './overview-tab.component.scss',
})
export class OverviewTabComponent implements AfterViewInit, OnDestroy {
  private api = inject(ApiService);
  private ops = inject(OpsService);
  private router = inject(Router);
  state = inject(ConsoleStateService);

  @ViewChild('hub') hubEl!: ElementRef<HTMLElement>;
  @ViewChild('core') coreEl!: ElementRef<HTMLElement>;
  @ViewChild('links') linksEl?: ElementRef<SVGSVGElement>;
  private zone = inject(NgZone);
  @ViewChildren('cardEl') cardEls!: QueryList<ElementRef<HTMLElement>>;

  connectors = signal<Connector[]>([]);
  svgSize = signal({ w: 0, h: 0 });
  private ro?: ResizeObserver;

  subs = signal<PagedResult<SubscriptionItem> | null>(null);
  subsPage = signal(1);
  sessions = signal<BrokerSessionHealth | null>(null);

  activity = signal<TodayActivity | null>(null);
  maxHour = computed(() => Math.max(1, ...(this.activity()?.hours.map(h => h.signals) ?? [1])));
  fillPct = computed(() => {
    const f = this.activity()?.funnel;
    return f && f.orders ? Math.round((f.filled / f.orders) * 100) : 0;
  });
  /** soonest-ending subscriptions for "Needs attention" (first page is already sorted by end date) */
  endingSoon = computed(() => (this.subs()?.items ?? []).slice(0, 3));

  left = computed<HubCard[]>(() => {
    const o = this.state.overview();
    const v = (n: number | undefined) => (n === undefined ? '—' : String(n));
    return [
      { key: 'perf', title: 'Performance', value: o ? `S ${o.strategyPerformance.total} · U ${o.userPerformance.users}` : '—',
        segments: o ? [{ t: 'S', accent: true }, { t: ` ${o.strategyPerformance.total} · ` }, { t: 'U', accent: true }, { t: ` ${o.userPerformance.users}` }] : undefined,
        icon: 'trending-up', color: '#3b82f6', tab: 'performance',
        tags: [{ label: `Profitable ${o?.strategyPerformance.profitable ?? 0}`, tone: 'green' }, { label: `Losing ${o?.strategyPerformance.losing ?? 0}`, tone: 'red' }] },
      { key: 'cont', title: 'Containers', value: o ? `${o.containers.running}/${o.containers.total}` : '—', icon: 'package', color: '#14b8a6', tab: 'infrastructure',
        tags: [{ label: `Healthy ${o?.containers.healthy ?? 0}`, tone: 'green' }, { label: `Unhealthy ${o?.containers.unhealthy ?? 0}`, tone: 'green' }] },
      { key: 'exits', title: 'In-Flight Exits', value: v(o?.inFlightExits.total), sub: o ? `(${o.inFlightExits.accounts} accounts)` : '',
        icon: 'alert-circle', color: '#ef4444', live: true, highlight: true, tab: 'positions', subTab: 'inflight',
        tags: [{ label: `Pending ${o?.inFlightExits.pending ?? 0}`, tone: 'orange' }, { label: `Accounts ${o?.inFlightExits.accounts ?? 0}`, tone: 'orange' }] },
      { key: 'clogs', title: 'Container Logs', value: 'View', icon: 'terminal', color: '#a855f7', tab: 'logs',
        tags: [{ label: 'Log stream ›', tone: 'purple' }] },
      { key: 'subs', title: 'Subscriptions Track', value: v(o?.subscriptions.ending7d), sub: '(ending ≤ 7d)', icon: 'tag', color: '#ec4899', anchor: 'subscription-watch',
        tags: [{ label: `Today ${o?.subscriptions.today ?? 0}`, tone: 'green' }, { label: `≤ 3d ${o?.subscriptions.within3d ?? 0}`, tone: 'green' },
               { label: `≤ 30d ${o?.subscriptions.within30d ?? 0}`, tone: 'cyan' }] },
    ];
  });

  right = computed<HubCard[]>(() => {
    const o = this.state.overview();
    const v = (n: number | undefined) => (n === undefined ? '—' : String(n));
    return [
      { key: 'oms', title: 'OMS Signals (Today)', value: v(o?.omsSignals.today), sub: o ? `(${o.omsSignals.accounts} accounts)` : '',
        icon: 'zap', color: '#22c55e', live: true, highlight: true, tab: 'signals',
        tags: [{ label: `In progress ${o?.omsSignals.inProgress ?? 0}`, tone: 'orange' }, { label: `Failed ${o?.omsSignals.failed ?? 0}`, tone: 'green' },
               { label: `Accounts ${o?.omsSignals.accounts ?? 0}`, tone: 'cyan' }] },
      { key: 'pos', title: 'Position Tracking', value: v(o?.positionTracking.total), icon: 'briefcase', color: '#f59e0b', live: true, highlight: true, tab: 'positions',
        tags: [{ label: `Opened ${o?.positionTracking.openedToday ?? 0}`, tone: 'blue' }, { label: `Closed ${o?.positionTracking.closedToday ?? 0}`, tone: 'green' },
               { label: `Accounts ${o?.positionTracking.loadedAccounts ?? 0}`, tone: 'cyan' }] },
      { key: 'logs', title: 'Logs & Errors', value: v(o?.logsErrors.total), icon: 'x-circle', color: '#6366f1', live: true, highlight: true, tab: 'logs',
        tags: [{ label: `Errors ${o?.logsErrors.errors ?? 0}`, tone: 'red' }, { label: `Dead-letters ${o?.logsErrors.deadLetters ?? 0}`, tone: 'orange' }] },
      { key: 'brokers', title: 'Brokers & Health', value: o ? `${o.health.successRate}%` : '—', sub: o ? `(${o.brokers.active}/${o.brokers.total} brokers)` : '',
        icon: 'radio', color: '#0ea5e9', route: '/admin/broker-live',
        tags: [{ label: `Brokers active ${o?.brokers.active ?? 0}/${o?.brokers.total ?? 0}`, tone: 'blue' }, { label: `Success ${o?.health.successRate ?? 0}%`, tone: 'green' }] },
      { key: 'sessions', title: 'Sessions Health', value: o ? `${o.sessions.valid}/${o.sessions.total}` : '—', icon: 'check-circle', color: '#22c55e', anchor: 'session-health',
        tags: [{ label: `Valid ${o?.sessions.valid ?? 0}`, tone: 'green' }, { label: `Not refreshed ${o?.sessions.notRefreshed ?? 0}`, tone: 'green' },
               { label: `No token ${o?.sessions.noToken ?? 0}`, tone: 'green' }] },
    ];
  });

  constructor() {
    effect(() => {
      this.state.refreshTick();
      this.loadSubs(this.subsPage());
      this.api.brokerSessions().subscribe(s => this.sessions.set(s));
      this.ops.todayActivity().subscribe(a => this.activity.set(a));
    });
  }

  scrollTo(id: string) { document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' }); }

  /**
   * Absolute document URL for SVG url(#id) references. With <base href="/"> a bare "#id" resolves against
   * the base URL in Firefox/Safari and the gradient/filter silently disappears.
   */
  svgRef = signal('');
  private rafId = 0;

  // ---- tile value-change flash + click launch
  private prevValues = new Map<string, string>();
  flashing = signal<ReadonlySet<string>>(new Set());
  launching = signal<string | null>(null);
  private timers: ReturnType<typeof setTimeout>[] = [];

  private trackValueChanges = effect(() => {
    const tiles = [...this.left(), ...this.right()];
    const changed: string[] = [];
    for (const t of tiles) {
      const prev = this.prevValues.get(t.key);
      if (prev !== undefined && prev !== '—' && t.value !== '—' && prev !== t.value) changed.push(t.key);
      this.prevValues.set(t.key, t.value);
    }
    if (!changed.length) return;
    this.flashing.update(s => new Set([...s, ...changed]));
    this.timers.push(setTimeout(() => this.flashing.update(s => new Set([...s].filter(k => !changed.includes(k)))), 700));
  });

  /**
   * Browser zoom (Ctrl +/−) changes the CSS-pixel layout. Several independent triggers make sure the rails are
   * re-drawn after it: ResizeObserver (element sizes), window resize (fires on every zoom step), devicePixelRatio
   * change (zoom changes DPR), visualViewport resize, and every overview refresh as a safety net.
   */
  @HostListener('window:resize') onWindowResize() { this.scheduleDraw(); }

  private redrawOnData = effect(() => { this.state.overview(); this.left(); this.right(); this.scheduleDraw(); });

  private dprQuery?: MediaQueryList;
  private onDprChange = () => { this.scheduleDraw(); this.watchDpr(); };
  private watchDpr() {
    this.dprQuery?.removeEventListener('change', this.onDprChange);
    this.dprQuery = matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
    this.dprQuery.addEventListener('change', this.onDprChange);
  }
  private onViewport = () => this.scheduleDraw();

  ngAfterViewInit() {
    this.watchDpr();
    window.visualViewport?.addEventListener('resize', this.onViewport);
    // Re-measure whenever the hub, the core or any tile changes size (window resize, data arriving, fonts).
    this.ro = new ResizeObserver(() => this.scheduleDraw());
    this.ro.observe(this.hubEl.nativeElement);
    this.ro.observe(this.coreEl.nativeElement);
    const observeCards = () => this.cardEls.forEach(r => this.ro!.observe(r.nativeElement));
    observeCards();
    this.cardEls.changes.subscribe(() => { observeCards(); this.scheduleDraw(); });
    this.scheduleDraw();
    // tiles slide in with a transform (ResizeObserver doesn't see that) → re-measure once the entrance finishes
    this.timers.push(setTimeout(() => this.scheduleDraw(), 1200));
  }

  ngOnDestroy() {
    this.ro?.disconnect(); cancelAnimationFrame(this.rafId); this.timers.forEach(clearTimeout);
    this.dprQuery?.removeEventListener('change', this.onDprChange);
    window.visualViewport?.removeEventListener('resize', this.onViewport);
  }

  /** Coalesce bursts of resize callbacks into one measurement per frame. */
  private scheduleDraw() {
    cancelAnimationFrame(this.rafId);
    this.rafId = requestAnimationFrame(() => this.drawConnectors());
  }

  /**
   * Rails from core ports to each tile's inner edge: d = M core C midX,coreY midX,tileY tile.
   * Ports per side: two top tiles → near the top corner, middle tile → mid-side, two bottom tiles → near the bottom corner.
   */
  private drawConnectors() {
    const hub = this.hubEl?.nativeElement;
    const core = this.coreEl?.nativeElement;
    if (!hub || !core) return;
    const h = hub.getBoundingClientRect();
    // measure relative to the SVG's own origin (that is the space the paths are drawn in)
    const svgBox = this.linksEl?.nativeElement.getBoundingClientRect();
    const o = { left: svgBox?.left ?? h.left, top: svgBox?.top ?? h.top };
    const c = core.getBoundingClientRect();
    const nLeft = this.left().length;
    const cards = [...this.left(), ...this.right()];
    // port offsets from the core's top as a fraction of its height (5 tiles per side)
    const ports = [0.09, 0.2, 0.5, 0.8, 0.91];

    const lines: Connector[] = this.cardEls.map((ref, i) => {
      const r = ref.nativeElement.getBoundingClientRect();
      const isLeft = i < nLeft;
      const k = isLeft ? i : i - nLeft;
      const x1 = (isLeft ? c.left : c.right) - o.left;
      const y1 = c.top - o.top + c.height * (ports[k] ?? 0.5);
      const x2 = (isLeft ? r.right : r.left) - o.left;
      const y2 = r.top + r.height / 2 - o.top;
      const midX = (x1 + x2) / 2;
      const f = (n: number) => n.toFixed(1);
      return {
        id: `ov-rail-${i}`, color: cards[i]?.color ?? '#94a3b8', live: !!cards[i]?.live,
        d: `M ${f(x1)},${f(y1)} C ${f(midX)},${f(y1)} ${f(midX)},${f(y2)} ${f(x2)},${f(y2)}`,
        x1, y1, x2, y2,
        begin: +(i * 0.25).toFixed(2),
      };
    });
    // ResizeObserver / matchMedia callbacks run outside Angular's zone → apply inside it so the view always repaints
    this.zone.run(() => {
      this.svgRef.set(location.href.split('#')[0]);
      this.svgSize.set({ w: h.width, h: h.height });
      this.connectors.set(lines);
    });
  }

  loadSubs(page: number) {
    this.api.expiringSubscriptions(page).subscribe(r => this.subs.set(r));
  }
  subsPages = computed(() => { const s = this.subs(); return s ? Math.max(1, Math.ceil(s.total / s.pageSize)) : 1; });
  subsRange = computed(() => {
    const s = this.subs();
    if (!s) return '';
    const start = (s.page - 1) * s.pageSize + 1;
    return `${start}–${Math.min(s.total, start + s.items.length - 1)} of ${s.total}`;
  });

  /** Play the launch animation, then navigate. */
  go(card: HubCard) {
    if (this.launching()) return;
    const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
    this.launching.set(card.key);
    this.timers.push(setTimeout(() => { this.launching.set(null); this.navigate(card); }, reduce ? 0 : 280));
  }

  private navigate(card: HubCard) {
    if (card.route) this.router.navigateByUrl(card.route);
    else if (card.anchor) this.scrollTo(card.anchor);
    else if (card.tab) this.router.navigate([], { queryParams: { tab: card.tab, sub: card.subTab ?? null } });
  }
}
