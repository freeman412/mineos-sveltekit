# Azure OAuth (future)

## Goal
Add a full Azure OAuth flow for authenticating MineOS users, following admin-approved linking rules.

## Scope
- Implement Azure AD login flow (OIDC Authorization Code + PKCE).
- Store tenant/app settings in system settings or configuration.
- Map Azure user identities to existing MineOS users that were pre-approved by admin.
- Add audit logging for login/link events.

## Acceptance
- Admin can enable/disable Azure OAuth in settings.
- OAuth sign-in works end-to-end and issues MineOS JWT tokens.
- Users can only sign in if admin has pre-approved their identity.
- Clear error messaging for unapproved or unmatched identities.
