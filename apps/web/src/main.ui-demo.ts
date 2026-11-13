import { provideBrowserGlobalErrorListeners } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { DemoApp } from './app/ui-demo/demo-app';
import { demoRoutes } from './app/ui-demo/demo.routes';
import { DemoSession } from './app/ui-demo/demo-session';
import { DemoWorkspace } from './app/ui-demo/demo-workspace';

bootstrapApplication(DemoApp, {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(demoRoutes, withComponentInputBinding()),
    DemoSession,
    DemoWorkspace,
  ],
}).catch(error => console.error(error));
