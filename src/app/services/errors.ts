import { HttpErrorResponse } from '@angular/common/http';

/** Pulls the API's { error } message out of a failed response, with a
 *  themed fallback when the den is unreachable. */
export function apiError(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) return 'the den is offline — is the api running?';
    if (err.error?.error) return err.error.error;
    return `something went wrong (${err.status}).`;
  }
  return 'something went wrong.';
}
