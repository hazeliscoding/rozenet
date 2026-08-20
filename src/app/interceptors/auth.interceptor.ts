import { HttpInterceptorFn } from '@angular/common/http';

/** Attaches the stored bearer token to every API request. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('rozenet_token');
  if (token) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(req);
};
