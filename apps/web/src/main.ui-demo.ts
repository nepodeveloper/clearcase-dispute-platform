import { provideBrowserGlobalErrorListeners } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { DemoApp } from './app/ui-demo/demo-app';
import { demoRoutes } from './app/ui-demo/demo.routes';
import { DemoSession } from './app/ui-demo/demo-session';
import { DemoWorkspace } from './app/ui-demo/demo-workspace';

if (document.querySelector('cc-demo-root') === null) {
  document.body.appendChild(document.createElement('cc-demo-root'));
}

bootstrapApplication(DemoApp, {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(demoRoutes, withComponentInputBinding()),
    DemoSession,
    DemoWorkspace,
  ],
}).catch(error => console.error(error));
