# Classroom API tests (Newman)

Run the 26 API checks from the repository root:

```sh
npm install
npm run test:classroom-api
```

The runner reads `BOOTSTRAP_ADMIN_EMAIL`, `BOOTSTRAP_ADMIN_PASSWORD`, `IDENTITY_API_PORT`, and `CLASSROOM_API_PORT` from the ignored local `.env`. To override credentials or URLs without editing files, set `CLASSROOM_ADMIN_EMAIL`, `CLASSROOM_ADMIN_PASSWORD`, `IDENTITY_API_BASE_URL`, or `CLASSROOM_API_BASE_URL` in the shell. Credentials and tokens are not stored in the checked-in Environment JSON; the runner creates a private temporary Newman environment and redacts secrets from the JSON report.

Start the local Identity and Classroom APIs with RabbitMQ and the seeded Classroom database before running. The suite logs in through Identity and captures access tokens and IDs from API responses. It covers session override validation, recurring-generation idempotency for an already generated date, stale and parallel stale schedule updates, and Admin rejection on Teacher/Student-only routes. These checks use existing demo records and stale or invalid requests so they do not change seeded data. The authorization checks verify that an Admin token is rejected by Teacher/Student role-restricted endpoints; successful role-specific access requires Teacher and Student credentials that are not included in the shared local configuration.

Reports are written to:

- `TestResults/newman/classroom-api.json`
- `TestResults/newman/classroom-api.junit.xml`

The command returns a non-zero exit code if Newman or any assertion fails. The Environment JSON defaults to ports `6001` and `6003`; the runner replaces them with values from `.env` when present.
