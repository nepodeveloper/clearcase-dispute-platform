import { Component, ElementRef, forwardRef, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALIDATORS, NG_VALUE_ACCESSOR, ValidationErrors, Validator } from '@angular/forms';

const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
let nextDateFieldId = 0;

/** Formats an ISO calendar date (yyyy-MM-dd) as dd MMM yyyy without any time-zone shift. */
export function formatCalendarDate(iso: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  return match === null ? '' : `${match[3]} ${months[Number(match[2]) - 1]} ${match[1]}`;
}

/** Parses dd MMM yyyy (or ISO yyyy-MM-dd) into an ISO calendar date, or null when it is not a real date. */
export function parseCalendarDate(text: string): string | null {
  const value = text.trim();
  const iso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  const display = /^(\d{1,2})[\s-]+([A-Za-z]{3})[\s-]+(\d{4})$/.exec(value);
  let parts: [number, number, number];
  if (iso !== null) parts = [Number(iso[1]), Number(iso[2]), Number(iso[3])];
  else if (display !== null) parts = [Number(display[3]),
    months.findIndex(name => name.toLowerCase() === display[2].toLowerCase()) + 1, Number(display[1])];
  else return null;
  const [year, month, day] = parts;
  const date = new Date(Date.UTC(year, month - 1, day));
  if (month < 1 || date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day)
    return null;
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/** Date input shown as dd MMM yyyy with a native calendar picker; the form value stays ISO yyyy-MM-dd. */
@Component({
  selector: 'cc-date-field',
  host: { class: 'cc-field cc-date-field' },
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DateField), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => DateField), multi: true },
  ],
  template: `
    <label [for]="inputId">{{ label() }}</label>
    <span class="cc-date-field__control">
      <input #textInput class="cc-control" type="text" [id]="inputId" [attr.name]="inputName() || null" autocomplete="off"
        placeholder="dd MMM yyyy" [value]="text()" [disabled]="disabled()"
        [attr.aria-invalid]="ariaInvalid() || (touched() && invalid()) ? 'true' : null"
        [attr.aria-describedby]="ariaDescribedBy()"
        (input)="typed(textInput.value)" (blur)="blurred()">
      <button class="cc-date-field__button" type="button" [disabled]="disabled()" (click)="openPicker()"
        [attr.aria-label]="'Open calendar for ' + label().toLowerCase()">
        <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" width="18" height="18">
          <path fill="currentColor" d="M7 2h2v2h6V2h2v2h3a1 1 0 0 1 1 1v15a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h3V2Zm12 8H5v9h14v-9ZM5 8h14V6H5v2Z" />
        </svg>
      </button>
      <input #picker class="cc-date-field__picker" type="date" tabindex="-1" aria-hidden="true"
        [value]="value()" [disabled]="disabled()" (change)="picked(picker.value)">
    </span>`,
})
export class DateField implements ControlValueAccessor, Validator {
  readonly label = input.required<string>();
  readonly inputName = input('');
  readonly ariaInvalid = input(false);
  readonly ariaDescribedBy = input<string | null>(null);
  protected readonly inputId = `cc-date-field-${++nextDateFieldId}`;
  protected readonly value = signal('');
  protected readonly text = signal('');
  protected readonly invalid = signal(false);
  protected readonly touched = signal(false);
  protected readonly disabled = signal(false);
  private readonly textInput = viewChild.required<ElementRef<HTMLInputElement>>('textInput');
  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');
  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(value: unknown): void {
    const iso = typeof value === 'string' ? parseCalendarDate(value) ?? '' : '';
    this.value.set(iso);
    this.text.set(formatCalendarDate(iso));
    this.invalid.set(false);
  }

  registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(disabled: boolean): void { this.disabled.set(disabled); }

  validate(): ValidationErrors | null {
    return this.invalid() ? { calendarDate: true } : null;
  }

  protected typed(text: string): void {
    const iso = text.trim() === '' ? '' : parseCalendarDate(text);
    this.text.set(text);
    this.invalid.set(iso === null);
    this.value.set(iso ?? '');
    this.onChange(iso ?? '');
  }

  protected blurred(): void {
    if (!this.invalid()) this.text.set(formatCalendarDate(this.value()));
    this.touched.set(true);
    this.onTouched();
  }

  protected picked(iso: string): void {
    this.writeValue(iso);
    this.onChange(this.value());
    this.onTouched();
  }

  protected openPicker(): void {
    try {
      this.picker().nativeElement.showPicker();
    } catch {
      // Browsers without showPicker support still accept a typed date.
      this.textInput().nativeElement.focus();
    }
  }
}
