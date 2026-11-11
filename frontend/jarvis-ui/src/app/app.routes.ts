import { Routes } from '@angular/router';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'admin/strategy-performance-console-v2' },
      {
        path: 'admin/strategy-performance-console-v2',
        loadComponent: () => import('./console/console.component').then(m => m.ConsoleComponent),
      },
      {
        path: 'admin/broker-live',
        loadComponent: () => import('./pages/broker-live/broker-live-tab.component').then(m => m.BrokerLiveTabComponent),
      },
      {
        path: 'admin/crypto-live',
        loadComponent: () => import('./pages/crypto-live/crypto-live.component').then(m => m.CryptoLiveComponent),
      },
      {
        path: 'admin/realtime',
        loadComponent: () => import('./pages/realtime/realtime-page.component').then(m => m.RealtimePageComponent),
      },
      {
        path: '**',
        loadComponent: () => import('./pages/placeholder-page.component').then(m => m.PlaceholderPageComponent),
      },
    ],
  },
];
