import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TransactionsPage } from './transactions/transactions-page';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, TransactionsPage],
  templateUrl: './foundation.html',
  styleUrl: './foundation.css',
})
export class App {
  protected readonly title = signal('ClearCase');
}
