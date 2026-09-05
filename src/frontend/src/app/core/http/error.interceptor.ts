import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import { NotificationStore } from '../notifications/notification-store';
import { type ProblemDetails } from '../api/contracts';

/**
 * Turns transport failures into a single, readable shape and surfaces them once.
 *
 * The API returns RFC 9457 problem responses carrying a stable `code`, so this
 * interceptor prefers that over inventing its own messages. Two categories are
 * handled specially:
 *
 *   • 400 with an `errors` dictionary is a validation failure. It is passed
 *     through without a toast, because the form or control that produced it can
 *     render field-level messages far better than a global notification.
 *
 *   • 429 is rate limiting. The read API is intentionally anonymous, so this is
 *     a condition real users can genuinely hit, and it deserves a message that
 *     says what to do rather than a generic failure.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const notifications = inject(NotificationStore);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const problem = (error.error ?? {}) as ProblemDetails;

      // Validation failures belong to the caller, not to a global toast.
      const isValidationFailure = error.status === 400 && problem.errors !== undefined;

      if (!isValidationFailure) {
        notifications.error(describe(error, problem));
      }

      return throwError(() => error);
    }),
  );
};

function describe(error: HttpErrorResponse, problem: ProblemDetails): string {
  if (error.status === 0) {
    return 'Cannot reach the Calametra service. Check your connection and try again.';
  }

  if (error.status === 429) {
    return 'Too many requests. Pause for a moment before continuing.';
  }

  if (error.status >= 500) {
    return problem.traceId
      ? `Something went wrong on our side. Reference: ${problem.traceId}`
      : 'Something went wrong on our side. Please try again.';
  }

  return problem.detail ?? problem.title ?? 'The request could not be completed.';
}
