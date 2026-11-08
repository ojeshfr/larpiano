# Local integration review

No GitHub push, release publication, Portways deployment, or production database modification was performed.

## Email

The source provides SMTP and a mock provider, with verification and password-recovery templates. Production configuration and live email delivery could not be inspected because the signed-in Portways browser was unavailable. Required SMTP settings are `EMAIL_PROVIDER=smtp`, `EMAIL_HOST`, `EMAIL_PORT`, `EMAIL_FROM`, and provider-appropriate `EMAIL_USER` / `EMAIL_PASS` (or `EMAIL_API_KEY` / `EMAIL_SECRET`). `EMAIL_FROM_NAME` and `EMAIL_USE_TLS` are optional. The sender domain must be verified with the provider. Do not place credentials in source control. The backend also requires a strong `JWT_SECRET` and an independently configured `ADMIN_TOKEN`. Configuration presence is not proof of delivery.


### Gmail SMTP configuration

The existing SMTP transport supports Gmail on port 587 with STARTTLS. Configure `EMAIL_PROVIDER=smtp`, `EMAIL_HOST=smtp.gmail.com`, `EMAIL_PORT=587`, `EMAIL_USE_TLS=true`, `EMAIL_USER` and `EMAIL_FROM` as the sending Gmail address, and `EMAIL_PASS` as a Google app password. Gmail app passwords require 2-Step Verification and may be unavailable on some accounts. Enter the secret directly into the hosting environment; never commit it or paste it into chat. These settings and the transport have been tested with a mocked SMTP server only, not with Gmail or Portways networking.

## Storage

The code supports S3-compatible storage using `STORAGE_ENDPOINT`, `STORAGE_BUCKET`, `STORAGE_ACCESS_KEY`, `STORAGE_SECRET_KEY` and optional `STORAGE_REGION`; development falls back to local files. The configured provider, access permissions, usage and live bucket connectivity were not inspected. Admin downloads require authorization in application code. There is no evidence here about bucket-level public access; it must be checked separately. No objects were uploaded, overwritten, or deleted.

## Remaining blockers

- The authenticated presence implementation needs automatic access-token refresh while the app remains open, real PostgreSQL integration tests, multi-device/revocation and concurrency tests, and opt-out/logout verification.
- Authentication expiry needs a consistent UI, server inputs and the remaining dynamic templates require a complete security review, and the in-memory rate limiter is per process, not distributed.
- Some UI features remain preview-only and search is limited; pagination and thorough loading/error states remain unfinished. Do not treat the dashboard as fully connected.
- Isolated PostgreSQL migration and rollback tests remain outstanding; see admin-schema-review.md. No production schema was inspected.
- Real email delivery, live bucket security, guest and authenticated desktop flows, and full overlay tests remain unverified.
