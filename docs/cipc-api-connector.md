# CIPC APIVerse connector

The Secure Client Portal supports read-only CIPC authority verification for:

- `cipc_registration` — company registration / enterprise standing
- `cipc_annual_returns` — annual-return standing
- `cipc_beneficial_ownership` — beneficial-ownership standing

The connector is disabled by default. Manual accountant-confirmed verification remains available when CIPC is not configured or unavailable.

## Safety rule

Only the backend connector can create a verification with method `authority_verified`.

The manual endpoint always creates `accountant_confirmed` records. Provider failures, timeouts, non-success HTTP responses, unreadable JSON, missing mappings, and unmapped statuses do not create a pass/fail verification.

## Confirmed CIPC company-information contract

CIPC APIVerse publicly documents the following operation under **CIPC Public Data - Commercial - v1**:

```text
POST https://apim.cipc.co.za/companies-api/v1/information
Content-Type: application/json
Authorization: Bearer <access token>
```

Request body:

```json
{
  "enterprise_number": "2020/939681/07"
}
```

The documented response schema exposes public enterprise data including:

- `enterprise_number`
- `enterprise_name`
- `enterprise_type_description`
- `registration_date`
- `enterprise_status_description`
- `financial_year_end`
- registered physical/postal address fields
- `tax_number`
- `business_start_date`

The endpoint and request shape are configured in `appsettings.json`. The connector supports POST JSON bodies and nested/array response paths.

The public schema is not used to guess compliance semantics. Before enabling `cipc_registration`, capture an actual successful response from CIPC and verify the real response shape plus the exact `enterprise_status_description` values that should be treated as pass/fail.

## CIPC onboarding

CIPC APIVerse uses OAuth 2.0 and requires subscription to its Authorisation API before the other APIs can be consumed. Obtain the application's token URL, client credentials, required scopes/authentication method and API subscription from the CIPC developer portal.

Do not guess credentials, token endpoints, status meanings or response mappings.

## Configuration

Use configuration or a secret manager. Do not commit production credentials.

Environment-variable examples:

```text
CipcApi__Enabled=true
CipcApi__TokenUrl=<CIPC token URL>
CipcApi__ApiBaseUrl=https://apim.cipc.co.za
CipcApi__ClientId=<secret/client id>
CipcApi__ClientSecret=<secret>
CipcApi__Scope=<scope if required>
CipcApi__ClientAuthenticationMethod=body
```

Set `ClientAuthenticationMethod` to `basic` only if the CIPC application requires HTTP Basic client authentication at the token endpoint.

The confirmed registration-request settings are:

```text
CipcApi__Checks__cipc_registration__EndpointTemplate=/companies-api/v1/information
CipcApi__Checks__cipc_registration__RequestMethod=POST
CipcApi__Checks__cipc_registration__RegistrationNumberBodyProperty=enterprise_number
```

After testing the real CIPC response, configure the result mapping:

```text
CipcApi__Checks__cipc_registration__OutcomeProperty=<verified dotted/array JSON property path>
CipcApi__Checks__cipc_registration__EvidenceProperty=<optional verified reference property>
CipcApi__Checks__cipc_registration__PassValues__0=<exact verified CIPC status value>
CipcApi__Checks__cipc_registration__FailValues__0=<exact verified CIPC status value>
CipcApi__Checks__cipc_registration__ReviewDays=30
```

Response paths support nested objects and zero-based array indexes, for example `Enterprise.0.enterprise_status_description`, but only configure that path after confirming the actual returned payload.

Repeat the process for `cipc_annual_returns` and `cipc_beneficial_ownership` using their actual CIPC API products/operations.

## Runtime flow

1. Accountant marks the CIPC check as applicable and saves the company registration number.
2. If the connector configuration for that check is complete, the UI exposes **Verify with CIPC**.
3. The backend obtains an OAuth token using the configured CIPC credentials.
4. For company registration, the backend POSTs the enterprise number to `/companies-api/v1/information`.
5. The returned status is compared only to configured, verified pass/fail values.
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
- confirm OAuth token URL, authentication mode and scopes;
- confirm an actual 200 company-information payload;
- confirm the semantic meaning of every pass/fail status value;
- confirm the annual-returns and beneficial-ownership API operations separately;
- test with CIPC-approved test/sandbox data first;
- store client credentials in production secrets, not `appsettings.json`;
- verify outbound HTTPS/firewall access;
- verify logging does not capture tokens or full authority responses;
- run integration tests for success, timeout, 401/403, 404, 429, 5xx and unexpected response schemas.
