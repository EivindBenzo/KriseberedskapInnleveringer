# Beredskapsportalen – ressurs- og behovsportal for Totalforsvaret

Vi i gruppen holder på å lage applikasjonen slik at den har offentlig aktører som melder behov, ressursleverandør som registrerer ressurser, slik at dispatch team kan koble dem sammen. Kriseledelsen funker som administrator og kan tildele roller til brukere, registrere hendelser, i tillegg til å ha større oversikt over de ulike delene av applikasjonen. Dette er en prototype fortsatt.

## Innhold

1. [Drift](#drift)
2. [Arkitektur](#arkitektur)
3. [Databasen (ER-modellen)](#databasen-er-modellen)
4. [Roller og arbeidsflyt](#roller-og-arbeidsflyt)
5. [Sikkerhet](#sikkerhet)
6. [Testing – scenarier og resultater](#testing--scenarier-og-resultater)
7. [Dokumentasjon i koden](#dokumentasjon-i-koden)
8. [Bruk av KI](#bruk-av-ki)

## Drift

### Det du trenger

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (må kjøre, krever virtualisering/WSL 2 på Windows)
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) for å bygge, teste og lage migrasjoner lokalt

### Start app og database i Docker

```sh
cp .env.example .env          # Windows PowerShell: Copy-Item .env.example .env
# Bytt ut de to passordene i .env med lange, tilfeldige verdier
docker compose up --build
```

Åpne <http://localhost:8080>. `docker-compose.yml` starter to containere:

| Tjeneste | Innhold |
| --- | --- |
| `mariadb` | MariaDB 10.11.19. Databasen lagres i volumet `mariadb-data`. Porten er ikke åpen utenfor Docker. |
| `heimevernet` | Nettsiden. Den venter til databasen er klar, kjører EF Core-migrasjonene (lager tabellene fra ER-modellen) og legger inn roller og demodata. |

Nyttige kommandoer:

```sh
docker compose logs -f heimevernet      # se loggen til appen
docker compose stop                     # stopp (data beholdes)
docker compose down -v                  # stopp og SLETT databasen
```

> **Første gang etter ER-ombyggingen:** Databasen har fått nye tabeller (Bruker, Ressurs, Behov osv.) og kan ikke oppgraderes fra den gamle strukturen. Har du kjørt en eldre versjon før, må den gamle databasen slettes én gang med `docker compose down -v` før `docker compose up --build`. Det samme gjelder databasen på Render/Aiven.

### Demokontoer

`SEED_DEMO_ACCOUNTS=true` (standard i `docker-compose.yml`) lager én konto per rolle og litt demodata: en hendelse, tre behov, fire ressurser, to forespørsler og én aktiv tildeling. Passordet er `Dragoncool22!` med mindre `DEMO_ACCOUNT_PASSWORD` er satt.

| E-post | Rolle |
| --- | --- |
| `offentlig@gmail.com` | Offentlig aktør |
| `ressurs@gmail.com` | Ressursleverandør |
| `verifisering@gmail.com` | Verifiseringsansvarlig |
| `dispatch@gmail.com` | Dispatch-team |
| `kriseledelse@gmail.com` | Kriseledelse |

Kontoene har kjent passord og skal bare brukes til demo. Slå av `SEED_DEMO_ACCOUNTS` og slett kontoene etterpå.

### Første kriseledelse-konto uten demodata

Sett `ADMIN_EMAIL` og `ADMIN_PASSWORD` i `.env` før oppstart. Da lages en konto med rollen Kriseledelse, som kan gi andre brukere roller under «Brukere». Fjern verdiene fra `.env` etterpå. Har du ikke tilgang til en kriseledelse-konto, kan en eksisterende konto oppgraderes fra terminalen:

```sh
docker compose exec heimevernet dotnet HeimevernetInnlevering1.dll --promote-admin din@epost.no
```

### Kjøre appen uten Docker

Appen trenger en MariaDB og leser tilkoblingen fra `ConnectionStrings__DefaultConnection` eller fra `MARIADB_HOST`, `MARIADB_PORT`, `MARIADB_DATABASE`, `MARIADB_USER` og `MARIADB_APP_PASSWORD`. Sett disse og kjør `dotnet run`.

### Endre databasen (migrasjoner)

Endrer dere en entitetsklasse i `Models/Entities` eller oppsettet i `Data/AppDbContext.cs`, lag en ny migrasjon og commit den:

```sh
dotnet tool install --global dotnet-ef --version 9.0.22
dotnet ef migrations add BeskrivendeNavn
```

Migrasjonen kjøres automatisk når appen starter. EF Core 9.0.22 og Pomelo 9.0.0 brukes fordi Pomelo ikke støtter EF Core 10 ennå.

## Arkitektur

```text
Nettleser ──► Controllers ──► WorkflowService / DashboardService ──► AppDbContext (EF Core) ──► MariaDB
                 │                         (forretningsregler)            (ER-modellen)
                 └──► Views (Razor) + wwwroot (CSS, JS, Leaflet-kart, ikoner)
```

| Mappe | Innhold |
| --- | --- |
| `Models/Entities` | De 12 tabellene fra ER-diagrammet som C#-klasser, og `Statuser.cs` med gyldige statusverdier |
| `Data/AppDbContext.cs` | Oversetter klassene til tabeller: navn, kolonner, lengder, nøkler, fremmednøkler og kardinaliteter |
| `Data/SeedData.cs` | Behovstyper, ressurstyper og koblingen mellom dem (legges inn av migrasjonen) |
| `Migrations` | EF Core-migrasjonen `ErModell` som lager databasen |
| `Services/WorkflowService.cs` | All forretningslogikk: registrering, verifisering, forespørsel, tildeling og fremdrift. Sjekker rolle og eierskap og logger hver handling. |
| `Services/DashboardService.cs` | Henter det hver rolle skal se på «Oversikt» |
| `Controllers` | `Home` (forside, kart, ressurser), `Behov` (meld behov), `Hendelse`, `Operations` (forespørsler/tildelinger), `Account`, `Admin` |
| `Views` | Razor-sider. Kart med Leaflet ligger i `Views/Home/Map.cshtml` og `Views/Behov/Registrer.cshtml`. |
| `wwwroot/lib/leaflet` | Kartbiblioteket Leaflet 1.9.4, lagret lokalt (ingen ekstern CDN) |
| `tests` | Integrasjonstester mot MariaDB (`MariaDbIntegrationTests`) og frontend-tester (`Frontend`) |

**Kart:** Nesten alt lages på serveren i C#: hvor kartet starter, hvilke ressurser som vises (filteret), symbolene og popup-teksten (`KartVisning` og `RessursIkon`). Det eneste JavaScript-et er `wwwroot/js/kart.js`, som tegner dette med Leaflet og fyller ut breddegrad/lengdegrad når man klikker i kartet i et skjema. Kartflisene hentes fra OpenStreetMap. Stedsnavn for et punkt finner serveren hos OpenStreetMap Nominatim når skjemaet lagres, med koordinater som reserve. «Finn sted» søker på serveren i `wwwroot/data/norway-localities.json` (GeoNames, CC BY 4.0), og spør bare Nominatim hvis stedet ikke finnes der (`Services/Stedssok.cs`).

**Uten JavaScript:** Menyen på mobil åpnes med en avkrysningsboks og CSS, feltet for «Annen type …» vises med CSS (`:has`), lys/mørk modus lagres i en informasjonskapsel som C# leser (`TemaController`), og etter en statusendring sender serveren deg tilbake til samme sted på siden med et `#anker` i adressen.

## Databasen (ER-modellen)

Tabellene lages fra ER-diagrammet i rapporten (mappeinnlevering 1):

| Tabell | Primærnøkkel | Fremmednøkler |
| --- | --- | --- |
| Bruker | BrukerID | – |
| Rolle | RolleID | – |
| BrukerRolle | (BrukerID, RolleID) | Bruker, Rolle |
| Hendelse | HendelseID | OpprettetAv → Bruker |
| Behovstype | BehovstypeID | – |
| Ressurstype | RessurstypeID | – |
| BehovstypeRessurstype | (BehovstypeID, RessurstypeID) | Behovstype, Ressurstype |
| Behov | BehovID | BehovstypeID, RegistrertAv → Bruker, HendelseID (kan være tom) |
| Ressurs | RessursID | RessurstypeID, EierID → Bruker |
| Verifisering | VerifiseringID | RessursID, VerifisertAv → Bruker |
| Forespørsel | ForespørselID | BehovID, RessursID, SendtAv → Bruker |
| Tildeling | TildelingID | ForespørselID (unik: én tildeling per forespørsel) |

Kardinalitetene fra rapporten håndheves av fremmednøklene, for eksempel Forespørsel 1 – 0..1 Tildeling med en unik indeks. Brukere med data kan ikke slettes ved et uhell (`ON DELETE RESTRICT`).

Statusfeltene er `VARCHAR` med verdiene fra rapporten, og koden sjekker at bare disse brukes (`Models/Entities/Statuser.cs`):

| Felt | Verdier |
| --- | --- |
| Ressurs.Status | venter på verifisering, tilgjengelig, forespurt, tildelt, i bruk, returnert, utilgjengelig, avvist |
| Behov.Status / Prioritet | ny, vurderes, tildelt, løst / akutt, planlagt |
| Forespørsel.Svar | venter, godtatt, avslått (+ avbrutt) |
| Tildeling.Fremdrift | bekreftet, på vei, fremme, fullført |
| Verifisering.Resultat | godkjent, avvist |
| Hendelse.Status | aktiv, avsluttet |

**Avvik fra ER-diagrammet, og hvorfor:**

- **ASP.NET Core Identity** bruker Bruker, Rolle og BrukerRolle til innlogging og legger til noen tekniske kolonner (`UserName`, `SecurityStamp`, `LockoutEnd` osv.) som trengs for sikker innlogging og utestenging. TofaktorNøkkel lagres av Identity i hjelpetabellen `BrukerToken`. Identity har også hjelpetabellene `BrukerClaim`, `BrukerInnlogging` og `RolleClaim`.
- **Stedsnavn** på Ressurs og Behov er lagt til, slik at sider og kart kan vise «Hamar» i stedet for bare koordinater.
- **Hendelseslogg** er en ekstra tabell som logger hvem som gjorde hva, når og hvorfor. Den brukes til historikken på hver ressurs og forespørsel.
- **Forespørsel.Svar = «avbrutt»** brukes når dispatch trekker en forespørsel tilbake før tildeling.
- **Samtidighetskontroll:** Ressurs.Status, Behov.Status, Forespørsel.Svar og Tildeling.Fremdrift brukes som samtidighetsfelt i EF Core. Prøver to dispatchere å forespørre eller tildele samme ressurs samtidig, lykkes bare den ene, uten ekstra kolonner.
- **Ressurs.Status lagres** (bevisst unntak fra 3NF, som beskrevet i rapporten), slik at kart og lister kan filtrere raskt.

## Roller og arbeidsflyt

| Rolle | Kan |
| --- | --- |
| Offentlig aktør | Melde behov med type, antall, frist, kontaktperson og punkt i kartet, og knytte behovet til en hendelse. Se egne behov. |
| Ressursleverandør | Registrere, redigere og slette egne ressurser med punkt i kartet, sette tilgjengelig/utilgjengelig og svare på forespørsler |
| Verifiseringsansvarlig | Godkjenne eller avvise ressurser (avvisning krever begrunnelse, ikke egne ressurser) |
| Dispatch-team | Se åpne behov med passende verifiserte ressurser (filtrert via BehovstypeRessurstype), sende forespørsler, tildele og oppdatere fremdrift |
| Kriseledelse | Opprette og avslutte hendelser, prioritere og lukke behov, følge tildelinger og hendelsesloggen, og gi brukere roller |

Flyten fra rapporten:

1. En ressurs registreres og får status **venter på verifisering**.
2. Verifiseringsansvarlig godkjenner den, og ressursen blir **tilgjengelig**. Godkjenningen gjelder i 30 dager.
3. En offentlig aktør melder et behov (**ny**).
4. Dispatch sender en forespørsel. Ressursen blir **forespurt** og behovet **vurderes**.
5. Eieren **godtar** eller **avslår**. Ved avslag blir ressursen tilgjengelig igjen.
6. Dispatch tildeler (bare etter at eieren har godtatt). Ressursen og behovet blir **tildelt**.
7. Fremdrift: **bekreftet → på vei → fremme** (ressursen er **i bruk**) **→ fullført** (ressursen er **returnert**).
8. En returnert ressurs må verifiseres på nytt før den kan brukes igjen. Kriseledelsen setter behovet til **løst**.

## Sikkerhet

- **Passord** lagres bare som hash av ASP.NET Core Identity. De må ha minst 12 tegn med store og små bokstaver og minst ett tall. Spesialtegn kreves ikke, fordi lengde gir mer sikkerhet enn rare tegn og gjør passordet lettere å huske.
- **Utestenging:** Kontoen låses i 15 minutter etter 5 feil passord.
- **Rollebasert tilgang:** Alle kontrollere har `[Authorize]`, og hver handling sjekker rolle og eierskap en gang til i `WorkflowService`. Det holder altså ikke å skjule knapper.
- **Registrering:** Man kan bare velge Ressursleverandør eller Offentlig aktør selv. Dispatch, Kriseledelse og Verifiseringsansvarlig tildeles av kriseledelsen. En verifiseringsansvarlig kan ikke godkjenne sin egen ressurs.
- **Rolleendring:** Brukeren logges ut ved neste forespørsel (security stamp sjekkes ved hver forespørsel).
- **CSRF-beskyttelse:** Alle POST-skjemaer har antiforgery-token.
- **SQL-injeksjon:** EF Core bruker parametriserte spørringer.
- **Informasjonskapsler** er `HttpOnly` og `SameSite=Lax`. Data Protection-nøklene lagres i volumet `identity-keys`.
- **Hemmeligheter:** Passord ligger i `.env`, som ikke er i Git. Databasen er ikke tilgjengelig utenfor Docker-nettverket, og appen bruker en egen databasebruker, ikke root.
- **Stedsnavn** settes av serveren, slik at det nettleseren sender ikke stoles på. Stedsnavn og tekst HTML-escapes i visning.
- **Kjent svakhet:** NuGet varsler om en moderat sårbarhet i `MySqlConnector 2.4.0` (GHSA-473q-m89c-ghf8), som følger med Pomelo 9.0.0. Den bør oppdateres når Pomelo gir ut en ny versjon.

## Testing – scenarier og resultater

### Automatiske tester

```sh
# Frontend-tester (Node.js, ingen pakker kreves)
node --test tests/Frontend/*.test.cjs

# Integrasjonstester mot ekte MariaDB: sett MARIADB_HOST/PORT/DATABASE/USER/APP_PASSWORD først.
# Hver test lager og sletter sin egen database, så brukeren må ha rett til CREATE/DROP DATABASE.
dotnet test tests/MariaDbIntegrationTests/MariaDbIntegrationTests.csproj

# Eller i Docker, mot en isolert database:
docker compose -p heimevernet-test --profile integration run --build --rm mariadb-integration-tests
docker compose -p heimevernet-test down -v
```

Resultat 4. november 2026: **68 av 68** integrasjonstester og **38 av 38** frontend-tester bestått.

| Testfil | Hva testes |
| --- | --- |
| `ErModellTests` | Alle 12 tabellene har kolonnene fra ER-diagrammet. Oppslagsdata finnes, og hver behovstype har minst én ressurstype. Unik ForespørselID i Tildeling. Bruker kan ikke slettes når den eier data. Norske tegn og koordinater lagres uendret. |
| `WorkflowTests` | Hele flyten fra registrering til returnert. En verifiseringsansvarlig kan ikke godkjenne egen ressurs, og avvisning krever begrunnelse. En uverifisert eller allerede forespurt ressurs kan ikke forespørres. Bare eier kan svare. Tildeling krever godtatt forespørsel. Fremdrift går ett steg om gangen. To samtidige forespørsler gir bare én vinner. Endret plassering krever ny verifisering. Ressurser med historikk kan ikke slettes. Feil rolle avvises. Hendelser kobles til behov. |
| `SeedingOgRegistreringTests` | Demodata gir én konto per rolle og kan kjøres flere ganger. Registrering lagrer navn og telefon. Man kan ikke gi seg selv Kriseledelse, Dispatch eller Verifisering. Bootstrap av admin krever riktig passord. |
| `ResourceAuthorizationTests` | Bare eieren med rollen Ressursleverandør kan endre en ressurs |
| `PlaceGeocoderTests`, `DatabaseProviderConfigurationTests` | Stedsoppslag (grenser, hurtigbuffer, feil) og tilkoblingsoppsett |
| `KartOgTemaTests` | Riktig symbol per ressurstype, «Annet»-filteret, «Finn sted» (lokalt søk uten aksenter) og at temaknappen lagrer valget og bare sender til lokale adresser |
| `tests/Frontend` | At `kart.js` bare tegner det serveren har bestemt, at sidene ikke har annen JavaScript, CSS for meny og «Annen type …», og at sidene har riktige skjemaer og rollestyring |

### Enhetstesting (Unit Testing)

Vi har gjennomført automatiserte enhetstester med xUnit for å kontrollere at enkeltfunksjoner i applikasjonen fungerer som forventet. Formålet er å oppdage feil tidlig og kontrollere funksjonalitet uten å måtte teste hele systemet.

Eksempler på tester:

- **ResourceAuthorizationTests:** Tester tilgangsregler for ressurser, blant annet at bare eieren med riktig rolle kan endre en ressurs.
- **PlaceGeocoderTests:** Tester stedsoppslag, koordinater og feilhåndtering ved hjelp av simulerte HTTP-svar.
- **KartOgTemaTests:** Inneholder tester av kartikoner og temafunksjonalitet.
- **DatabaseProviderConfigurationTests:** Kontrollerer databasekonfigurasjon og håndtering av ulike oppsett.

Enhetstestene ligger i samme testprosjekt som integrasjonstestene (`MariaDbIntegrationTests`). Noen tester kontrollerer enkeltfunksjoner isolert, mens andre tester samspillet mellom flere komponenter.

Ved siste dokumenterte testkjøring 4. november 2026 bestod alle 68 testene i testprosjektet. Dette inkluderer både enhetstester og integrasjonstester.

Enhetstestingen bidrar til å sikre at sentrale funksjoner fungerer som forventet, og gjør det enklere å oppdage feil ved videreutvikling.

### Systemtest (gjennomført i nettleser, Playwright mot MariaDB)

| # | Scenario | Forventet | Resultat |
| --- | --- | --- | --- |
| 1 | Ny bruker registrerer seg | Kan bare velge Ressursleverandør/Offentlig aktør, og navn og telefon lagres | OK |
| 2 | Offentlig aktør melder akutt transportbehov, knytter det til «Flom i Glomma» og klikker i kartet | Behovet lagres med koordinater og vises på oversikten | OK |
| 3 | Ressursleverandør registrerer «Minibuss 9 seter» med kartpunkt | Status «venter på verifisering», ikke synlig for dispatch | OK |
| 4 | Verifiseringsansvarlig godkjenner minibussen | Status «tilgjengelig», rad i Verifisering | OK |
| 5 | Dispatch åpner transportbehovet | Minibussen vises som passende ressurs (Transport → Kjøretøy) | OK |
| 6 | Dispatch sender forespørsel, eieren godtar, dispatch tildeler | Forespørsel «godtatt», tildeling «bekreftet», ressurs «tildelt» | OK |
| 7 | Dispatch setter på vei → fremme → fullført | Ressurs «i bruk» og deretter «returnert», Tildeling.Fullført satt | OK |
| 8 | Kriseledelse oppretter hendelsen «Strømbrudd i Hamar» og gir en ny bruker rollen Verifiseringsansvarlig | Hendelse lagret, og rollen endres i BrukerRolle | OK |

### Sikkerhetstest

| # | Scenario | Forventet | Resultat |
| --- | --- | --- | --- |
| 1 | Ikke innlogget åpner `/Home/Dashboard` | Sendes til innlogging (302 → `/Account/Login`) | OK |
| 2 | Offentlig aktør åpner `/Admin/Index` | Ingen tilgang (302 → `/Account/AccessDenied`) | OK |
| 3 | Offentlig aktør sender POST til `/Operations/Verify` uten token | Avvist, og ingen endring i databasen | OK |
| 4 | Registrering med rollen Kriseledelse sendt direkte (forbi nedtrekkslisten) | Avvist på serveren, ingen bruker lagres | OK (automatisk test) |
| 5 | Ressursleverandør prøver å svare på en forespørsel om en annens ressurs | Avvist | OK (automatisk test) |

### Brukervennlighetstest

Planlagt protokoll med medstudenter. Bruk demokontoene og ikke forklar fremgangsmåten på forhånd:

1. Som offentlig aktør: meld et behov og plasser det i kartet.
2. Som ressursleverandør: registrer en ressurs og finn den igjen i listen.
3. Som dispatch: finn en passende ressurs til et behov og send forespørsel.
4. Gjør en ugyldig innsending (f.eks. uten kartpunkt) og rett feilen.
5. Gjenta oppgave 1 på mobil eller smal skjerm.

Noter for hver oppgave om den ble fullført, tidsbruk, hvor deltakeren nølte og hvilke feil som ble gjort. *Resultatene fylles inn av gruppen når testen er gjennomført.*

## Dokumentasjon i koden

Klassene og metodene som bærer logikken, har XML-kommentarer (`///`) på norsk som forklarer hva de gjør og hvorfor:

- `Data/AppDbContext.cs` forklarer hver tabell og kardinalitet
- `Services/WorkflowService.cs` forklarer reglene for hver overgang
- `Models/Entities/*` forklarer hva hver tabell betyr
- Controllerne forklarer tilgangsstyringen

## Bruk av KI

KI ble brukt som støtteverktøy. Gruppen leser, tester og forklarer koden før den committes.

| Verktøy | Brukt til |
| --- | --- |
| GitHub Copilot | Første prototype og videreutvikling på `oliver-branch`: kartarbeidsflate, ikoner, tema, stedssøk, Identity og roller, arbeidsflyt, demodata, Docker/MariaDB og tester |
| Claude (Anthropic) | Ombygging av databasen til ER-diagrammet fra rapporten (12 tabeller, norske navn og statuser), ny migrasjon, tilpasning av tjenester, kontrollere og sider, skjemaet «Meld behov» med kart, hendelser, strammere rolleregistrering, lokal Leaflet, nye integrasjonstester og denne README-en |

Eksempler på prompts:

- «Gå gjennom prototypen og sammenlign den med ER-diagrammet og kravene.»
- «Bygg om databasen slik at UML/ER-diagrammet fungerer som database, men behold designet fra oliver-branch.»
- «Test hele flyten i nettleseren for hver rolle og ta skjermbilder.»

Fra den første prototypen (GitHub Copilot):

- «Lag en ASP.NET Core MVC-løsning med GET/POST for hendelsesskjema.»
- «Se på Marius-branchen og kombiner Entity Framework med skjema- og kartdesignet, slik at informasjonen faktisk lagres.»
- «La kartet starte i Kristiansand og lag en side som viser lagret informasjon.»
- «Legg til ressurstype og ressursnavn fra Marius-branchen»

Før større endringer ble det laget en backup-branch (`backup/oliver-before-resource-map-2026-09-28`) slik at endringene kunne rulles tilbake.

Alle endringer er bygget og testet (se tallene over) før de ble gitt til gruppen. KI-generert kode kan inneholde feil, og gruppen må gjennomgå den selv.
