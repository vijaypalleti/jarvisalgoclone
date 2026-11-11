import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { TickDirective } from '../../shared/tick.directive';
import { OpsService } from '../../core/ops.service';
import {
  AccountDetail, AccountListItem, AutoCloseEntry, CleanupItem, HedgeRecord, InFlightExit, PositionRecord, PositionsTabSummary
} from '../../core/ops.models';
import { ConsoleStateService } from '../console-state.service';

type Sub = 'tracking' | 'inflight' | 'cleanup' | 'hedges' | 'autoclose';
const BROKERS = ['5PAISA', 'ANT', 'ANGELONE', 'ZERODHA', 'UPSTOX', 'FYERS', 'SHAREINDIA', 'NUBRA'];
const PRESET_KEY = 'jarvis.positions.detailFilter';

@Component({
  selector: 'app-positions-tab',
  standalone: true,
  imports: [IconComponent, TickDirective, DatePipe, DecimalPipe, FormsModule],
  templateUrl: './positions-tab.component.html',
  styleUrl: './positions-tab.component.scss',
})
export class PositionsTabComponent {
  private ops = inject(OpsService);
  private state = inject(ConsoleStateService);
  private route = inject(ActivatedRoute);

  brokers = BROKERS;
  sub = signal<Sub>((this.route.snapshot.queryParamMap.get('sub') as Sub) || 'tracking');
  filtersOpen = signal(true);
  autoRefresh = signal(true);   // demo simulator keeps P&L moving

  // top filters
  f = { search: '', broker: '', clientId: '', partnerId: '', openOnly: false };
  applied = signal({ ...this.f });
  sort = signal<'client' | 'open' | 'broker'>('client');
  sortAsc = signal(true);

  summary = signal<PositionsTabSummary | null>(null);
  accounts = signal<AccountListItem[]>([]);
  selectedId = signal<string | null>(null);
  detail = signal<AccountDetail | null>(null);
  detailTab = signal<'positions' | 'orders'>('positions');
  expanded = signal<string | null>(null);
  busy = signal<string | null>(null);

  // detail filters (client-side)
  df = this.emptyDetailFilter();
  dApplied = signal(this.emptyDetailFilter());

  inflight = signal<InFlightExit[]>([]);
  cleanup = signal<CleanupItem[]>([]);
  hedges = signal<HedgeRecord[]>([]);
  autoClose = signal<AutoCloseEntry[]>([]);

  sortedAccounts = computed(() => {
    const list = [...this.accounts()];
    return this.sortAsc() ? list : list.reverse();
  });

  positions = computed(() => {
    const d = this.detail();
    if (!d) return [];
    const f = this.dApplied();
    const has = (v: string, q: string) => !q || v.toLowerCase().includes(q.trim().toLowerCase());
    return d.positions.filter(p =>
      (f.status === 'All' || p.status === f.status.toUpperCase() || (f.status === 'Open' && p.status === 'RISK')) &&
      has(p.symbol, f.symbol) && has(String(p.strategyId), f.strategyId) && has(p.exchange, f.exchange) &&
      has(p.product, f.product) && has(p.entrySignalId, f.entrySignal) && has(p.latestExitId ?? '', f.exitSignal) &&
      has(p.correlation, f.correlation) && has(p.token, f.token) && has(p.orderTag, f.orderTag) &&
      (!f.from || p.createdAt.slice(0, 10) >= f.from) && (!f.to || p.createdAt.slice(0, 10) <= f.to));
  });

  constructor() {
    effect(() => {
      this.state.refreshTick();
      this.applied(); this.sort();
      this.loadAccounts();
      this.ops.positionsSummary().subscribe(s => this.summary.set(s));
    });
    effect(() => {
      const s = this.sub();
      this.state.refreshTick();
      if (s === 'inflight') this.ops.inFlight().subscribe(x => this.inflight.set(x));
      if (s === 'cleanup') this.ops.cleanup().subscribe(x => this.cleanup.set(x));
      if (s === 'hedges') this.ops.hedges().subscribe(x => this.hedges.set(x));
      if (s === 'autoclose') this.ops.autoClose().subscribe(x => this.autoClose.set(x));
    });
    interval(this.state.livePollMs).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
      if (!this.autoRefresh()) return;
      this.loadAccounts(); this.reloadDetail();
      this.ops.positionsSummary().subscribe(s => this.summary.set(s));
      const s = this.sub();
      if (s === 'inflight') this.ops.inFlight().subscribe(x => this.inflight.set(x));
      if (s === 'cleanup') this.ops.cleanup().subscribe(x => this.cleanup.set(x));
      if (s === 'hedges') this.ops.hedges().subscribe(x => this.hedges.set(x));
      if (s === 'autoclose') this.ops.autoClose().subscribe(x => this.autoClose.set(x));
    });
  }

  private emptyDetailFilter() {
    return { status: 'All', symbol: '', strategyId: '', exchange: '', product: '', entrySignal: '', exitSignal: '',
             correlation: '', token: '', orderTag: '', from: '', to: '' };
  }

  loadAccounts() {
    const a = this.applied();
    this.ops.accounts({ ...a, sort: this.sort() }).subscribe(list => {
      this.accounts.set(list);
      const sel = this.selectedId();
      if (!sel || !list.some(x => x.clientId === sel)) this.select(list[0]?.clientId ?? null);
    });
  }

  select(id: string | null) {
    this.selectedId.set(id);
    this.expanded.set(null);
    this.reloadDetail();
  }

  reloadDetail() {
    const id = this.selectedId();
    if (!id) { this.detail.set(null); return; }
    this.ops.account(id, {}).subscribe(d => this.detail.set(d));
  }

  apply() { this.applied.set({ ...this.f }); }
  clear() { this.f = { search: '', broker: '', clientId: '', partnerId: '', openOnly: false }; this.apply(); }
  toggleOpenOnly() { this.f.openOnly = !this.f.openOnly; this.apply(); }

  applyDetail() { this.dApplied.set({ ...this.df }); }
  detailToday() { const t = new Date().toISOString().slice(0, 10); this.df.from = t; this.df.to = t; this.applyDetail(); }
  detailLast7() {
    const to = new Date(); const from = new Date(); from.setDate(to.getDate() - 7);
    this.df.from = from.toISOString().slice(0, 10); this.df.to = to.toISOString().slice(0, 10); this.applyDetail();
  }
  clearDetail() { this.df = this.emptyDetailFilter(); this.applyDetail(); }
  saveDetailPreset() { try { localStorage.setItem(PRESET_KEY, JSON.stringify(this.df)); } catch { /* storage unavailable */ } }
  loadDetailPreset() {
    try { const raw = localStorage.getItem(PRESET_KEY); if (raw) { this.df = { ...this.emptyDetailFilter(), ...JSON.parse(raw) }; this.applyDetail(); } } catch { /* ignore */ }
  }

  exitPosition(p: PositionRecord, ev: Event) {
    ev.stopPropagation();
    const id = this.selectedId();
    if (!id || !confirm(`Force exit ${Math.abs(p.netQty)} × ${p.symbol}?`)) return;
    this.busy.set(p.id);
    this.ops.exitPosition(id, p.id).subscribe({
      next: () => { this.busy.set(null); this.reloadDetail(); this.loadAccounts(); },
      error: () => this.busy.set(null),
    });
  }

  exportCsv() {
    const d = this.detail();
    if (!d) return;
    const cols: (keyof PositionRecord)[] = ['side', 'exchange', 'symbol', 'strategyId', 'product', 'buyQty', 'sellQty', 'netQty',
      'avgBuy', 'avgSell', 'ltp', 'realized', 'unrealized', 'status', 'createdAt', 'closedAt', 'correlation'];
    const esc = (v: unknown) => `"${String(v ?? '').replace(/"/g, '""')}"`;
    const csv = [cols.join(','), ...this.positions().map(p => cols.map(c => esc(p[c])).join(','))].join('\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
    a.download = `positions-${d.account.clientId}-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(a.href);
  }

  pnlClass(v: number | null | undefined) { return !v ? '' : v > 0 ? 'pos' : 'neg'; }
  shortId(s: string | null | undefined, n = 22) { return !s ? '' : s.length > n ? s.slice(0, n) + '…' : s; }
}
