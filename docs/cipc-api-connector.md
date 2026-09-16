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

## Confirmed annual-returns filing-history contract

CIPC APIVerse describes **Filing history** as returning annual-returns filing history and documents:

```text
POST https://apim.cipc.co.za/companies-api/v1/filing-history
Content-Type: application/json
Authorization: Bearer <access token>
```

Request body:

```json
{
  "enterprise_number": "<company registration number>"
}
```

The public response example exposes a `filings_history_list` property. The public page does not document the internal structure or status vocabulary of that value well enough to safely infer whether annual returns are currently up to date. Therefore the endpoint/request contract is configured for `cipc_annual_returns`, but `OutcomeProperty`, `PassValues` and `FailValues` remain intentionally empty until an actual authorised 200 response is captured and its semantics are verified.

This means the portal will not claim `authority_verified` for annual returns merely because the Filing History endpoint responded successfully.

## Confirmed supporting Company Profile API

CIPC also documents a broader read-only company profile operation:

```text
POST https://apim.cipc.co.za/companies-api/v1/company-profile
Content-Type: application/json
Authorization: Bearer <access token>
```

with the same enterprise-number request shape. Its documented response contains broad sections such as `Company`, `Directors`, `History`, `Registered-Office-Address`, `Registered-Postal-Address`, and `Secretaries`.

This is useful supporting data for the portal, but it is not currently used as a compliance pass/fail check. The narrower Basic Company Information and Filing History operations remain the preferred sources for company registration and annual-return verification respectively.

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

The confirmed annual-return request settings are:

```text
CipcApi__Checks__cipc_annual_returns__EndpointTemplate=/companies-api/v1/filing-history
CipcApi__Checks__cipc_annual_returns__RequestMethod=POST
CipcApi__Checks__cipc_annual_returns__RegistrationNumberBodyProperty=enterprise_number
```

After testing real CIPC responses, configure result mappings only from verified fields/status values:

```text
CipcApi__Checks__cipc_registration__OutcomeProperty=<verified dotted/array JSON property path>
CipcApi__Checks__cipc_registration__EvidenceProperty=<optional verified reference property>
CipcApi__Checks__cipc_registration__PassValues__0=<exact verified CIPC status value>
CipcApi__Checks__cipc_registration__FailValues__0=<exact verified CIPC status value>

CipcApi__Checks__cipc_annual_returns__OutcomeProperty=<verified property derived from filing history>
CipcApi__Checks__cipc_annual_returns__EvidenceProperty=<optional verified filing reference/property>
CipcApi__Checks__cipc_annual_returns__PassValues__0=<exact verified CIPC value>
CipcApi__Checks__cipc_annual_returns__FailValues__0=<exact verified CIPC value>
```

Response paths support nested objects and zero-based array indexes, for example `Enterprise.0.enterprise_status_description`, but only configure that path after confirming the actual returned payload.

Beneficial ownership remains separate and must be wired from the actual BO API contract rather than inferred from the Companies API.

## Runtime flow

1. Accountant marks the CIPC check as applicable and saves the company registration number.
2. If the connector configuration for that check is complete, the UI exposes **Verify with CIPC**.
3. The backend obtains an OAuth token using the configured CIPC credentials.
4. For company registration, the backend POSTs the enterprise number to `/companies-api/v1/information`.
5. For annual returns, the backend POSTs the enterprise number to `/companies-api/v1/filing-history`.
6. Returned data is compared only to configured, verified pass/fail values.
7. A recognised result is stored as `authority_verified` with source/evidence reference, checked time, actor and next review date.
8. An unrecognised or failed provider response is not persisted as a compliance result.

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

or:

```json
{
  "version": "<current monitoring version>",
  "checkCode": "cipc_annual_returns"
}
```

The endpoint is restricted to accountant/admin users and the normal client-assignment access rules still apply.

## Production checklist

Before setting `CipcApi:Enabled` to `true`:

- confirm the CIPC application/subscription is approved;
- confirm OAuth token URL, authentication mode and scopes;
- confirm an actual 200 Basic Company Information payload;
- confirm the semantic meaning of every enterprise-status pass/fail value;
- confirm an actual 200 Filing History payload and the structure of `filings_history_list`;
- confirm how CIPC represents an annual return that is current, outstanding or otherwise not in good standing;
- confirm the beneficial-ownership API operation separately;
- test with CIPC-approved test/sandbox data first;
- store client credentials in production secrets, not `appsettings.json`;
- verify outbound HTTPS/firewall access;
- verify logging does not capture tokens or full authority responses;
- run integration tests for success, timeout, 401/403, 404, 429, 5xx and unexpected response schemas.
