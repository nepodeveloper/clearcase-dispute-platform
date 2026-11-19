import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEnZa from '@angular/common/locales/en-ZA';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { sessionInterceptor } from './auth/session-interceptor';

registerLocaleData(localeEnZa);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: LOCALE_ID, useValue: 'en-ZA' },
    provideHttpClient(withInterceptors([sessionInterceptor])),
    provideRouter(routes, withComponentInputBinding()),
  ]
};
