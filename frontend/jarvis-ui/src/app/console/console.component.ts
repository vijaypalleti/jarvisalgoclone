import { Component, OnInit, computed, inject, input } from '@angular/core';
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

  ngOnInit() { this.state.refresh(); }

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
