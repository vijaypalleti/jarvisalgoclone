import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { TickDirective } from '../../shared/tick.directive';
import { MultiSelectComponent, MsOption } from '../../shared/multi-select.component';
import { OpsService } from '../../core/ops.service';
import { ApiService } from '../../core/api.service';
import { AccountListItem, PerformanceSummary, UserPerformanceDetail } from '../../core/ops.models';
import { ConsoleStateService } from '../console-state.service';

type Inner = 'overview' | 'trading' | 'users' | 'basket' | 'billing';
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

@Component({
  selector: 'app-performance-tab',
  standalone: true,
  imports: [IconComponent, TickDirective, MultiSelectComponent, DatePipe, DecimalPipe, FormsModule],
  templateUrl: './performance-tab.component.html',
  styleUrl: './performance-tab.component.scss',
})
export class PerformanceTabComponent {
  private ops = inject(OpsService);
  private api = inject(ApiService);
  private state = inject(ConsoleStateService);

  inner = signal<Inner>('trading');
  innerTabs: { key: Inner; label: string; icon: string }[] = [
    { key: 'overview', label: 'Overview', icon: 'bar-chart' },
    { key: 'trading', label: 'Trading', icon: 'trending-up' },
    { key: 'users', label: 'User Management', icon: 'users-round' },
    { key: 'basket', label: 'Basket & Strategy', icon: 'basket' },
    { key: 'billing', label: 'Subscriptions & Billing', icon: 'ticket' },
  ];

  filtersOpen = signal(true);
  baskets = signal<string[]>([]);
  strategies = signal<string[]>([]);
  /** strategy name → production code (STRAT…), shown next to the name like the real dropdown */
  strategyCodes = signal<Record<string, string>>({});
  basket = signal('');
  /** selected strategies (empty = all). P&L, positions and trades are computed from these only. */
  strategySel = signal<string[]>([]);
  /** label used in the scope texts: '' (all) · the name · 'N strategies' */
  strategy = computed(() => { const s = this.strategySel(); return s.length === 0 ? '' : s.length === 1 ? s[0] : `${s.length} strategies`; });
  /** options for the multi-select: name + production code on the right */
  strategyOptions = computed<MsOption[]>(() => this.strategies().map(s => ({ value: s, label: s, hint: this.strategyCodes()[s] })));
  from = signal(iso(new Date()));
  to = signal(iso(new Date()));
  range = signal<'today' | '7d' | '30d' | 'mtd' | 'custom'>('today');

  // user list filters
  partnerId = '';
  userName = '';
  broker = '';
  listFilter = signal({ partnerId: '', name: '', broker: '' });
  sort = signal<'name' | 'pnl'>('name');
  sortAsc = signal(true);
  autoRefresh = signal(true);   // demo simulator keeps P&L moving
  collapsed = signal(false);

  data = signal<PerformanceSummary | null>(null);
  checked = signal<Set<string>>(new Set());
  activeId = signal<string | null>(null);
  detail = signal<UserPerformanceDetail | null>(null);
  positionsOpen = signal(true);
  tradesOpen = signal(true);
  tradeFilter = signal('');
  expandedTrade = signal<string | null>(null);

  users = computed(() => {
    const list = [...(this.data()?.users ?? [])];
    const key = this.sort();
    list.sort((a, b) => key === 'pnl' ? a.strategyPnl - b.strategyPnl : a.name.localeCompare(b.name));
    return this.sortAsc() ? list : list.reverse();
  });
  trades = computed(() => {
    const q = this.tradeFilter().trim().toLowerCase();
    const t = this.detail()?.trades ?? [];
    return q ? t.filter(x => (x.correlation + x.strategy + x.symbol).toLowerCase().includes(q)) : t;
  });
  allChecked = computed(() => this.users().length > 0 && this.users().every(u => this.checked().has(u.clientId)));

  constructor() {
    this.api.signalFilters().subscribe(f => {
      this.baskets.set(f.baskets); this.strategies.set(f.strategies);
      this.strategyCodes.set(Object.fromEntries(f.strategyCatalog.map(x => [x.name, x.code])));
    });
    effect(() => {
      this.state.refreshTick();
      const lf = this.listFilter();
      const strategies = this.strategySel().join(',') || undefined;
      this.ops.performance({ basket: this.basket(), partnerId: lf.partnerId, name: lf.name, broker: lf.broker, strategies }).subscribe(d => {
        this.data.set(d);
        const active = this.activeId();
        if (!active || !d.users.some(u => u.clientId === active)) this.open(d.users[0]?.clientId ?? null);
        else this.loadDetail();
      });
    });
    interval(this.state.livePollMs).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => { if (this.autoRefresh()) this.refreshPnl(); });
  }

  setRange(r: 'today' | '7d' | '30d' | 'mtd') {
    const now = new Date();
    const from = new Date(now);
    if (r === '7d') from.setDate(now.getDate() - 7);
    if (r === '30d') from.setDate(now.getDate() - 30);
    if (r === 'mtd') from.setDate(1);
    this.from.set(iso(from)); this.to.set(iso(now)); this.range.set(r);
  }

  applyList() { this.listFilter.set({ partnerId: this.partnerId, name: this.userName, broker: this.broker }); }
  clearList() { this.partnerId = ''; this.userName = ''; this.broker = ''; this.applyList(); }
  refreshPnl() { this.listFilter.set({ ...this.listFilter() }); }

  open(id: string | null) {
    this.activeId.set(id);
    if (id) this.checked.update(s => new Set(s).add(id));
    this.expandedTrade.set(null);
    this.loadDetail();
  }
  loadDetail() {
    const id = this.activeId();
    if (!id) { this.detail.set(null); return; }
    this.ops.userPerformance(id, this.strategySel().join(',') || undefined).subscribe(d => this.detail.set(d));
  }
  toggleCheck(u: AccountListItem, ev: Event) {
    ev.stopPropagation();
    this.checked.update(s => { const n = new Set(s); n.has(u.clientId) ? n.delete(u.clientId) : n.add(u.clientId); return n; });
  }
  toggleAll() {
    this.checked.set(this.allChecked() ? new Set() : new Set(this.users().map(u => u.clientId)));
  }
  clearSelection() { this.checked.set(new Set()); }
  closeDetails() { this.activeId.set(null); this.detail.set(null); }

  initials(name: string) { return name.split(' ').map(p => p[0]).join('').slice(0, 2).toUpperCase(); }
  pnlClass(v: number | null | undefined) { return !v ? '' : v > 0 ? 'pos' : 'neg'; }
  short(s: string, n = 28) { return s.length > n ? s.slice(0, n) + '…' : s; }
}
