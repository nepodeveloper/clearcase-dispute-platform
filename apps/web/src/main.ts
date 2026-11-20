import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { applyStoredTheme } from './app/theme/theme-service';

applyStoredTheme();

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
