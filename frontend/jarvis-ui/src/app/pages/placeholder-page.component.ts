import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

@Component({
  selector: 'app-placeholder-page',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="card empty">
      <h2>{{ title }}</h2>
      <p>This screen hasn't been cloned yet. Only the Strategy Performance console is wired up.</p>
      <a class="btn primary" routerLink="/admin/strategy-performance-console-v2">Open Strategy Performance (New)</a>
    </div>`,
  styles: [`.empty{padding:48px;text-align:center;margin-top:20px} h2{margin:0 0 8px} p{color:var(--text-2);margin:0 0 20px}`],
})
export class PlaceholderPageComponent {
  title = inject(Router).url.split('/').pop()!.split('?')[0].replace(/-/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
}
