import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe, UpperCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { ApiService } from '../../core/api.service';
import { SignalListItem, SignalListResponse, SignalQuery } from '../../core/models';
import { ConsoleStateService } from '../console-state.service';
import { SignalDetailModalComponent } from './signal-detail-modal.component';

const PRESET_KEY = 'jarvis.signals.preset';
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

@Component({
  selector: 'app-live-signals-tab',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, UpperCasePipe, FormsModule, SignalDetailModalComponent],
  templateUrl: './live-signals-tab.component.html',
  styleUrl: './live-signals-tab.component.scss',
})
export class LiveSignalsTabComponent {
  private api = inject(ApiService);
  private state = inject(ConsoleStateService);

  subTab = signal<'recent' | 'queue' | 'rejected'>('recent');
  filtersOpen = signal(true);
  autoRefresh = signal(false);

  form: SignalQuery = this.defaultForm();
  applied = signal<SignalQuery>({ ...this.form });
  quick = signal('all');
  sort = signal('time');

  baskets = signal<string[]>([]);
  strategies = signal<string[]>([]);
  data = signal<SignalListResponse | null>(null);
  loading = signal(false);
  selected = signal<SignalListItem | null>(null);
  toast = signal<string | null>(null);

  quickFilters = [
    { key: 'all', label: 'All' }, { key: 'entry', label: 'Entry' }, { key: 'exit', label: 'Exit' },
    { key: 'exit-pending', label: 'Exit Pending' }, { key: 'exit-completed', label: 'Exit Completed' },
    { key: 'partial-exit', label: 'Partial Exit' }, { key: 'chunked', label: 'Chunked' },
  ];
  sorts = [{ key: 'time', label: 'Time' }, { key: 'symbol', label: 'Symbol' }, { key: 'exchange', label: 'Exchange' }, { key: 'side', label: 'Side' }];

  query = computed<SignalQuery>(() => ({ ...this.applied(), filter: this.quick(), sort: this.sort(), take: 50 }));

  constructor() {
    this.api.signalFilters().subscribe(f => { this.baskets.set(f.baskets); this.strategies.set(f.strategies); });
    effect(() => { this.state.refreshTick(); this.load(this.query()); });
    interval(10_000).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
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

  pct(s: SignalListItem) { return s.requested ? (s.filled / s.requested) * 100 : 0; }
  exitPct(s: SignalListItem) { return s.requested ? (s.exited / s.requested) * 100 : 0; }

  onClosed(changed: boolean) {
    this.selected.set(null);
    if (changed) this.load(this.query());
  }
}
