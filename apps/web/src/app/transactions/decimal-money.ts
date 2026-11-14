import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'decimalMoney' })
export class DecimalMoney implements PipeTransform {
  transform(amount: string, currency: string): string {
    const parts = /^(-?\d+)\.(\d{2})$/.exec(amount);
    if (parts === null || !/^[A-Z]{3}$/.test(currency)) {
      throw new Error('Expected a two-decimal monetary amount and ISO currency code.');
    }
    // Keep the API's decimal string intact; Number/CurrencyPipe can round away cents.
    return `${currency} ${parts[1].replace(/\B(?=(\d{3})+(?!\d))/g, ',')}.${parts[2]}`;
  }
}
