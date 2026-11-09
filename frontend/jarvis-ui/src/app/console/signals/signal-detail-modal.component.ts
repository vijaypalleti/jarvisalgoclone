import { Component, HostListener, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { DatePipe, DecimalPipe, LowerCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { ApiService } from '../../core/api.service';
import { SignalDetailResponse, SignalListItem } from '../../core/models';

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
  busy = signal(false);
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
    return `Showing ${start}–${start + a.items.length - 1} of ${a.total} broker accounts`;
  });

  constructor() {
    this.filter$.pipe(debounceTime(250), takeUntilDestroyed()).subscribe(() => { this.page.set(1); this.load(); });
  }

  ngOnInit() { this.load(); }

  load() {
    this.api.signalDetail(this.sig().id, { ...this.f, page: this.page(), pageSize: 10 })
      .subscribe(d => this.detail.set(d));
  }

  onFilter() { this.filter$.next(); }
  go(p: number) { this.page.set(p); this.expanded.set(null); this.load(); }
  toggle(key: string) { this.expanded.update(e => (e === key ? null : key)); }

  forceExit() {
    if (!confirm(`Force exit all open positions for ${this.sig().symbol}?`)) return;
    this.busy.set(true);
    this.api.forceExit(this.sig().id).subscribe({
      next: () => { this.changed = true; this.busy.set(false); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  @HostListener('document:keydown.escape') close() { this.closed.emit(this.changed); }
}
