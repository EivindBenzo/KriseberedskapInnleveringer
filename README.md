# HeimevernetInnlevering1

ASP.NET Core MVC-applikasjon (`net10.0`) med ASP.NET Core Identity, roller, behovsinnmelding og kartressurser lagret i MariaDB. EF Core og Identity bruker 9.0.22 sammen med Pomelo 9.0.0.

## Innlogging og tilgang

Forsiden er offentlig. Velg «Registrer» for å opprette en konto; nye kontoer får alltid rollen `Bruker`. På innloggingssiden velges «Bruker» eller «Administrator (full tilgang)». Begge krever gyldige innloggingsopplysninger. Administratorvalget kontrollerer rollen i databasen og gir aldri i seg selv ekstra rettigheter.

| Funksjon | Bruker | Admin |
| --- | --- | --- |
| Se kart og ressursoversikt, filtrere på type | Ja | Ja |
| Registrere ressurser | Ja | Ja |
| Redigere/flytte/slette egne ressurser | Ja | Ja |
| Redigere/slette andres eller eldre eierløse ressurser | Nei | Ja |
| Melde behov og se egne innmeldinger | Ja | Ja |
| Se alle behov og databaseverifisering | Nei | Ja |
| Liste brukere og tildele/fjerne Admin | Nei | Ja |

«Skjema» er nå «Meld behov», krever innlogging og knyttes til brukeren. Gamle innmeldinger og ressurser beholdes; eierløse behov er kun synlige for administratorer. Alle ressursendringer kontrolleres på serveren. Sletting krever bekreftelse og en POST med antiforgery-token.

### Første administrator

Valgfritt: sett `ADMIN_EMAIL` og `ADMIN_PASSWORD` i din lokale `.env` før oppstart. Bruk et unikt passord på minst 12 tegn med stor/liten bokstav, tall og spesialtegn. Begge verdier må være satt for at oppstart skal opprette administratoren. Uten dem opprettes bare rollene. Bruk aldri ekte verdier i Git eller skjermbilder. En administrator kan deretter administrere roller på «Brukere»-siden.

Passord lagres som Identity-hasher i databasen. Gjentatte mislykkede innlogginger utløser midlertidig låsing. Informasjonskapsler er HttpOnly/SameSite; HTTPS gir Secure-cookie, mens HTTP fungerer i lokal Docker-utvikling. Bruk HTTPS i produksjon.

## Prerequisites

- .NET SDK 10 for running the app locally
- Docker Engine and Docker Compose v2 for the containerized app and database

## Kartikoner og tema

Kartet viser egne, lokalt lagrede SVG-symboler for Kjøretøy (bil), Maskin (gravemaskin), Drone, Generator, Personell og Utstyr (verktøykasse). Forhåndsvisningen har stiplet omriss og oppdateres når du velger ressurstype; symbolet beholdes når plasseringen flyttes. Lagrede ressurser bruker sin registrerte type, uten databaseendringer. Ukjent eller ikke valgt type vises med spørsmålstegn.

Symbolene skaleres ved zoomendring, med mindre ikoner på oversiktsnivå og begrenset minimum/maksimum. Tegnforklaringen nederst til venstre bruker nøyaktig samme ikonoppslag som markører og oversikten, og kan foldes sammen på mobil. Fullskjermknappen nederst til høyre bruker nettleserens Fullscreen API, med CSS-reserve på nettlesere som ikke støtter det. Trykk knappen igjen eller Esc for å avslutte. Begge kontrollene følger temaet og blir på kartet i fullskjerm.

«Rediger» åpner samme kart med ressursens felt utfylt. Klikk i kartet for å flytte punktet, og lagre. «Vis på kart» i oversikten fokuserer den valgte ressursen.

Nettstedet starter med lyst tema, uavhengig av operativsystemets innstilling. Knappen ved menyen bytter mellom «Mørk modus» og «Lys modus», også med tastaturet. Valget lagres i nettleseren på tvers av sider og omlasting. Hvis nettleserlagring er blokkert, virker byttet på den åpne siden; uten JavaScript brukes lyst tema. Kartfliser og søkeleverandør er uendret.

Focused frontend checks use Node.js's built-in test runner (no npm packages):

```sh
node --test tests/Frontend/*.test.cjs
dotnet build
```

Before updating an existing checkout, preserve any uncommitted CSS/logo edits (for example with a local commit or backup). Those local changes are not included here; no logo replacement is part of this update.

**Lokalt CSS-stash:** Brukeren har tidligere stashet endringer i `wwwroot/css/site.css`. Kontroller eventuelle konflikter nøye når stashet hentes tilbake. Denne oppdateringen legger nye stiler i separate CSS-filer og endrer ikke `site.css`.

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

Etter oppdatering må appen bygges på nytt: `docker compose up --build -d`. Identity-tabeller og nullable eierkoblinger legges til med en ny migrering; eksisterende tabeller/data slettes ikke. Ikke bruk `docker compose down -v`, volumfjerning eller databaseresett.

Volumet `identity-keys` lagrer ASP.NET Core Data Protection-nøkler i appcontainerens `/app/keys`, slik at innloggings- og antiforgery-cookies tåler gjenoppretting av containeren. Bevar og beskytt både dette volumet og `mariadb-data` ved sikkerhetskopiering. Nøkkelfilene er ikke kryptert i hvile; begrens tilgang til Docker-verten/volumet. Eksisterende cookies fra før denne endringen kan kreve én ny innlogging. Lokal kjøring bruker en vedvarende nøkkelmappe utenfor kildekoden; `DataProtection__KeyPath` kan overstyre plasseringen.

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

The opt-in test project exercises migrations and real MariaDB persistence, including Identity users/roles, resource ownership, Norwegian characters, nullable form locations, map coordinates and timestamps, and map-resource create/read/update/delete. Its source is excluded from the web project's compile glob. Authorization and login-choice unit tests can run without MariaDB using `dotnet test tests/MariaDbIntegrationTests/MariaDbIntegrationTests.csproj --filter 'FullyQualifiedName!~MariaDbPersistenceTests'`.

Run against a new, isolated Compose project name to exercise an empty database and fresh schema without touching the normal application volume. If `heimevernet-smoke` was used before, choose a different project name; Compose keeps named volumes after `down`.

```sh
docker compose -p heimevernet-smoke --profile integration run --build --rm mariadb-integration-tests
docker compose -p heimevernet-smoke down
```

The test container connects to MariaDB over the internal Compose network; the database port is not exposed. The test removes the rows it creates. Restart persistence can also be checked manually using the steps above. These persistence and focused authorization checks do not replace browser or full system testing.

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

GitHub Copilot hjalp med Identity/roller, eierkontroll og CRUD, additive migreringer, kartkontroller, tester og denne dokumentasjonen. Faktisk utførte valideringer og begrensninger rapporteres i PR-en; AI-generert kode må fortsatt gjennomgås av prosjektgruppen.

GitHub Copilot also assisted with the local resource SVGs, allowlisted marker mapping, light/dark theme controls and styling, and focused frontend checks in this update. Validation results and any browser-testing limitations are recorded in the pull request.

GitHub Copilot was used for this MariaDB migration to inspect the existing persistence and Docker configuration, check provider compatibility, draft the provider configuration and EF migration, and prepare integration coverage and operations documentation. Prompts in this work asked for the SQLite-to-MariaDB change, EF/provider version verification, Docker readiness and persistence, and focused migration tests. The resulting changes were reviewed and validated with restore/build and MariaDB-backed checks where available. This note describes AI assistance for this migration only; it does not make claims about earlier work by the group.
