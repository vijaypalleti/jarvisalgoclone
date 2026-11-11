import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type SimScenario = 'Calm' | 'Normal' | 'Volatile' | 'Rally' | 'Crash';
export interface SimEvent { time: string; kind: string; text: string; }
export interface SimStatus {
  running: boolean; scenario: SimScenario; tickMs: number; ticks: number; lastTick: string | null;
  spot: number; spotOpen: number; spotChangePct: number; totalPnl: number; openPositions: number; events: SimEvent[];
}

/** Demo market simulator controls (moves the fake OMS data; never touches Fyers/Delta). */
@Injectable({ providedIn: 'root' })
export class SimService {
  private http = inject(HttpClient);
  status() { return this.http.get<SimStatus>('/api/sim'); }
  pause() { return this.http.post<SimStatus>('/api/sim/pause', {}); }
  resume() { return this.http.post<SimStatus>('/api/sim/resume', {}); }
  scenario(name: SimScenario) { return this.http.post<SimStatus>(`/api/sim/scenario/${name.toLowerCase()}`, {}); }
}
