import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './foundation.html',
  styleUrl: './foundation.css',
})
export class App {
  protected readonly title = signal('ClearCase');
}
