import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { sessionInterceptor } from './auth/session-interceptor';
import { AgentDisputesApi } from './agent/agent-disputes-api';
import { SupervisorEscalationsApi } from './supervisor/supervisor-escalations-api';
import { OPERATIONS_API } from './operations/operations-api';
import { inject } from '@angular/core';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([sessionInterceptor])),
    provideRouter(routes, withComponentInputBinding()),
    {
      provide: OPERATIONS_API,
      useFactory: () => ({
        agent: inject(AgentDisputesApi),
        supervisor: inject(SupervisorEscalationsApi),
      }),
    },
  ]
};
