# Phase 2: transport and browser session security

Recommended layout: serve the React frontend at one public HTTPS origin and proxy
`/api` on that same origin to this API. Set the frontend `VITE_API_BASE_URL` to that
origin (without `/api`), `PortalLinks__FrontendBaseUrl` to the frontend URL, and
`Cors__AllowedOrigins__0` to the exact origin. This preserves host-only,
`SameSite=Strict` session cookies. Separate subdomains also require HTTPS and must
be on the same registrable site. Unrelated hosting domains are not supported by
the current Strict cookie policy; CORS does not override cookie restrictions.

## Proxy configuration

- `Security__TrustedProxies__0`, `__1`, etc.: actual reverse proxy IP addresses.
  Defaults trust only `127.0.0.1` and `::1` for a local IIS/Nginx proxy.
- `Security__TrustedNetworks__0`, etc.: specific proxy CIDRs if static IPs are not
  available. Universal `/0` networks are rejected.
- `Security__ForwardLimit`: number of trusted proxy hops, default 1, maximum 5.
- `Security__HttpsPort`: public HTTPS port, default 443.
- Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`: it bypasses explicit trust
  and is rejected outside Development.

The proxy must preserve the public Host header and replace/append X-Forwarded-For
and X-Forwarded-Proto correctly. Unknown proxies cannot change the API's client IP
or scheme. Restrict direct network access to the API to the proxy. For Cloudflare
or multiple hops, configure the actual trusted network path; do not blindly trust
arbitrary forwarded headers. HTTPS GET/HEAD redirects use 308; non-HTTPS mutations
are rejected. HSTS is 30 days without preload or blanket subdomain enforcement.

## CSRF protocol

1. GET `/api/auth/csrf` with credentials to obtain `requestToken` and its HttpOnly
   anti-forgery cookie. The response is never cacheable.
2. Send `X-CSRF-Token: <requestToken>` plus cookies on every POST/PUT/PATCH/DELETE,
   including login, logout, refresh and multipart uploads.
3. Fetch another token after an authentication change. Tokens are identity-bound.

The frontend shares a token bootstrap across concurrent writes, keeps the token in
memory and retries at most once after the middleware's explicit `CSRF_INVALID`
403. Other failures are never automatically replayed. Requests from origins outside
the configured allowlist and API origin are rejected even with a valid token.
Development uses the same CSRF protocol, with HTTP-compatible local cookies.

API responses have no-store, nosniff, deny-framing, no-referrer and restrictive CSP
headers. Unhandled errors return generic JSON with a trace ID; exception details
remain in server logs. The Vite frontend build emits its own CSP and security-header
files. Configure the static host to send those headers; API headers do not protect
HTML served by another host. Development Swagger retains its local scripts.

## Before deploying

Verify the final domain, TLS certificate and actual proxy IPs in staging. Check
login, refresh, logout, document upload and PDF previews in a real browser. The
automated tests cover the middleware and client protocol with simulated HTTPS;
they cannot verify an unprovisioned public domain, proxy or certificate.
