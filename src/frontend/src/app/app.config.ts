import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  type ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';

import { APP_CONFIG, DEFAULT_APP_CONFIG } from './core/config/app-config';
import { apiBaseUrlInterceptor } from './core/http/api-base-url.interceptor';
import { errorInterceptor } from './core/http/error.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // Zoneless change detection. The map emits a high rate of events during pan
    // and zoom, and Zone.js would schedule a change-detection pass for each one.
    // Signals make those updates explicit instead, which is what keeps the
    // timeline and marker layers responsive while the camera is moving.
    provideZonelessChangeDetection(),

    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
    ),

    provideHttpClient(
      withFetch(),
      // Order matters: the base URL is applied before the error handler wraps
      // the response, so a failure is reported against its resolved URL.
      withInterceptors([apiBaseUrlInterceptor, errorInterceptor]),
    ),

    { provide: APP_CONFIG, useValue: DEFAULT_APP_CONFIG },
  ],
};
