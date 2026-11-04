# Beredskapsportalen – ressurs- og behovsportal for Totalforsvaret

## Innhold

1. [Databasen (ER-modellen)](#databasen-er-modellen)
2. [Roller og arbeidsflyt](#roller-og-arbeidsflyt)
3. [Sikkerhet](#sikkerhet)
4. [Testing – scenarier og resultater](#testing--scenarier-og-resultater)
5. [Dokumentasjon i koden](#dokumentasjon-i-koden)
6. [Bruk av KI](#bruk-av-ki)

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

Resultat 3. november 2026: **61 av 61** integrasjonstester og **131 av 131** frontend-tester bestått.

| Testfil | Hva testes |
| --- | --- |
| `ErModellTests` | Alle 12 tabellene har kolonnene fra ER-diagrammet. Oppslagsdata finnes, og hver behovstype har minst én ressurstype. Unik ForespørselID i Tildeling. Bruker kan ikke slettes når den eier data. Norske tegn og koordinater lagres uendret. |
| `WorkflowTests` | Hele flyten fra registrering til returnert. En verifiseringsansvarlig kan ikke godkjenne egen ressurs, og avvisning krever begrunnelse. En uverifisert eller allerede forespurt ressurs kan ikke forespørres. Bare eier kan svare. Tildeling krever godtatt forespørsel. Fremdrift går ett steg om gangen. To samtidige forespørsler gir bare én vinner. Endret plassering krever ny verifisering. Ressurser med historikk kan ikke slettes. Feil rolle avvises. Hendelser kobles til behov. |
| `SeedingOgRegistreringTests` | Demodata gir én konto per rolle og kan kjøres flere ganger. Registrering lagrer navn og telefon. Man kan ikke gi seg selv Kriseledelse, Dispatch eller Verifisering. Bootstrap av admin krever riktig passord. |
| `ResourceAuthorizationTests` | Bare eieren med rollen Ressursleverandør kan endre en ressurs |
| `PlaceGeocoderTests`, `DatabaseProviderConfigurationTests` | Stedsoppslag (grenser, hurtigbuffer, feil) og tilkoblingsoppsett |
| `tests/Frontend` | Kartlogikk, ikoner, tema, tastaturnavigasjon, stedssøk og at sidene har riktige skjemaer og rollestyring |

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
