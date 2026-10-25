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

Primærmenyen viser Hjem, Kart og Verifiser (sistnevnte krever Admin), samt konto og eventuell brukeradministrasjon. Skjema/Data-lenkene er fjernet fra meny og forside, ikke fra databasen eller backend: `/Home/Form` og `/Home/Data` er fortsatt tilgjengelige for innloggede brukere. Gamle innmeldinger og ressurser beholdes; eierløse behov er kun synlige for administratorer. Alle ressursendringer kontrolleres på serveren. Kartets ressurskort har «Rediger»/«Slett» for eier og Admin. Sletting krever bekreftelse og en POST med antiforgery-token.

### Første administrator

Valgfritt: sett `ADMIN_EMAIL` og `ADMIN_PASSWORD` i din lokale `.env` før oppstart. Bruk et unikt passord på minst 12 tegn med stor/liten bokstav, tall og spesialtegn. Begge verdier må være satt for at oppstart skal opprette administratoren. Uten dem opprettes bare rollene. Bruk aldri ekte verdier i Git eller skjermbilder. En administrator kan deretter administrere roller på «Brukere»-siden.

Passord lagres som Identity-hasher i databasen. Gjentatte mislykkede innlogginger utløser midlertidig låsing. Informasjonskapsler er HttpOnly/SameSite; HTTPS gir Secure-cookie, mens HTTP fungerer i lokal Docker-utvikling. Bruk HTTPS i produksjon.

## Prerequisites

- .NET SDK 10 for running the app locally
- Docker Engine and Docker Compose v2 for the containerized app and database

## Kartikoner og tema

Kartet viser egne, lokalt lagrede SVG-symboler for Kjøretøy (bil), Maskin (gravemaskin), Drone, Generator, Personell og Utstyr (verktøykasse). Ingen nye koordinater eller forhåndsvisning velges ved oppstart eller etter innsending. Klikk i kartet eller velg et søkeresultat først. Forhåndsvisningen har stiplet omriss og oppdateres når du velger ressurstype; typevalg alene lager aldri en markør. Ukjent/ikke valgt type kan vises med spørsmålstegn etter eksplisitt plassering. Lagrede ressurser bruker sin registrerte type, uten databaseendringer.

Symbolene skaleres ved zoomendring, med mindre ikoner på oversiktsnivå og begrenset minimum/maksimum. Tegnforklaringen nederst til venstre bruker samme ikoner, med seks avkrysningsbokser som kun skjuler/viser lagrede markører. Alle kan slås av; verken databasen eller valgt forhåndsvisning endres. Valget beholdes ved zoom/fullskjerm. «Vis på kart» gjenaktiverer ressursens type ved behov. Tegnforklaringen er større kun i fullskjerm og kan foldes sammen på mobil. Firehjørnesymbolet nederst til høyre bruker nettleserens Fullscreen API, med CSS-reserve. Trykk knappen igjen eller Esc for å avslutte. Kontrollene følger temaet og støtter tastatur.

«Rediger» åpner samme kart med eksisterende punkt og felt utfylt, uten dobbel markør. Klikk i kartet for å flytte punktet, og lagre. Servervalidering beholder et gyldig valgt punkt ved feil.

Ressursnavn er brukerens tittel. Stedsnavn er en skrivebeskyttet oppsummering av valgt punkt og bestemmes på serveren; innsendt stedsnavn stoles ikke på. Reverse-oppslag bruker nærliggende adresse/tettsted (høyst 1 km fra punktet), ellers ærlige koordinater, også til havs eller ved nettverksfeil. Et flyttet punkt får straks koordinatetikett, og sene svar fra et tidligere punkt ignoreres.

### Stedsoppslag og personvern

Bysøk utføres kun ved «Søk»/Enter, aldri mens du skriver. Reverse-oppslag skjer kun etter punktvalg/lagring, aldri ved panorering/zoom. Den autentiserte serveren bruker [Nominatims offisielle bruksvilkår](https://operations.osmfoundation.org/policies/nominatim/): identifiserende User-Agent, maksimalt én ekstern forespørsel per sekund for hele appinstansen, ingen arbeidskø, tids-/svarstørrelsesgrenser og begrenset hurtigbuffer (512 oppføringer, treff i 24 timer, manglende treff i 30 sekunder). Ingen nye API-nøkler kreves. Overbelastning/feil gir koordinatfallback og blokkerer ikke lagring.

**Personvern:** Valgte koordinater og søketekst sendes til OpenStreetMaps Nominatim via serveren, ikke kontoopplysninger eller ressursnavn. Kartfliser lastes fortsatt direkte fra OpenStreetMap. Ikke søk etter konfidensielle/personlige opplysninger. For sensitive plasseringer kan eksterne stedsoppslag deaktiveres med `Geocoding__Enabled=false`; koordinatetiketter og manuell kartplassering fungerer fortsatt. `Geocoding__BaseUrl` (HTTPS) og `Geocoding__UserAgent` kan overstyres av operatøren uten kodeendring. Offentlig Nominatim skal kun brukes fra én appinstans; bruk egen/annen egnet tjeneste eller felles global begrensning før skalering til flere instanser. Dette er ikke en generell URL-proxy.

Nettstedet starter med lyst tema, uavhengig av operativsystemets innstilling. Knappen ved menyen bytter mellom «Mørk modus» og «Lys modus», også med tastaturet. Valget lagres i nettleseren på tvers av sider og omlasting. Hvis nettleserlagring er blokkert, virker byttet på den åpne siden; uten JavaScript brukes lyst tema.

Focused frontend checks use Node.js's built-in test runner (no npm packages):

```sh
node --test tests/Frontend/*.test.cjs
dotnet build
```

Headeren bruker den allerede versjonerte `wwwroot/images/forsvaret-logo.jpg`, ikke en lokal/usporet PNG. JPG-en har emblem helt ut mot bildekantene, så eksisterende 80px høyde på desktop / 56px på mobil viser hele merket uten strekk eller beskjæring. Ingen eksterne logoer er lastet ned.

### Oppdater lokalt etter gjennomgått og fullført PR

PR-en skal ha `oliver-branch` som base og skal ikke flettes automatisk. En åpen PR betyr **ikke** at implementering og testing er ferdig; les sjekklisten og begrensningene først.

```sh
git status
git stash list
# Bare hvis du har nye, ulagrede CSS-endringer:
git stash push -m "Lokal CSS før kartoppdatering" -- wwwroot/css/site.css
git switch oliver-branch
git pull --ff-only origin oliver-branch
docker compose up --build -d --force-recreate heimevernet
docker compose ps
docker compose logs --tail=100 heimevernet
```

Bevar eksisterende CSS-stash og eventuelle lokale bilder; ikke bruk reset/clean eller overskriv dem. Ikke kjør `git stash pop` blindt: inspiser riktig stash og kombiner ønskede endringer manuelt ved konflikter. Denne oppdateringen endrer ikke `site.css`. Etter rebuild: Ctrl+F5, test kart og tema. Bevar både database- og nøkkelvolum; **aldri `docker compose down -v`**.

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

GitHub Copilot hjalp også med denne kart-/navigasjonsoppryddingen: eksplisitt punktvalg, skrivebeskyttet stedsoppslag med koordinatfallback, Nominatim-policy, filter-/fullskjermkontroller, logo-/menyretting og fokuserte tester. Faktisk kjørte tester og nettlesersjekker, inkludert eventuelle nettverksbegrensninger, oppgis i PR-en; dokumentasjonen er ikke en påstand om fullført testing.

GitHub Copilot hjalp med Identity/roller, eierkontroll og CRUD, additive migreringer, kartkontroller, tester og denne dokumentasjonen. Faktisk utførte valideringer og begrensninger rapporteres i PR-en; AI-generert kode må fortsatt gjennomgås av prosjektgruppen.

GitHub Copilot also assisted with the local resource SVGs, allowlisted marker mapping, light/dark theme controls and styling, and focused frontend checks in this update. Validation results and any browser-testing limitations are recorded in the pull request.

GitHub Copilot was used for this MariaDB migration to inspect the existing persistence and Docker configuration, check provider compatibility, draft the provider configuration and EF migration, and prepare integration coverage and operations documentation. Prompts in this work asked for the SQLite-to-MariaDB change, EF/provider version verification, Docker readiness and persistence, and focused migration tests. The resulting changes were reviewed and validated with restore/build and MariaDB-backed checks where available. This note describes AI assistance for this migration only; it does not make claims about earlier work by the group.
