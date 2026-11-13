import { Injectable, signal } from '@angular/core';

export type DemoRole = 'customer' | 'agent' | 'supervisor';

export const DEMO_HOME: Record<DemoRole, string> = {
  customer: '/',
  agent: '/agent/disputes',
  supervisor: '/agent/escalations',
};

@Injectable()
export class DemoSession {
  private readonly selectedRole = signal<DemoRole | null>(null);
  readonly role = this.selectedRole.asReadonly();

  enter(role: DemoRole): void {
    this.selectedRole.set(role);
  }

  leave(): void {
    this.selectedRole.set(null);
  }
}
