import { Component, HostListener, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { DatePipe, DecimalPipe, LowerCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { ApiService } from '../../core/api.service';
import { ChunkOrder, SignalAccountExecution, SignalDetailResponse, SignalListItem } from '../../core/models';

/** Optional columns that are hidden when every visible row is zero/empty (unless "Show all columns"). */
type OptCol = 'chunks' | 'pending' | 'rejected' | 'cancelled' | 'avgEntry' | 'avgExit' | 'retry' | 'modify' | 'reason';

@Component({
  selector: 'app-signal-detail-modal',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, LowerCasePipe, FormsModule],
  templateUrl: './signal-detail-modal.component.html',
  styleUrl: './signal-detail-modal.component.scss',
})
export class SignalDetailModalComponent implements OnInit {
  private api = inject(ApiService);

  sig = input.required<SignalListItem>();
  closed = output<boolean>();

  detail = signal<SignalDetailResponse | null>(null);
  page = signal(1);
  expanded = signal<string | null>(null);
  busy = signal<string | null>(null);   // 'force' | `exit:<client>` | `chunk:<client>:<n>`
  toast = signal<string | null>(null);
  showAllCols = signal(false);
  changed = false;

  f = { client: '', account: '', broker: '', status: '' };
  private filter$ = new Subject<void>();

  pages = computed(() => {
    const a = this.detail()?.accounts;
    return a ? Math.max(1, Math.ceil(a.total / a.pageSize)) : 1;
  });
  range = computed(() => {
    const a = this.detail()?.accounts;
    if (!a || a.total === 0) return 'No broker accounts';
    const start = (a.page - 1) * a.pageSize + 1;
    return `Showing ${start}–${start + a.items.length - 1} of ${a.total} broker accounts · problems first`;
  });
  totalAccounts = computed(() => (this.detail()?.statusCounts ?? []).reduce((n, c) => n + c.count, 0));

  /** A column is shown if any row on this page has a meaningful value in it. */
  cols = computed<Record<OptCol, boolean>>(() => {
    const rows = this.detail()?.accounts.items ?? [];
    const all = this.showAllCols();
    const any = (fn: (a: SignalAccountExecution) => unknown) => all || rows.some(a => { const v = fn(a); return v !== null && v !== undefined && v !== 0 && v !== ''; });
    return {
      chunks: any(a => a.chunkOrders.length), pending: any(a => a.pending), rejected: any(a => a.rejected),
      cancelled: any(a => a.cancelled), avgEntry: any(a => a.avgEntry), avgExit: any(a => a.avgExit),
      retry: any(a => a.retry), modify: any(a => a.modify), reason: any(a => a.rejectReason ?? a.partialReason),
    };
  });
  hiddenCount = computed(() => Object.values(this.cols()).filter(v => !v).length);
  colspan = computed(() => 7 + Object.values(this.cols()).filter(Boolean).length);

  constructor() {
    this.filter$.pipe(debounceTime(250), takeUntilDestroyed()).subscribe(() => { this.page.set(1); this.load(); });
  }

  ngOnInit() { this.load(); }

  load() {
    this.api.signalDetail(this.sig().id, { ...this.f, page: this.page(), pageSize: 10 })
      .subscribe(d => {
        this.detail.set(d);
        // open the worst row automatically so the problem is visible without a click
        const first = d.accounts.items[0];
        if (first && first.severity === 0 && this.expanded() === null) this.expanded.set(this.key(first));
      });
  }

  onFilter() { this.filter$.next(); }
  setStatus(s: string) { this.f.status = s; this.page.set(1); this.load(); }
  go(p: number) { this.page.set(p); this.expanded.set(null); this.load(); }
  toggle(key: string) { this.expanded.update(e => (e === key ? null : key)); }
  key(a: SignalAccountExecution) { return a.clientId + '|' + a.brokerAccount; }

  // ------------------------------------------------------------ presentation helpers

  statusIcon(s: string) { return s === 'COMPLETED' || s === 'EXECUTED' ? '✓' : s === 'PARTIAL' ? '⚠' : s === 'REJECTED' || s === 'FAILED' ? '✕' : '●'; }
  statusTone(s: string) { return s === 'COMPLETED' || s === 'EXECUTED' ? 'green' : s === 'PARTIAL' ? 'amber' : s === 'REJECTED' || s === 'FAILED' ? 'red' : 'gray'; }
  chunkAnomaly(c: ChunkOrder) { return (c.brokerStatus === 'COMPLETED' && c.fill === 0 && c.req > 0) || (c.taskStatus === 'EXECUTED' && !c.brokerOrder); }
  placed(a: SignalAccountExecution) { return a.chunkOrders.filter(c => !!c.brokerOrder).length; }
  isZero(v: number | null | undefined) { return v === null || v === undefined || v === 0; }

  badge(s: SignalListItem) {
    if (s.health === 'residual') return { label: `Exit partial · ${s.residualQty.toLocaleString()} residual`, tone: 'amber', icon: '⚠' };
    if (s.health === 'error') {
      const n = s.unresolvedIssues;
      return { label: `${s.exitStatus === 'EXIT COMPLETED' ? 'Exited' : s.status} · ${n} issue${n === 1 ? '' : 's'}`, tone: 'red', icon: '✕' };
    }
    if (s.health === 'open') return { label: 'Open', tone: 'blue', icon: '●' };
    return { label: 'Exited', tone: 'green', icon: '✓' };
  }

  copy(text: string | null | undefined, ev: Event) {
    ev.stopPropagation();
    if (!text) return;
    navigator.clipboard?.writeText(text).then(() => this.flash('Copied ' + text), () => this.flash('Copy failed'));
  }
  private flash(msg: string) { this.toast.set(msg); setTimeout(() => this.toast.set(null), 1600); }

  // ------------------------------------------------------------ actions

  exitRemaining(a: { clientId: string; netOpen?: number; qty?: number }, ev?: Event) {
    ev?.stopPropagation();
    const qty = a.netOpen ?? a.qty ?? 0;
    if (!confirm(`Exit the remaining ${qty.toLocaleString()} qty for client ${a.clientId}?`)) return;
    this.busy.set('exit:' + a.clientId);
    this.api.exitRemaining(this.sig().id, a.clientId).subscribe({
      next: () => { this.done(`Exit sent for ${a.clientId}`); },
      error: e => { this.busy.set(null); this.flash(e?.error?.error ?? 'Exit failed'); },
    });
  }

  retryChunk(a: SignalAccountExecution, c: ChunkOrder, ev: Event) {
    ev.stopPropagation();
    if (!confirm(`Retry chunk ${c.index}/${a.chunkOrders.length} (${c.unfilled.toLocaleString()} qty) for ${a.clientId}?`)) return;
    this.busy.set(`chunk:${a.clientId}:${c.index}`);
    this.api.retryChunk(this.sig().id, a.clientId, c.index).subscribe({
      next: () => this.done(`Chunk ${c.index} re-placed for ${a.clientId}`),
      error: e => { this.busy.set(null); this.flash(e?.error?.error ?? 'Retry failed'); },
    });
  }

  forceExit() {
    if (!confirm(`Force exit ALL open positions for ${this.sig().symbol}?`)) return;
    this.busy.set('force');
    this.api.forceExit(this.sig().id).subscribe({
      next: () => this.done('Force exit sent'),
      error: () => this.busy.set(null),
    });
  }

  private done(msg: string) { this.changed = true; this.busy.set(null); this.flash(msg); this.load(); }

  @HostListener('document:keydown.escape') close() { this.closed.emit(this.changed); }
}
