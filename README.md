# HeimevernetInnlevering1

ASP.NET Core MVC application for form submissions and map resources. The form and map routes continue to use the existing `FormSubmission` and `MapSubmission` data model; this repository change migrates their persistence from SQLite to MariaDB. It does not add user accounts, authentication, authorization, or new resource-management UI.

## Prerequisites

- .NET SDK 10 for running the app locally
- Docker Engine and Docker Compose v2 for the containerized app and database

## Run with Docker Compose

Create a local environment file and replace both placeholder values with different, strong random passwords. Hexadecimal values avoid connection-string delimiter issues:

```sh
cp .env.example .env
openssl rand -hex 32
openssl rand -hex 32
```

Put the generated values in `MARIADB_ROOT_PASSWORD` and `MARIADB_APP_PASSWORD` in `.env`. `.env` is ignored by Git; do not commit it or put real passwords in tracked files.

Validate the Compose configuration and start the app and database:

```sh
docker compose config --quiet
docker compose up --build -d
docker compose ps
```

Open <http://localhost:8080>. The app waits for the MariaDB health check and applies EF Core migrations at startup. MariaDB is not published on a host port; only the app is available on port 8080. The app connects to the `mariadb` Compose service on port 3306 using the non-root `heimevernet_app` database account.

Useful operations:

```sh
docker compose logs -f heimevernet mariadb
docker compose stop
docker compose down
```

The named `mariadb-data` volume persists database files across container restarts and ordinary `docker compose down` / `up` cycles. Do not remove volumes as part of normal startup or shutdown. To verify persistence manually, submit a form and a map resource, run `docker compose restart mariadb`, wait for the database to become healthy, then check the application's data and map pages.

## Local app with MariaDB

The app requires an explicit connection configuration. It accepts either the standard `ConnectionStrings__DefaultConnection` environment variable or these component variables: `MARIADB_HOST`, `MARIADB_PORT`, `MARIADB_DATABASE`, `MARIADB_USER`, and `MARIADB_APP_PASSWORD`. There is no SQLite or local-file fallback.

To run the app on the host while using the Compose database, create an untracked `docker-compose.local.yml` containing:

```yaml
services:
  mariadb:
    ports:
      - "127.0.0.1:3307:3306"
```

Start only the database with that local-only port binding and run the web app:

```sh
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d mariadb
set -a
. ./.env
set +a
unset MARIADB_ROOT_PASSWORD
export MARIADB_HOST=127.0.0.1
export MARIADB_PORT=3307
export MARIADB_DATABASE=heimevernet
export MARIADB_USER=heimevernet_app
dotnet restore
dotnet run
```

The database port is bound only to loopback in this optional local setup. For a separately installed MariaDB, create the database and application user yourself, then provide a connection string or the component variables above. The server should be MariaDB 10.11.19 for the same tested configuration as Compose.

## Troubleshooting

- If Compose reports a missing password variable, check that `.env` exists and both values have been replaced.
- If the app cannot connect, inspect `docker compose ps` and `docker compose logs heimevernet mariadb`; confirm the database is healthy and that the app password matches the account initialized in the existing database volume.
- Changing a password in `.env` does not change credentials already stored in a MariaDB data volume. Update the database account deliberately rather than deleting the volume.
- If migration application fails, inspect the app logs and preserve a database backup before attempting any schema repair.

## Database provider and schema

The project targets `net10.0`. Pomelo's published `9.0.0` compatibility documentation specifies EF Core 9.x and .NET 8.0 or later, and lists MariaDB 10.11 among its tested servers. The published provider line does not support EF Core 10, so this project aligns its EF Core design dependency to 9.0.22 and uses Pomelo 9.0.0 while retaining the .NET 10 target. Do not mix the EF Core 10 packages with this provider.

- [Pomelo 9.0.0 compatibility and setup](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/tree/9.0.0#compatibility)
- [Pomelo 9.0.0 NuGet package](https://www.nuget.org/packages/Pomelo.EntityFrameworkCore.MySql/9.0.0)
- [MariaDB 10.11 Docker image tags](https://hub.docker.com/_/mariadb)

Compose pins `mariadb:10.11.19`; the EF provider's `MariaDbServerVersion` is pinned to the same server version. The initial migration creates the two existing submission tables with integer identity keys, existing string bounds and nullability, `double` coordinates, `datetime(6)` timestamps, and `utf8mb4` text storage.

Create future schema changes as EF migrations rather than using `EnsureCreated` or hand-written startup DDL. Install the EF tool matching the EF design package, set `ConnectionStrings__DefaultConnection` to a valid MariaDB connection string, then run:

```sh
dotnet tool install --global dotnet-ef --version 9.0.22
dotnet ef migrations add DescribeYourChange
```

Review and commit the generated migration and model snapshot. The app applies committed migrations at startup; invalid configuration or an unavailable database produces a clear startup failure after a bounded readiness check.

## Integration smoke tests

The opt-in test project exercises migrations and real MariaDB persistence, including Norwegian characters, nullable form locations, map coordinates and timestamps, and map-resource create/read/update/delete. Its source is excluded from the web project's compile glob.

Run against a new, isolated Compose project name to exercise an empty database and fresh schema without touching the normal application volume. If `heimevernet-smoke` was used before, choose a different project name; Compose keeps named volumes after `down`.

```sh
docker compose -p heimevernet-smoke --profile integration run --build --rm mariadb-integration-tests
docker compose -p heimevernet-smoke down
```

The test container connects to MariaDB over the internal Compose network; the database port is not exposed. The test removes the rows it creates. Restart persistence can also be checked manually using the steps above. These checks cover this persistence migration only; they are not a substitute for the project's separate authentication, security, usability, or full system testing requirements.

## Existing SQLite data

Starting this Compose setup creates a new MariaDB database. It does **not** import records from a previous `Data/heimevernet.db` file or the old `heimevernet-data` Docker volume. Existing records are not deleted by this migration, and no automatic conversion is performed.

Before changing or removing an old SQLite deployment, stop the old app and make a separate backup. For a local database file, SQLite's backup command is:

```sh
sqlite3 Data/heimevernet.db ".backup 'heimevernet.sqlite.backup'"
```

For an existing Docker container, copy its database file after stopping the app:

```sh
docker cp <old-app-container>:/app/Data/heimevernet.db ./heimevernet.sqlite.backup
```

Confirm the backup exists and can be opened before proceeding. Keep the old named volume until its data has been reviewed and any separately planned transfer has been verified. Moving SQLite records to MariaDB requires an explicit, separately tested transfer; it is not part of normal application startup.

## AI assistance

GitHub Copilot was used for this MariaDB migration to inspect the existing persistence and Docker configuration, check provider compatibility, draft the provider configuration and EF migration, and prepare integration coverage and operations documentation. Prompts in this work asked for the SQLite-to-MariaDB change, EF/provider version verification, Docker readiness and persistence, and focused migration tests. The resulting changes were reviewed and validated with restore/build and MariaDB-backed checks where available. This note describes AI assistance for this migration only; it does not make claims about earlier work by the group.
