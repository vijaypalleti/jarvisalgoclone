import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../core/api.service';
import { OverviewSummary } from '../core/models';
import { SimService, SimStatus } from '../core/sim.service';

/** Shared state for the Strategy Performance console: overview summary + a refresh tick tabs can react to. */
@Injectable({ providedIn: 'root' })
export class ConsoleStateService {
  private api = inject(ApiService);
  private simApi = inject(SimService);

  readonly overview = signal<OverviewSummary | null>(null);
  readonly alertsOpen = signal(0);
  readonly refreshTick = signal(0);
  readonly loading = signal(false);
  readonly sim = signal<SimStatus | null>(null);

  /** How often tabs poll while the demo simulator is running. */
  readonly livePollMs = 3000;
  get live() { return this.sim()?.running ?? false; }

  refresh() {
    this.loading.set(true);
    this.loadOverview(() => this.loading.set(false));
    this.refreshTick.update(t => t + 1);
  }

  /** Quiet refresh of the header numbers only (no tab reloads). */
  poll() {
    this.loadOverview();
    this.simApi.status().subscribe({ next: s => this.sim.set(s), error: () => this.sim.set(null) });
  }

  private loadOverview(done?: () => void) {
    this.api.overview().subscribe({
      next: o => { this.overview.set(o); this.alertsOpen.set(o.logsErrors.errors); done?.(); },
      error: () => done?.(),
    });
  }
}
