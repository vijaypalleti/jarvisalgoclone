import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import { OpsService } from '../../core/ops.service';
import { ContainerInfo, ContainerSummary } from '../../core/ops.models';
import { ConsoleStateService } from '../console-state.service';
import { UiPointerService } from '../../shared/ui-pointer.service';

@Component({
  selector: 'app-infrastructure-tab',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, FormsModule],
  templateUrl: './infrastructure-tab.component.html',
  styleUrl: './infrastructure-tab.component.scss',
})
export class InfrastructureTabComponent {
  private ops = inject(OpsService);
  private state = inject(ConsoleStateService);

  data = signal<ContainerSummary | null>(null);
  loading = signal(false);
  autoRefresh = signal(true);
  lastLoaded = signal<Date | null>(null);
  now = signal(Date.now());
  busy = signal<string | null>(null);

  search = signal('');
  healthSel = signal('All');
  chip = signal<'All' | 'Healthy' | 'Unhealthy' | 'Degraded'>('All');
  view = signal<'cards' | 'table'>('cards');

  tiles = computed(() => {
    const d = this.data();
    if (!d) return [];
    return [
      { label: 'Total Containers', value: d.total, color: '#2563eb' },
      { label: 'Running', value: d.running, color: '#16a34a' },
      { label: 'Stopped', value: d.stopped, color: '#dc2626' },
      { label: 'Healthy', value: d.healthy, color: '#16a34a' },
      { label: 'Unhealthy', value: d.unhealthy, color: '#dc2626' },
      { label: 'No Heartbeat', value: d.noHeartbeat, color: '#ea580c' },
      { label: 'MongoDB Connected', value: d.mongoConnected, color: '#16a34a' },
      { label: 'Redis Connected', value: d.redisConnected, color: '#16a34a' },
      { label: 'Routing OK', value: d.routingOk, color: '#16a34a' },
      { label: 'Avg CPU', value: d.avgCpu.toFixed(3) + '%', color: '#6366f1' },
      { label: 'Avg Memory', value: d.avgMemory.toFixed(2) + '%', color: '#6366f1' },
    ];
  });

  cacheAgeSec = computed(() => {
    const d = this.data();
    return d ? Math.max(0, Math.round((new Date(d.checkedAt).getTime() - new Date(d.contractMasterLoadedAt).getTime()) / 1000)) : 0;
  });

  shown = computed(() => {
    const q = this.search().trim().toLowerCase();
    const health = this.chip() !== 'All' ? this.chip() : this.healthSel();
    return (this.data()?.containers ?? []).filter(c =>
      (!q || c.name.toLowerCase().includes(q) || c.clientId.toLowerCase().includes(q) || c.accounts.toLowerCase().includes(q)) &&
      (health === 'All' || this.healthOf(c) === health));
  });

  constructor() {
    effect(() => { this.state.refreshTick(); this.load(); });
    // assistant "show the container that's down": make sure no search/health filter hides it
    const pointer = inject(UiPointerService);
    effect(() => {
      if (!pointer.request()?.targets.some(t => t.key.startsWith('container:'))) return;
      untracked(() => { this.search.set(''); this.healthSel.set('All'); this.chip.set('All'); });
    });
    const destroy = inject(DestroyRef);
    interval(this.state.livePollMs).pipe(takeUntilDestroyed(destroy)).subscribe(() => { if (this.autoRefresh()) this.load(true); });
    interval(1_000).pipe(takeUntilDestroyed(destroy)).subscribe(() => this.now.set(Date.now()));
  }

  load(silent = false) {
    if (!silent) this.loading.set(true);
    this.ops.containers().subscribe({
      next: d => { this.data.set(d); this.lastLoaded.set(new Date()); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  healthOf(c: ContainerInfo): 'Healthy' | 'Unhealthy' | 'Degraded' {
    if (c.status !== 'Running' || !c.heartbeat) return 'Unhealthy';
    if (c.restartsToday > 2 || c.cpuPct > 40 || !c.redisOk || !c.mongoOk || !c.ltpFeed) return 'Degraded';
    return 'Healthy';
  }

  /** 0.2682 ms → "00:00:00.0002682" (same format as the source dashboard) */
  latency(ms: number) { return '00:00:00.' + (ms / 1000).toFixed(7).slice(2); }

  act(kind: 'restart' | 'stop', c: ContainerInfo) {
    if (kind === 'stop' && !confirm(`Stop ${c.name}?`)) return;
    this.busy.set(c.name);
    const call = kind === 'restart' ? this.ops.restartContainer(c.name) : this.ops.stopContainer(c.name);
    call.subscribe({ next: () => { this.busy.set(null); this.load(true); }, error: () => this.busy.set(null) });
  }
  provisionAll() { this.ops.provisionAll().subscribe(() => this.load()); }
  stopAll() { if (confirm('Stop ALL containers? Every client OMS will go offline.')) this.ops.stopAll().subscribe(() => this.load()); }
  reloadCache() { this.ops.reloadContractMaster().subscribe(() => this.load()); }
}
