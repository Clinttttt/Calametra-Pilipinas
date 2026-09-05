import { type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { APP_CONFIG } from '../config/app-config';

/**
 * Rewrites root-relative `/api/...` URLs onto the configured API host.
 *
 * Lets every service request `/api/earthquakes` without knowing where the API
 * lives, which keeps the base URL a deployment concern rather than something
 * threaded through constructors. Absolute URLs pass through untouched, so tile
 * and basemap requests are unaffected.
 */
export const apiBaseUrlInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) {
    return next(request);
  }

  const { apiBaseUrl } = inject(APP_CONFIG);

  return next(request.clone({ url: `${apiBaseUrl}${request.url}` }));
};
