import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService, AuthUser } from './auth-service';

export function homeUrl(user: AuthUser): string {
  if (user.roles.includes('Supervisor')) return '/agent/escalations';
  if (user.roles.includes('Agent')) return '/agent/disputes';
  return user.roles.includes('Customer') ? '/' : '/unauthorized';
}

export function loginDestination(user: AuthUser, returnUrl: string | null): string {
  // Allow only known routes for this role, not arbitrary URLs or auxiliary outlets.
  const path = returnUrl?.split(/[?#]/, 1)[0] ?? '';
  const customerPath = /^\/(?:disputes(?:\/[0-9a-f-]{36})?|transactions\/[0-9a-f-]{36})?$/i.test(path);
  const allowed = (customerPath && user.roles.includes('Customer'))
    || (path === '/agent/disputes' && user.roles.includes('Agent'))
    || (path === '/agent/escalations' && user.roles.includes('Supervisor'));
  return allowed && returnUrl !== null && !/[\\\u0000-\u001f]/.test(returnUrl) ? returnUrl : homeUrl(user);
}

export async function authGuard(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Promise<boolean | UrlTree> {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.restore();
  const user = auth.user();
  if (user === null) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  return route.data['role'] === undefined || user.roles.includes(route.data['role'])
    ? true : router.createUrlTree(['/unauthorized']);
}

export const loginGuard: CanActivateFn = async route => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.restore();
  const user = auth.user();
  return user === null ? true : router.parseUrl(loginDestination(user, route.queryParamMap.get('returnUrl')));
};
