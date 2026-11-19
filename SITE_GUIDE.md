# Beredskapsportalen – site guide (for humans and AI assistants)

Norwegian crisis-preparedness portal (prototype). **Public actors report needs**, **resource providers register resources**, **dispatch matches them**, **crisis leadership oversees and administers**. UI language is Norwegian.

## Stack
- ASP.NET Core MVC (Razor views, .NET 10), EF Core 9 + Pomelo, MariaDB 10.11, ASP.NET Identity (cookie auth, custom `Bruker`).
- Vanilla JS + CSS (no framework). Map: Leaflet-style `kart.js`. Docker: `docker compose up --build` → http://localhost:8080.
- Startup (`Program.cs`): wait for DB → `MigrateAsync` → `IdentitySeeder` → demo data (`Data/DemoDataSeeder.cs`, `DemoDataExtras.cs`, only on a fresh DB).
- Demo logins (password `Dragoncool22!`): `offentlig@gmail.com`, `ressurs@gmail.com`, `dispatch@gmail.com`, `kriseledelse@gmail.com`. (README also lists `verifisering@gmail.com`; in code verification is done by DispatchTeam.)

## Roles (`Models/AppRoles.cs`)
| Role | Label | Can do |
|---|---|---|
| `OffentligAktor` | Offentlig aktør | Report needs (`Behov/Registrer`), see own needs and their status. Self-signup. |
| `Ressursleverandor` | Ressursleverandør | Register/edit/delete own resources, mark availability, accept/decline requests. Self-signup. |
| `DispatchTeam` | Dispatch-team | Verify resources, create requests (need↔resource), allocate, cancel, update assignment progress. Assigned by leadership. |
| `Kriseledelse` | Kriseledelse | Everything dispatch sees plus: create/close incidents, set need status/priority, manage users and roles (`Admin`). |

## Workflow (core idea)
1. Provider registers **Ressurs** → status `venter på verifisering`.
2. Dispatch **verifies** (approve → `tilgjengelig`, reject → `avvist`).
3. Public actor registers **Behov** (type, qty, location, priority `akutt`/`planlagt`, optional deadline/incident) → `ny`.
4. Dispatch creates a **Forespørsel** (request) for a resource against a need → resource `forespurt`, need `vurderes`.
5. Provider **accepts/declines** (`Svar`: venter/godtatt/avslått/avbrutt).
6. Dispatch **allocates** → **Tildeling** with progress `bekreftet → på vei → fremme → fullført`; resource `tildelt`/`i bruk`/`returnert`; need becomes `tildelt` then `løst`.
7. Every action is written to `Hendelseslogg` (audit) in the same transaction (`Services/WorkflowService.cs`, with concurrency tokens).

Status constants live in `Models/Entities/Statuser.cs`.

## Pages (route → purpose → who)
Layout: `_AppShell.cshtml` (left sidebar on desktop, **bottom tab bar on mobile** via `mobile.css`), top bar with search, notifications (`_UserMenu`), theme toggle (`Tema/Bytt`).

| Route | View | Purpose |
|---|---|---|
| `/` (Home/Index) | `Home/Index` | Help/about portal page |
| `/Account/Login`, `/Register` | `Account/*` | Auth |
| `/Home/Dashboard` | `Home/Dashboard` | **Oversikt**: role-specific dashboard (see below) |
| `/Home/Map` | `Home/Map` | **Kart**: map with resources/needs, side rail with filter + sort; providers can register a resource here |
| `/Home/Resources` | `Home/Verify` + `_ResourceList` | **Ressurser**: hero with KPIs, list/map toggle (“Listevisning”), filters |
| `/Home/Edit/{id}`, `/Delete/{id}` | | Provider edit/delete resource |
| `/Behov/Registrer` | `Behov/Registrer` | **Meld behov** form (OffentligAktor) with place search/geocoding |
| `/Hendelse` | `Hendelse/Index` | **Hendelser** (incidents) list + create (leadership), pin/main incident |
| `/Hendelse/Arbeidsrom/{id}` | `Hendelse/Arbeidsrom` | Incident workspace: needs, decisions (`Beslutning`), timeline |
| `/Messages/Inbox` | `Messages/Inbox` | **Meldinger**: 1:1 and group threads; “Se kontakt” deep-links to Contacts |
| `/Home/Contacts` | `Home/Contacts` | **Kontaktliste** + group-chat picker (`#groupPicker`) |
| `/Account/Profile` | `Account/Profile` | **Profil**: overview, notifications prefs, kontaktliste tab (in-page message composer), password, avatar |
| `/Admin` | `Admin/Index` | **Brukere**: list users, set roles (leadership only) |
| `/Search?q=` | `Search/Index` | Global search across needs/resources/incidents |
| `/Operations/*` (POST) | – | Workflow actions: Verify, CreateRequest, Respond, Allocate, Cancel, Assignment, Availability, NeedStatus, Prioritize |

### Dashboard by role (4 tiles on top switch panels; `dashboard-workspace.js`; sorting/filters in `dashboard-sort.js`)
- **Offentlig aktør**: *Mine behov* (status tabs Alle/Sendt inn/Under behandling/Delvis tildelt/Tildelt/Løst, search, sort), *Under behandling* (orange left banner), *Akutte behov* (red banner + “!” badge).
- **Ressursleverandør**: *Mine ressurser*, *Forespørsler til meg* (accept/decline) + answered history, *Ressurser som trenger oppfølging*.
- **Dispatch**: *Venter på verifisering* queue, *Åpne behov* + *Forespørsler*, *Akutte behov*, *Aktive tildelinger* (progress updates).
- **Kriseledelse**: *Situasjonsbilde*, *Aktive hendelser*, *Åpne behov*, *Akutte behov*, *Brukere og roller*, *Aktive tildelinger*.

## Code map
- `Controllers/` – Home (dashboard, map, resources, contacts), Behov, Hendelse, Operations, Messages, Account, Admin, Search, Tema.
- `Services/` – `WorkflowService` (all state transitions + audit), `DashboardService` (builds `DashboardViewModel`), `ShellService` (badges/notifications), `PlaceGeocoder`/`Stedssok`/`NorwegianPlaces` (place search).
- `Models/Entities/` – `Bruker, Ressurs, Behov, Forespørsel, Tildeling, Hendelse, Verifisering, Melding, Hendelseslogg, Oppslag (types)`.
- `Data/` – `AppDbContext`, migrations, seeders, `DatabaseProviderConfiguration`.
- `wwwroot/css/` – `site, shell, home, workspace, workspace-map, dashboard-sort, account, **mobile.css** (all ≤767px overrides)`.
- `wwwroot/js/` – `dashboard-workspace, dashboard-sort, rail-filter, kart, resources, behov, contacts, messages, profile(-area), toast`.
- Views are compiled into the DLL: after changing `.cshtml`, rebuild (`docker compose up --build`).

## Mobile design (≤767px)
Bottom tab bar replaces sidebar; cards stack; map rail becomes a bottom sheet (tap map/header to close); tables become cards. All in `mobile.css` plus mobile blocks in `workspace.css`/`dashboard-sort.css`.

## Screenshots
Not included: the app needs MariaDB and can’t run in the authoring environment. To capture them, run `docker compose up --build`, log in as each demo role and screenshot Oversikt, Kart, Ressurser, Hendelser, Meldinger, Kontaktliste, Profil (+ Brukere for leadership) at desktop width and at 390px wide (DevTools device mode).
