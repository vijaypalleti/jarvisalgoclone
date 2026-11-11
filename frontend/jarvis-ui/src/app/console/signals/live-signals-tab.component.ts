import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { MultiSelectComponent, MsOption } from '../../shared/multi-select.component';
import { ApiService } from '../../core/api.service';
import { SignalListItem, SignalListResponse, SignalQuery } from '../../core/models';
import { ConsoleStateService } from '../console-state.service';
import { SignalDetailModalComponent } from './signal-detail-modal.component';

const PRESET_KEY = 'jarvis.signals.preset';
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

@Component({
  selector: 'app-live-signals-tab',
  standalone: true,
  imports: [IconComponent, MultiSelectComponent, DatePipe, DecimalPipe, FormsModule, SignalDetailModalComponent],
  templateUrl: './live-signals-tab.component.html',
  styleUrl: './live-signals-tab.component.scss',
})
export class LiveSignalsTabComponent {
  private api = inject(ApiService);
  private state = inject(ConsoleStateService);

  subTab = signal<'recent' | 'queue' | 'rejected'>('recent');
  filtersOpen = signal(true);
  autoRefresh = signal(true);

  form: SignalQuery = this.defaultForm();
  applied = signal<SignalQuery>({ ...this.form });
  quick = signal('all');
  sort = signal('time');

  baskets = signal<string[]>([]);
  strategies = signal<string[]>([]);
  /** strategy name → production code (STRAT…), shown next to the name like the real dropdown */
  strategyCodes = signal<Record<string, string>>({});
  /** options for the multi-select: name + production code on the right */
  strategyOptions = computed<MsOption[]>(() => this.strategies().map(s => ({ value: s, label: s, hint: this.strategyCodes()[s] })));
  /** form.strategy holds the selection as 'a,b,c' (the API accepts several) */
  strategySel() { return (this.form.strategy ?? '').split(',').filter(Boolean); }
  setStrategies(v: string[]) { this.form = { ...this.form, strategy: v.join(',') }; this.apply(); }
  data = signal<SignalListResponse | null>(null);
  loading = signal(false);
  selected = signal<SignalListItem | null>(null);
  toast = signal<string | null>(null);

  /** "Needs attention" first: partial/residual, errors, contradictions */
  quickFilters = [
    { key: 'attention', label: 'Needs attention' },
    { key: 'all', label: 'All' }, { key: 'entry', label: 'Entry' }, { key: 'exit', label: 'Exit' },
    { key: 'exit-pending', label: 'Exit Pending' }, { key: 'exit-completed', label: 'Exit Completed' },
    { key: 'partial-exit', label: 'Partial Exit' }, { key: 'chunked', label: 'Chunked' },
  ];
  sorts = [{ key: 'time', label: 'Time' }, { key: 'symbol', label: 'Symbol' }, { key: 'exchange', label: 'Exchange' }, { key: 'side', label: 'Side' }];

  query = computed<SignalQuery>(() => ({ ...this.applied(), filter: this.quick(), sort: this.sort(), take: 50 }));

  /** cards (default) or one-line compact rows for scanning; remembered per browser */
  view = signal<'cards' | 'compact'>(this.readView());

  /** Rows grouped under date headers (the filter usually spans several days). */
  groups = computed(() => {
    const items = this.data()?.items ?? [];
    if (this.sort() !== 'time') return [{ key: 'all', label: '', items }];
    const out: { key: string; label: string; items: SignalListItem[] }[] = [];
    for (const s of items) {
      const key = s.reachedApiAt.slice(0, 10);
      let g = out.at(-1);
      if (!g || g.key !== key) { g = { key, label: this.dayLabel(key), items: [] }; out.push(g); }
      g.items.push(s);
    }
    return out;
  });

  constructor() {
    this.api.signalFilters().subscribe(f => {
      this.baskets.set(f.baskets); this.strategies.set(f.strategies);
      this.strategyCodes.set(Object.fromEntries(f.strategyCatalog.map(x => [x.name, x.code])));
    });
    effect(() => { this.state.refreshTick(); this.load(this.query()); });
    interval(5_000).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
      if (this.autoRefresh()) this.load(this.query());
    });
  }

  private defaultForm(): SignalQuery {
    const today = new Date();
    const from = new Date(); from.setDate(today.getDate() - 2);
    return { from: iso(from), to: iso(today), symbol: '', correlationId: '', client: '', basket: '', strategy: '' };
  }

  load(q: SignalQuery) {
    this.loading.set(true);
    this.api.signals(q).subscribe({
      next: d => { this.data.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  apply() { this.applied.set({ ...this.form }); }
  today() { const t = iso(new Date()); this.form = { ...this.form, from: t, to: t }; this.apply(); }
  clear() { this.form = this.defaultForm(); this.apply(); }

  savePreset() {
    try { localStorage.setItem(PRESET_KEY, JSON.stringify(this.form)); this.flash('Preset saved'); } catch { this.flash('Could not save preset'); }
  }
  loadPreset() {
    try {
      const raw = localStorage.getItem(PRESET_KEY);
      if (!raw) return this.flash('No saved preset');
      this.form = { ...this.defaultForm(), ...JSON.parse(raw) };
      this.apply();
      this.flash('Preset loaded');
    } catch { this.flash('Could not load preset'); }
  }
  private flash(msg: string) { this.toast.set(msg); setTimeout(() => this.toast.set(null), 1800); }

  // ------------------------------------------------------------ row presentation

  /** Segmented bar: exited (green) · filled but still open (blue) · unfilled/rejected (red), as % of requested. */
  segments(s: SignalListItem) {
    const req = s.requested || 1;
    const exited = Math.min(s.exited, s.filled);
    return {
      exited: (exited / req) * 100,
      open: (Math.max(0, s.filled - exited) / req) * 100,
      unfilled: (Math.max(0, s.requested - s.filled) / req) * 100,
      tip: `Exited ${exited.toLocaleString()} · Open ${Math.max(0, s.filled - exited).toLocaleString()} · ` +
           `Unfilled ${Math.max(0, s.requested - s.filled).toLocaleString()} of ${s.requested.toLocaleString()} requested`,
    };
  }

  /** One status badge that follows the worst account — never "completed" while qty is still open. */
  badge(s: SignalListItem): { label: string; tone: string; icon: string } {
    const issues = s.unresolvedIssues;
    if (s.health === 'residual') return { label: `Exit partial · ${s.residualQty.toLocaleString()} residual`, tone: 'amber', icon: '⚠' };
    const base = s.exitStatus === 'EXIT COMPLETED' ? 'Exited' : s.status === 'Open' ? 'Open' : s.status;
    if (s.health === 'error') return { label: `${base} · ${issues} issue${issues === 1 ? '' : 's'}`, tone: 'red', icon: '✕' };
    if (s.health === 'open') return { label: 'Open', tone: 'blue', icon: '●' };
    return { label: base, tone: 'green', icon: '✓' };
  }

  /** Lifecycle stepper state for the exit step. */
  exitStep(s: SignalListItem): { icon: string; tone: string; text: string } {
    if (s.health === 'residual') return { icon: '⚠', tone: 'amber', text: `Exit partial (${s.residualAccounts} acct)` };
    if (s.exitStatus === 'EXIT COMPLETED') return { icon: '✓', tone: 'green', text: 'Exit' };
    return { icon: '…', tone: 'gray', text: 'Exit pending' };
  }

  copy(text: string | null | undefined, ev: Event) {
    ev.stopPropagation();
    if (!text) return;
    navigator.clipboard?.writeText(text).then(() => this.flash('Copied ' + (text.length > 18 ? text.slice(0, 18) + '…' : text)),
      () => this.flash('Copy failed'));
  }

  setView(v: 'cards' | 'compact') {
    this.view.set(v);
    try { localStorage.setItem('jarvis.signals.view', v); } catch { /* storage unavailable */ }
  }
  private readView(): 'cards' | 'compact' {
    try { return localStorage.getItem('jarvis.signals.view') === 'compact' ? 'compact' : 'cards'; } catch { return 'cards'; }
  }
  private dayLabel(isoDay: string) {
    const d = new Date(isoDay + 'T00:00:00');
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const diff = Math.round((today.getTime() - d.getTime()) / 86_400_000);
    if (diff === 0) return 'Today';
    if (diff === 1) return 'Yesterday';
    return d.toLocaleDateString('en-IN', { weekday: 'short', day: '2-digit', month: 'short' });
  }

  onClosed(changed: boolean) {
    this.selected.set(null);
    if (changed) this.load(this.query());
  }
}
