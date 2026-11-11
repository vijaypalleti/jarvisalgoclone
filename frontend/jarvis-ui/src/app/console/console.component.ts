import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SimScenario, SimService } from '../core/sim.service';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { IconComponent } from '../shared/icon.component';
import { ConsoleStateService } from './console-state.service';
import { OverviewTabComponent } from './overview/overview-tab.component';
import { LiveSignalsTabComponent } from './signals/live-signals-tab.component';
import { PlaceholderTabComponent } from './placeholder-tab.component';
import { PositionsTabComponent } from './positions/positions-tab.component';
import { PerformanceTabComponent } from './performance/performance-tab.component';
import { InfrastructureTabComponent } from './infrastructure/infrastructure-tab.component';

export interface ConsoleTab { key: string; label: string; icon: string; }

@Component({
  selector: 'app-console',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, OverviewTabComponent, LiveSignalsTabComponent, PlaceholderTabComponent,
            PositionsTabComponent, PerformanceTabComponent, InfrastructureTabComponent],
  templateUrl: './console.component.html',
  styleUrl: './console.component.scss',
})
export class ConsoleComponent implements OnInit {
  state = inject(ConsoleStateService);
  private router = inject(Router);

  /** bound from ?tab= query param (withComponentInputBinding) */
  tab = input<string | undefined>();
  active = computed(() => this.tab() ?? 'overview');

  tabs: ConsoleTab[] = [
    { key: 'overview', label: 'Overview', icon: 'bar-chart' },
    { key: 'signals', label: 'Live Signals & Lifecycle', icon: 'send' },
    { key: 'positions', label: 'Positions & Exits', icon: 'pin' },
    { key: 'performance', label: 'Performance', icon: 'trending-up' },
    { key: 'infrastructure', label: 'Infrastructure', icon: 'wrench' },
    { key: 'logs', label: 'Logs & Audit', icon: 'search' },
    { key: 'alerts', label: 'Alerts', icon: 'bell' },
    { key: 'calendar', label: 'Trading Calendar', icon: 'calendar' },
    { key: 'cache', label: 'Strategy Cache', icon: 'layers' },
  ];

  activeLabel = computed(() => this.tabs.find(t => t.key === this.active())?.label ?? '');

  private simApi = inject(SimService);
  private destroy = inject(DestroyRef);

  ngOnInit() {
    this.state.refresh();
    this.state.poll();
    // header numbers + demo-sim status stay live without reloading the tabs
    interval(this.state.livePollMs).pipe(takeUntilDestroyed(this.destroy)).subscribe(() => this.state.poll());
  }

  // ---- demo market simulator
  scenarios: SimScenario[] = ['Calm', 'Normal', 'Volatile', 'Rally', 'Crash'];
  feedOpen = signal(false);
  setScenario(s: SimScenario) { this.simApi.scenario(s).subscribe(x => this.state.sim.set(x)); }
  toggleSim() {
    const running = this.state.sim()?.running;
    (running ? this.simApi.pause() : this.simApi.resume()).subscribe(x => this.state.sim.set(x));
  }

  // ---- dismissible dead-letter banner (per browser session)
  private static readonly BANNER_KEY = 'jarvis.banner.hidden';
  bannerHidden = signal(ConsoleComponent.readHidden());
  issueCount = computed(() => {
    const o = this.state.overview();
    return o ? o.deadLettersInRange + o.failedExecutionsInRange : 0;
  });
  hideBanner() { this.bannerHidden.set(true); try { sessionStorage.setItem(ConsoleComponent.BANNER_KEY, '1'); } catch { /* ignore */ } }
  showBanner() { this.bannerHidden.set(false); try { sessionStorage.removeItem(ConsoleComponent.BANNER_KEY); } catch { /* ignore */ } }
  private static readHidden() { try { return sessionStorage.getItem(ConsoleComponent.BANNER_KEY) === '1'; } catch { return false; } }

  select(key: string) {
    this.router.navigate([], { queryParams: { tab: key === 'overview' ? null : key }, queryParamsHandling: 'merge' });
  }

  export() {
    const o = this.state.overview();
    if (!o) return;
    const blob = new Blob([JSON.stringify(o, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `console-overview-${new Date().toISOString().slice(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(a.href);
  }
}
