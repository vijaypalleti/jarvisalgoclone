import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, debounceTime, interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { ApiService } from '../../core/api.service';
import { AlertsResponse } from '../../core/models';
import { ConsoleStateService } from '../console-state.service';

@Component({
  selector: 'app-recent-alerts',
  standalone: true,
  imports: [IconComponent, DatePipe, FormsModule],
  templateUrl: './recent-alerts.component.html',
  styleUrl: './recent-alerts.component.scss',
})
export class RecentAlertsComponent {
  private api = inject(ApiService);
  private router = inject(Router);
  private state = inject(ConsoleStateService);

  data = signal<AlertsResponse | null>(null);
  refreshing = signal(false);
  expanded = signal<string | null>(null);
  search = '';
  private search$ = new Subject<string>();

  chips = computed(() => (this.data()?.byBroker ?? []).slice(0, 4));
  moreChips = computed(() => Math.max(0, (this.data()?.byBroker.length ?? 0) - 4));

  constructor() {
    effect(() => { this.state.refreshTick(); this.load(); });
    this.search$.pipe(debounceTime(250), takeUntilDestroyed()).subscribe(() => this.load());
    interval(30_000).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => this.load());
  }

  onSearch(v: string) { this.search = v; this.search$.next(v); }

  load() {
    this.refreshing.set(true);
    this.api.alerts(this.search).subscribe({
      next: d => { this.data.set(d); this.state.alertsOpen.set(d.openCount); this.refreshing.set(false); },
      error: () => this.refreshing.set(false),
    });
  }

  toggle(id: string) { this.expanded.update(e => (e === id ? null : id)); }
  ackAccount(id: string, ev: Event) { ev.stopPropagation(); this.api.ackAccount(id).subscribe(() => this.load()); }
  ackRejection(id: string) { this.api.ackRejection(id).subscribe(() => this.load()); }
  ackAll() { this.api.ackAll().subscribe(() => this.load()); }
  viewLog() { this.router.navigate([], { queryParams: { tab: 'alerts' } }); }
}
