# CIPC APIVerse connector

The Secure Client Portal supports read-only CIPC authority verification for:

- `cipc_registration` — company registration / enterprise standing
- `cipc_annual_returns` — annual-return standing
- `cipc_beneficial_ownership` — beneficial-ownership standing

The connector is disabled by default. Manual accountant-confirmed verification remains available when CIPC is not configured or unavailable.

## Safety rule

Only the backend connector can create a verification with method `authority_verified`.

The manual endpoint always creates `accountant_confirmed` records. Provider failures, timeouts, non-success HTTP responses, unreadable JSON, missing mappings, and unmapped statuses do not create a pass/fail verification.

## CIPC onboarding

CIPC APIVerse documents OAuth 2.0 authentication and separate Companies and Beneficial Ownership APIs. Obtain an approved application/subscription and the exact token URL, API base URL, endpoint paths, response fields and permitted status values from the CIPC developer portal before enabling the connector.

Do not guess endpoint paths or response mappings.

## Configuration

Use configuration or a secret manager. Do not commit production credentials.

Environment-variable examples:

```text
CipcApi__Enabled=true
CipcApi__TokenUrl=<CIPC token URL>
CipcApi__ApiBaseUrl=<CIPC API base URL>
CipcApi__ClientId=<secret/client id>
CipcApi__ClientSecret=<secret>
CipcApi__Scope=<scope if required>
CipcApi__ClientAuthenticationMethod=body
```

Set `ClientAuthenticationMethod` to `basic` only if the CIPC application requires HTTP Basic client authentication at the token endpoint.

Each check must also have a verified endpoint and response mapping, for example:

```text
CipcApi__Checks__cipc_registration__EndpointTemplate=<path containing {registrationNumber}>
CipcApi__Checks__cipc_registration__OutcomeProperty=<dotted JSON property path>
CipcApi__Checks__cipc_registration__EvidenceProperty=<optional dotted JSON reference property>
CipcApi__Checks__cipc_registration__PassValues__0=<exact CIPC pass value>
CipcApi__Checks__cipc_registration__FailValues__0=<exact CIPC fail value>
CipcApi__Checks__cipc_registration__ReviewDays=30
```

Repeat the mapping for `cipc_annual_returns` and `cipc_beneficial_ownership` using the actual fields returned by the subscribed APIs.

`OutcomeProperty` supports a simple dotted object path such as `data.status`. Do not configure a field unless its meaning has been verified against the CIPC API documentation for your subscription.

## Runtime flow

1. Accountant marks the CIPC check as applicable and saves the company registration number.
2. If the connector configuration for that check is complete, the UI exposes **Verify with CIPC**.
3. The backend obtains an OAuth token using the configured CIPC credentials.
4. The backend calls the configured read-only endpoint.
5. The returned status is compared only to the configured pass/fail values.
6. A recognised result is stored as `authority_verified` with source/evidence reference, checked time, actor and next review date.
7. An unrecognised or failed provider response is not persisted as a compliance result.

## API endpoint in this portal

```text
POST /api/compliance/monitoring/{clientId}/authority-verifications/cipc
```

Request:

```json
{
  "version": "<current monitoring version>",
  "checkCode": "cipc_registration"
}
```

The endpoint is restricted to accountant/admin users and the normal client-assignment access rules still apply.

## Production checklist

Before setting `CipcApi:Enabled` to `true`:

- confirm the CIPC application/subscription is approved;
- confirm OAuth token authentication mode and scopes;
- confirm the exact endpoint for each enabled check;
- confirm the semantic meaning of every pass/fail value;
- test with CIPC-approved test/sandbox data first;
- store client credentials in production secrets, not `appsettings.json`;
- verify outbound HTTPS/firewall access;
- verify logging does not capture tokens or full authority responses;
- run integration tests for success, timeout, 401/403, 404, 429, 5xx and unexpected response schemas.
