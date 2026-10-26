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
| Svare på forespørsler / returnere egne tildelte ressurser | Ja | Ja |
| Opprette operative behov, sende forespørsler og tildele ressurser | Nei | Ja |
| Godkjenne/avvise ressurser (ikke egne) | Nei | Ja |
| Se alle behov | Nei | Ja |
| Liste brukere og tildele/fjerne Admin | Nei | Ja |

Primærmenyen viser Hjem, Kart, Ressurser og Mine ressurser. Bruker/Admin får Forespørsler på `/Operations`; tilbydere ser bare egne innkommende forespørsler. Admin får også Verifisering og Behov, som peker til seksjoner på samme arbeidsflate, samt Brukere. `/Home/Resources` er oversikten; `/Home/Mine` begrenser til egne ressurser. Gamle `/Home/Data?type=...` og `/Home/Verify?type=...` videresendes til Ressurser med typefilteret beholdt. Verifiseringshandlinger er Admin-beskyttet, ikke del av CRUD-oversikten. Eldre meldinger beholdes og vises bare til eier eller Admin; de blir ikke automatisk omgjort til operative behov. Det gamle kontaktskjemaet `/Home/Form` krever nå Admin. Sletting krever bekreftelse, versjonskontroll og POST med antiforgery-token; historikkførte/reserverte/tildelte ressurser må i stedet håndteres i arbeidsflyten.

## Arbeidsflyt og tilstandsmodell

Dette er en studentprototype, ikke en offisiell tjeneste eller et sanntids-/offline-system. Admin er prosjektets betrodde koordinator, behovsmelder og verifikator. Ingen nye privilegerte roller opprettes, og offentlig registrering kan aldri gi slike rettigheter. Admin tilordnes bare gjennom betrodd bootstrap, eksplisitt operatørkommando eller brukeradministrasjon. To separate kontoer trengs for å registrere og godkjenne samme ressurs: en Admin kan ikke godkjenne sin egen registrering.

| Begrep | Tilstander og regel |
| --- | --- |
| Tilgjengelighet | `Available` / `Unavailable` settes av eier eller Admin. `Reserved` og `Allocated` settes bare gjennom forespørselsflyten. |
| Verifisering | Nye **og eldre** ressurser starter `Pending`, uten oppdiktet historisk godkjenning. Admin godkjenner (`Approved`) eller avviser (`Rejected`) med begrunnelse og serverklokke. |
| Gyldighet | Godkjenning har faktisk tidspunkt og utløpsdato. Utløpt er en beregnet tilstand, ikke en bakgrunnsjobb. `Workflow__VerificationValidityDays` er 30 som standard (tillatt 1–3650); endring gjelder nye beslutninger. |
| Behov | Beskrivelse, sted, prioritet (`Low/Normal/High/Critical`) og livsløp (`Open/InProgress/Completed/Cancelled`) er separate felt. Kun Admin oppretter/koordinerer operative behov. |
| Forespørsel | `Pending` er ikke en tildeling. Bare ressursens eier kan akseptere (`Accepted`, reserverer atomisk) eller avvise (`Rejected`). Admin tildeler deretter (`Allocated`) og kan avbryte ventende/aksepterte forespørsler (`Cancelled`). |
| Retur | Eier eller Admin returnerer tildelt ressurs (`Returned`). Godkjenning nullstilles til `Pending`; inspeksjon/ny verifisering kreves før ny disponering. |

Bare **tilgjengelige ressurser med eier og gyldig godkjenning** kan forespørres. Vilkårene kontrolleres igjen ved eiers aksept og tildeling. Materielle endringer i navn, type, sted/koordinater, kapasitet eller transportbehov nullstiller godkjenning; en ren tilgjengelighetsendring gjør ikke det. Kapasitet/transport er valgfrie fritekstfelt, ikke oppdiktede standardenheter for alle kategorier.

MariaDB-transaksjoner og applikasjonsstyrte optimistiske versjonstoken beskytter ressurs, behov og forespørsel. Samtidige disponeringer kan ikke begge vinne; konflikter gir melding og krever ny vurdering. Endringer i kritisk arbeidsflyt registreres med aktør-ID, UTC-tid, handling og begrunnelse. Audit-historikken er et prototyperevisjonsspor, ikke et manipulasjonssikkert eksternt loggsystem. Sider viser status ved lasting; last på nytt for oppdatert informasjon. Lastfeil er ikke en tom resultatliste.

### Kartarbeidsflate

Kartet åpnes i **utforskingsmodus**. Søk flytter utsnittet; kartklikk registrerer ikke noe. «Registrer ressurs» åpner skjemaet, og «Velg plassering» aktiverer punktvalg. Redigering og ugyldige innsendinger beholder felt og valgte koordinater. Forhåndsvisning åpner aldri popup automatisk. På mobil velges Kart/Liste; detaljer kan utvides. Lokale forslag og eksplisitt eksternt søk beholder personvern-/attribusjonsreglene nedenfor.

### Første administrator

Valgfritt, utført av operatøren lokalt:

1. Bevar eksisterende `.env` og databasevolum. Sett både `ADMIN_EMAIL` og `ADMIN_PASSWORD` i lokal `.env` for en adresse som **ikke allerede har konto**. Velg en ny, unik hemmelighet utenfor chat, minst 12 tegn med stor/liten bokstav, tall og spesialtegn. Ikke gjenbruk passord delt i chat.
2. Kjør `docker compose up --build -d --force-recreate heimevernet`. Konto og roller opprettes i **din lokale MariaDB når appen starter**, ikke av cloud-agenten. Uten begge innstillinger opprettes ingen administrator; én manglende verdi stopper oppstart.
3. Logg inn som Administrator. Fjern deretter begge bootstrap-verdier fra `.env` og gjenskap appcontaineren med samme kommando. Kontoen beholdes i databasen.

En eksisterende vanlig konto blir **aldri** automatisk oppgradert: bootstrap stopper med feil. En eksisterende Admin beholdes uten passordendring. En innlogget administrator kan bruke «Administrer brukere». Hvis ingen Admin er tilgjengelig, kan en betrodd operatør med terminal-/databasetilgang eksplisitt oppgradere en **eksisterende** konto med engangskommandoen nedenfor. Ingen passord nullstilles, og registreringsskjemaet kan ikke tildele Admin. Cloud-agenten har ikke opprettet noen konto i brukerens lokale database eller hatt tilgang til den. Ikke legg ekte verdier i Git, logger eller skjermbilder.

### Gjenopprett administrator på eksisterende konto

Kontroller riktig database og konto først, og ta vanlig databasebackup. Kommandoen krever ingen `ADMIN_EMAIL`/`ADMIN_PASSWORD`, oppretter ingen konto og kjører verken migreringer, bootstrap eller HTTP-server. Databaseskjemaet må allerede være initialisert av normal oppstart. Erstatt eksempeladressen med kontoen du kontrollerer:

```sh
# Lokalt fra prosjektmappen, med eksisterende ConnectionStrings__DefaultConnection
# eller MARIADB_* miljøvariabler satt (dotnet laster ikke .env automatisk):
dotnet run --project HeimevernetInnlevering1.csproj -- --promote-admin operator@example.test

# I en allerede kjørende appcontainer, etter bygg/utrulling av denne versjonen:
docker compose exec heimevernet dotnet HeimevernetInnlevering1.dll --promote-admin operator@example.test
```

Prosessen avsluttes etter operasjonen: kode `0` ved suksess (også dersom kontoen allerede er Admin), `1` ved manglende/tvetydig konto eller databasefeil, `2` ved ugyldige argumenter. Konto-ID, eksisterende roller, passord og ressurser bevares. Første oppgradering fornyer sikkerhetsstempelet i samme transaksjon som rolletildelingen; eksisterende innlogginger avvises ved neste forespørsel. Logg inn på nytt og velg Administrator. Gjentakelse endrer ikke passord eller sikkerhetsstempel. Kommandoen skriver ikke kontoopplysninger, men e-postargumentet kan vises i prosesslisten og shellhistorikk; bruk en betrodd terminal og følg lokale rutiner for historikk. Dette er ikke et offentlig endepunkt eller en automatisk oppgradering via miljøvariabler. Ikke slett database- eller nøkkelvolum.

Passord lagres som Identity-hasher i databasen. Gjentatte mislykkede innlogginger utløser midlertidig låsing. Informasjonskapsler er HttpOnly/SameSite; HTTPS gir Secure-cookie, mens HTTP fungerer i lokal Docker-utvikling. Bruk HTTPS i produksjon.

## Prerequisites

- .NET SDK 10 for running the app locally
- Docker Engine and Docker Compose v2 for the containerized app and database

## Kartikoner og tema

Kartet viser egne, lokalt lagrede, ensfargede SVG-silhuetter uten sirkelmerker for Kjøretøy (bil), Maskin (gravemaskin), Drone, Generator, Personell og Utstyr (verktøykasse), samt ukjent type. Dette er originaltegnede symboler, ikke kopiert referanse-/vannmerkekunst. Symbolene er normalt 32px (zoom 12), begrenset til 28–36px, med sentrert anker og diskret kontrastskygge. Markører, forhåndsvisning, tegnforklaring, kort og Verifiser bruker samme mapping og innholdsversjonerte SVG-adresser, slik at gamle sirkelikoner ikke blir liggende i nettleserens hurtigbuffer etter utrulling. Ingen nye koordinater eller forhåndsvisning velges ved oppstart eller etter innsending. Klikk i kartet eller velg et søkeresultat først. Forhåndsvisningen oppdateres når du velger ressurstype, uten popup eller tekst over markøren; valgt sted vises i eksisterende oppsummering. Typevalg alene lager aldri en markør. Ukjent/ikke valgt type kan vises med spørsmålstegn etter eksplisitt plassering. Lagrede ressurser beholder informative popuper ved klikk.

Grønn silhuett betyr at ressursen har kjent type, en eksisterende eier, tilgjengelighet `Available` og gjeldende `Approved`-verifisering: godkjenningstidspunktet må være nå eller tidligere, og utløpstiden må ligge strengt frem i tid. Ventende, avvist eller utløpt verifisering, `Unavailable`, `Reserved`, `Allocated`, ukjente verdier, manglende lagret status og manglende eier gir rødt symbol, med tekst som forklarer årsaken. Utløp oppdateres også mens siden står åpen. Ulagret plasseringsforhåndsvisning og typeforklaring er nøytrale, ikke et løfte om tilgjengelighet. En ventende forespørsel reserverer ikke ressursen; først eierens aksept setter `Reserved`.

Symbolene skaleres ved zoomendring, med mindre ikoner på oversiktsnivå og begrenset minimum/maksimum. Tegnforklaringen nederst til venstre bruker samme ikoner, med seks avkrysningsbokser som kun skjuler/viser lagrede markører. Alle kan slås av; verken databasen eller valgt forhåndsvisning endres. Valget beholdes ved zoom/fullskjerm. «Vis på kart» gjenaktiverer ressursens type ved behov. Tegnforklaringen er større kun i fullskjerm og kan foldes sammen på mobil. Firehjørnesymbolet nederst til høyre bruker nettleserens Fullscreen API, med CSS-reserve. Trykk knappen igjen eller Esc for å avslutte. Kontrollene følger temaet og støtter tastatur.

«Rediger» åpner samme kart med eksisterende punkt og felt utfylt, uten dobbel markør. Klikk i kartet for å flytte punktet, og lagre. Servervalidering beholder et gyldig valgt punkt ved feil.

Ressursnavn er brukerens tittel. Stedsnavn er en skrivebeskyttet oppsummering av valgt punkt og bestemmes på serveren; innsendt stedsnavn stoles ikke på. Reverse-oppslag bruker nærliggende adresse/tettsted (høyst 1 km fra punktet), ellers ærlige koordinater, også til havs eller ved nettverksfeil. Et flyttet punkt får straks koordinatetikett, og sene svar fra et tidligere punkt ignoreres.

### Stedsoppslag og personvern

Mens du skriver (minst 2, høyst 120 tegn, 250 ms ventetid), vises inntil seks lokale stedsforslag. Nettleseren laster én versjonert fil på ca. 40 KB og søker deretter lokalt, uten å sende tastetrykk til en geokoder. Piltaster velger forslag, Enter velger uten å sende ressursskjemaet, Escape lukker; klikk/berøring fungerer også. Typing/panorering lager ingen forhåndsvisning. Sene svar ignoreres og eksplisitte forespørsler avbrytes ved nytt søk.

**Dekning/lisens:** 624 norske byer/tettsteder fra [GeoNames](https://www.geonames.org/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), bearbeidet norsk utvalg av `cities500` via GeoNamesCache 3.0.2. Dette er et fast øyeblikksbilde (steder over 500 innbyggere og administrative sentre), **ikke adresse-autofullføring eller alle norske steder**. Navn/koordinater kan være unøyaktige. Kilde, sjekksum, bearbeiding og attribusjon står i `wwwroot/data/NOTICE.txt`; ingen Python-pakke installeres. Oppdater ved å hente den dokumenterte distribusjonen, kontrollere SHA-256 og filtrere `cities500.json` på `countrycode == "NO"` som beskrevet i NOTICE; oppdater også dokumentert antall/test ved datasettendring.

Fullt steds-/adressesøk hos Nominatim utføres bare ved «Søk» eller Enter uten åpne forslag. [Offentlig Nominatim forbyr autofullføring](https://operations.osmfoundation.org/policies/nominatim/), også bak proxy/ventetid; derfor brukes aldri denne tjenesten mens du skriver. Reverse-oppslag skjer kun etter punktvalg/lagring, aldri ved panorering/zoom. Den autentiserte serveren beholder identifiserende User-Agent, maksimalt én ekstern forespørsel per sekund for hele appinstansen, ingen arbeidskø, tids-/svarstørrelsesgrenser og begrenset hurtigbuffer (512 oppføringer, treff i 24 timer, manglende treff i 30 sekunder). Ingen nye API-nøkler kreves. Overbelastning/feil gir koordinatfallback og blokkerer ikke lagring.

Vurdert alternativ: [Photon støtter søk mens du skriver og dokumentert egen drift](https://github.com/komoot/photon#setting-photon-up-with-the-release-binaries-and-extracts), men krever Java 21+/OpenSearch og separat søkedatabase/import. Offisiell planetdatabase er ca. 95 GB og anbefaler 64 GB RAM; landsuttrekk er mindre, men må fortsatt hentes, lagres og oppdateres. Derfor legges ingen tung tjeneste, offentlig demoserver eller betalt nøkkelkrav til Docker. Lokale forslag virker direkte i eksisterende Docker/publish; full adresse-autofullføring er ikke implementert.

**Personvern:** Eksplisitt søketekst og valgte koordinater sendes til OpenStreetMaps Nominatim via serveren, ikke kontoopplysninger eller ressursnavn. Lokale forslag sender ingen søketekst ut av nettleseren. Kartfliser lastes fortsatt direkte fra OpenStreetMap. Ikke søk etter konfidensielle/personlige opplysninger. For sensitive plasseringer kan eksterne stedsoppslag deaktiveres med `Geocoding__Enabled=false`; lokale forslag, koordinatetiketter og manuell kartplassering fungerer fortsatt. `Geocoding__BaseUrl` (HTTPS) og `Geocoding__UserAgent` kan overstyres av operatøren uten kodeendring. Offentlig Nominatim skal kun brukes fra én appinstans; bruk egen/annen egnet tjeneste eller felles global begrensning før skalering til flere instanser. Dette er ikke en generell URL-proxy.

Nettstedet starter med lyst tema, uavhengig av operativsystemets innstilling. Knappen ved menyen viser **gjeldende** modus: «Mørk modus» med måne på mørk side, «Lys modus» med sol på lys side. Tilgjengelig navn og verktøytips beskriver byttehandlingen, mens `aria-pressed` følger mørkt tema. Valget lagres i nettleseren på tvers av sider og omlasting. Hvis nettleserlagring er blokkert, virker byttet på den åpne siden; uten JavaScript brukes lyst tema. «Søk» har blå bakgrunn, hvit tekst og minst 44px høyde i begge temaer.

Focused frontend checks use Node.js's built-in test runner (no npm packages):

```sh
node --test tests/Frontend/*.test.cjs
dotnet build
dotnet test tests/MariaDbIntegrationTests/MariaDbIntegrationTests.csproj --filter 'FullyQualifiedName!~MariaDbPersistenceTests&FullyQualifiedName!~WorkflowTests&FullyQualifiedName!~AdminPromotionTests'
```

Fokuserte tester dekker tema/ARIA, zoom/felles ikonmapping, logoens proporsjoner, popupfri forhåndsvisning, samt lokal debounce/minimumslengde, hurtigbuffer, utdaterte svar, tastaturvalg, tekstescaping og nettverksfeil. Eksisterende tester beholdes; faktiske resultater og nettleser-/Docker-begrensninger oppgis i PR-en.

Headeren bruker versjonerte `wwwroot/images/forsvaret-logo.png`, avledet fra prosjektets eksisterende JPG med transparent bakgrunn og bevart hvit øksedetalj. Det flate originalrasteret er proporsjonskorrigert til 588×721 etter [emblemets dokumenterte 406:498-forhold](https://commons.wikimedia.org/wiki/File:Coat_of_arms_of_the_Norwegian_Armed_Forces.svg), uten å hente eller erstatte selve kunstverket. Hele emblemet vises med automatisk bredde og `object-fit: contain`, 48px høyt på desktop / 40px på mobil, sammen med tydelig prototypebetegnelse. Bildet ligger ubetinget i delt layout og leveres som offentlig statisk fil: også anonyme besøkende på Hjem, Logg inn og Registrer ser det, uten at kart/data gjøres offentlige. En ren Docker-build inkluderer filen; ingen lokal stash eller usporet bildefil kreves. Dette publiserer ikke localhost på internett.

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

Bevar eksisterende CSS-stash og eventuelle lokale bilder; ikke bruk reset/clean eller overskriv dem. Ikke kjør `git stash pop` blindt: inspiser riktig stash og kombiner ønskede endringer manuelt ved konflikter. Arbeidsflaten bruker egne stiloverstyringer etter `site.css`; gamle lokale stiler må gjennomgås før de kombineres. Etter rebuild: Ctrl+F5, test kart og tema. Bevar både database- og nøkkelvolum; **aldri `docker compose down -v`**.

## Run with Docker Compose

**Før oppgradering med migreringer:** Appen kjører `Database.MigrateAsync()` ved oppstart. Stopp appens skriving og ta en separat, operatørstyrt databasebackup **før** du starter den nye appversjonen. Kodegrenen nedenfor er ikke en databasebackup. Ingen backup av din lokale database er opprettet av cloud-agenten.

Fra lokal prosjektmappe, med eksisterende `.env` og databasecontainer:

```sh
docker compose stop heimevernet
umask 077
docker compose exec -T mariadb sh -c 'exec mariadb-dump -uroot -p"$MARIADB_ROOT_PASSWORD" --single-transaction --routines --events "$MARIADB_DATABASE"' > mariadb-before-workspace.sql
test -s mariadb-before-workspace.sql
```

Backupen inneholder konto-/Identity-data og må lagres privat utenfor Git og kontrolleres med en **prøverestaurering til en separat MariaDB-instans**. Kopier også Identity-nøkkelvolumet sikkert ved behov for full gjenoppretting. Ikke ta med databasedump i PR. Den nye migreringen er additiv: eksisterende rader og eierskap bevares, og gamle ressurser blir uverifiserte. Den tilskriver ikke eldre registreringer en historisk godkjenning.

### Kode- og databaserollback

Den urørte grenen `backup/oliver-before-workspace-redesign-2026-10-25` peker på `915a8cb843d51756251d58c8afc138ebad028d78` og er **bare et kodesnapshot**. Inspiser den uten å endre arbeidsgrenen:

```sh
git fetch origin backup/oliver-before-workspace-redesign-2026-10-25
git worktree add --detach ../workspace-before-redesign origin/backup/oliver-before-workspace-redesign-2026-10-25
```

Vanlig koderollback er en gjennomgått revert-PR til `oliver-branch`, ikke reset/force-push og aldri endring av backupgrenen. Revert av kode endrer ikke databasen. Eldre kode kjenner ikke nye statuser og kan omgå nye arbeidsflytregler; ikke bruk den som skrivende app mot operativt oppgradert datasett uten vurdering.

Ved nødvendig databaserestore: stopp alle skrivende apper, bevar også en kopi av tilstanden etter oppgraderingen, og prøverestaurer før bytte. På en **separat, tom kompatibel MariaDB-instans**, med riktig database/bruker konfigurert, kan dumpen leses inn med:

```sh
docker compose exec -T mariadb sh -c 'exec mariadb -uroot -p"$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE"' < mariadb-before-workspace.sql
```

Dette er en operatørhandling for den valgte restore-instansen, ikke en kommando som skal kjøres blindt mot eksisterende data. En pre-migreringsbackup mister senere forespørsler/beslutninger ved gjenoppretting; avklar disse før tilbakeføring. Automatisk nedmigrering eller sletting av Docker-volumer er ikke normal rollback. Bevar migreringshistorikk og database/kode som et kompatibelt par.

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

The opt-in test project exercises migrations and real MariaDB persistence, including Identity users/roles, resource ownership, Norwegian characters, nullable form locations, map coordinates and timestamps, and map-resource create/read/update/delete. Workflow tests additionally exercise decisions, expiry, owner access, allocation conflicts and return/reverification. Administrator recovery tests cover explicit opt-in versus bootstrap/public signup, missing accounts, repeat invocation, password/resource preservation, transactional security-stamp failure and CLI exit without HTTP hosting. Its source is excluded from the web project's compile glob. Tests without MariaDB can run using the filtered command above; run the complete suite only against an isolated test database with the documented MariaDB environment variables.

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

GitHub Copilot hjalp med arbeidsflateredesign, domenemodell for tilgjengelighet/verifisering/behov/forespørsler, konkurransekontroll, additive migreringer, tester og dokumentasjon i denne endringen. Eksisterende teknologi, lokale originale symboler og stedsoppslag beholdes. Testkommandoer, faktiske resultater, nettleserbilder og begrensninger oppgis i PR-en. Ingen virkelige personlige kontoer/passord fra chat brukes; eventuelle røykprøvekontoer er syntetiske i en isolert testdatabase. Gruppen må gjennomgå AI-generert kode og gjøre egen brukertest før den vurderes for annet enn prototypebruk.

### Kort brukertest for medstudenter

Bruk syntetiske data og en separat testdatabase. Ikke forklar fremgangsmåten på forhånd:

1. Finn en ressurs ved et oppgitt sted; forklar forskjellen mellom registrert, tilgjengelig og disponibel.
2. Registrer en egen ressurs og velg plassering. Finn tilbake til den.
3. Endre egen ressurs og tilgjengelighet; forklar eventuell ny verifisering.
4. Fremprovoser en ugyldig innsending (f.eks. manglende navn) og rett feilen uten å miste valgte data.
5. Med to autoriserte kontoer: vurder ressurs, send forespørsel, svar som eier, tildel og returner. Forklar hvorfor ny tildeling ikke er mulig før ny godkjenning.
6. Gjenta kart/liste og søk med tastatur og på smal skjerm.

Registrer per oppgave: fullført/ikke fullført, tidsbruk, hvor deltakeren nølte, feilhandling, behov for hjelp og deltakerens forklaring av status. Ikke samle virkelige kontaktdata. Ingen brukerfunn er påstått; denne protokollen må faktisk gjennomføres før resultater skrives i rapporten.

GitHub Copilot hjalp med transparent lokal logo, originale enklere ressursikoner, tema-/søkekontrast, popupfri forhåndsvisning, samlet Verifiser-side, tre nye tester og trygg lokal administratorveiledning. Ingen brukerpassord er brukt, og ingen konto er opprettet i brukerens lokale database. Faktisk utførte sjekker og begrensninger rapporteres i PR-en.

GitHub Copilot hjalp også med denne kart-/navigasjonsoppryddingen: eksplisitt punktvalg, skrivebeskyttet stedsoppslag med koordinatfallback, Nominatim-policy, filter-/fullskjermkontroller, logo-/menyretting og fokuserte tester. Faktisk kjørte tester og nettlesersjekker, inkludert eventuelle nettverksbegrensninger, oppgis i PR-en; dokumentasjonen er ikke en påstand om fullført testing.

GitHub Copilot hjalp med Identity/roller, eierkontroll og CRUD, additive migreringer, kartkontroller, tester og denne dokumentasjonen. Faktisk utførte valideringer og begrensninger rapporteres i PR-en; AI-generert kode må fortsatt gjennomgås av prosjektgruppen.

GitHub Copilot also assisted with the local resource SVGs, allowlisted marker mapping, light/dark theme controls and styling, and focused frontend checks in this update. Validation results and any browser-testing limitations are recorded in the pull request.

GitHub Copilot was used for this MariaDB migration to inspect the existing persistence and Docker configuration, check provider compatibility, draft the provider configuration and EF migration, and prepare integration coverage and operations documentation. Prompts in this work asked for the SQLite-to-MariaDB change, EF/provider version verification, Docker readiness and persistence, and focused migration tests. The resulting changes were reviewed and validated with restore/build and MariaDB-backed checks where available. This note describes AI assistance for this migration only; it does not make claims about earlier work by the group.
