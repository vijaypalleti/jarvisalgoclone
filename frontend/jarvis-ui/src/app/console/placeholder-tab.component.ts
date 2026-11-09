import { Component, input } from '@angular/core';

@Component({
  selector: 'app-placeholder-tab',
  standalone: true,
  template: `<div class="card box"><h3>{{ title() }}</h3><p>Not cloned yet — add this tab under <code>src/app/console/</code> and register it in <code>console.component.html</code>.</p></div>`,
  styles: [`.box{padding:40px;text-align:center} h3{margin:0 0 6px} p{color:var(--text-2);margin:0}`],
})
export class PlaceholderTabComponent {
  title = input('');
}
