# Administrator ownership

Ordinary administrators can manage users and assign existing roles. Creating, editing,
activating or deactivating role definitions additionally requires the installation owner.

Set `Administration__OwnerUserId` in the API deployment environment (or
`Administration:OwnerUserId` in local .NET user secrets) to the immutable user ID of
an existing, active administrator. Obtain the ID from the user directory. Use an ID,
not an email address. Do not commit real account identifiers to appsettings files.
If unset, malformed, inactive or not an administrator, all role writes are denied.
Existing role reads and day-to-day user management continue normally.

This value is not a firm setting and cannot be changed through the settings API.
To transfer ownership, first establish and verify the replacement administrator,
then change this deployment setting and restart the API. Owner disable/demotion
is denied until that transfer. Database maintenance remains an out-of-band operation.

The standard administrator role cannot be deactivated or lose core login, user and
role-management permissions. Disabling/demoting the final active administrator is
blocked across both administration APIs, even if another administrator is invited,
disabled or assigned an unusable role. Role writes and user status/role changes use
the same SQL Server application lock to serialize concurrent changes across API instances.
