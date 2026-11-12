import { Component, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './foundation.html',
  styleUrl: './foundation.css',
})
export class App {
  protected readonly title = signal('ClearCase');
}
