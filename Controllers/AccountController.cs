using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using HeimevernetInnlevering1.Services;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

public class AccountController(UserManager<Bruker> users, SignInManager<Bruker> signIn, ShellService shell) : Controller
{
    private const string LoginError = "Kunne ikke logge inn. Kontroller opplysningene eller prøv igjen senere.";

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await users.FindByEmailAsync(model.Email.Trim());
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, LoginError);
            return View(model);
        }

        var result = await signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, LoginError);
            return View(model);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Dashboard", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (ModelState.IsValid && !AppRoles.SelfService.Contains(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Velg en gyldig kontotype.");
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim();
        var user = new Bruker
        {
            UserName = email, Email = email, Fornavn = model.Fornavn.Trim(), Etternavn = model.Etternavn.Trim(),
            PhoneNumber = model.Telefon.Trim(),
            Organisasjon = string.IsNullOrWhiteSpace(model.Organisasjon) ? null : model.Organisasjon.Trim()
        };
        var result = await users.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(user, model.Role);
            if (result.Succeeded)
            {
                TempData["AccountMessage"] = "Kontoen er opprettet. Du kan nå logge inn.";
                return RedirectToAction(nameof(Login));
            }
            await users.DeleteAsync(user);
        }
        ModelState.AddModelError(string.Empty, "Kunne ikke opprette kontoen. Kontroller opplysningene og passordkravene, eller prøv å logge inn.");
        return View(model);
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Profile([FromServices] AppDbContext db)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var roles = (await users.GetRolesAsync(user)).Where(AppRoles.All.Contains).ToList();
        var model = new ProfileViewModel
        {
            Name = user.FulltNavn,
            Email = user.Email ?? user.UserName ?? "Ikke oppgitt",
            Phone = user.PhoneNumber,
            Organisation = user.Organisasjon,
            FirstName = user.Fornavn,
            LastName = user.Etternavn,
            HasAvatar = await users.GetAuthenticationTokenAsync(user, AvatarProvider, AvatarToken) is not null,
            Roles = roles,
            Ansvar = Ansvarsområde(roles),
            Vakt = await users.GetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VaktToken) is { } v && ShellService.Vaktstatuser.Any(s => s.Verdi == v) ? v : "pa",
            Varselvalg = ShellService.LesValg(await users.GetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VarselToken))
        };
        AddShortcuts(model, roles);

        if (roles.Contains(AppRoles.Ressursleverandor))
        {
            var resources = db.Ressurser.AsNoTracking().Where(r => r.EierID == user.Id);
            model.Stats.Add(("Mine ressurser", await resources.CountAsync()));
            model.Stats.Add(("Venter på verifisering", await resources.CountAsync(r => r.Status == Statuser.Ressurs.VenterPåVerifisering)));
        }
        if (roles.Contains(AppRoles.OffentligAktor))
        {
            var needs = db.Behov.AsNoTracking().Where(b => b.RegistrertAvID == user.Id);
            model.Stats.Add(("Behov meldt inn", await needs.CountAsync()));
            model.Stats.Add(("Akutte behov", await needs.CountAsync(b => b.Prioritet == Statuser.Prioritet.Akutt && b.Status != Statuser.Behov.Løst)));
        }
        if (roles.Contains(AppRoles.DispatchTeam) || roles.Contains(AppRoles.Kriseledelse))
        {
            model.Stats.Add(("Åpne behov", await db.Behov.AsNoTracking().CountAsync(b => b.Status != Statuser.Behov.Løst)));
            model.Stats.Add(("Akutte behov", await db.Behov.AsNoTracking().CountAsync(b => b.Prioritet == Statuser.Prioritet.Akutt && b.Status != Statuser.Behov.Løst)));
        }
        if (roles.Contains(AppRoles.Ressursleverandor))
            model.Stats.Add(("Aktive leveranser", await db.Tildelinger.AsNoTracking().CountAsync(t => t.Fullført == null && t.Forespørsel!.Ressurs!.EierID == user.Id)));
        if (roles.Contains(AppRoles.OffentligAktor))
            model.Stats.Add(("Løste behov", await db.Behov.AsNoTracking().CountAsync(b => b.RegistrertAvID == user.Id && b.Status == Statuser.Behov.Løst)));
        if (roles.Contains(AppRoles.DispatchTeam) || roles.Contains(AppRoles.Kriseledelse))
        {
            model.Stats.Add(("Aktive tildelinger", await db.Tildelinger.AsNoTracking().CountAsync(t => t.Fullført == null)));
            model.Stats.Add(("Aktive hendelser", await db.Hendelser.AsNoTracking().CountAsync(h => h.Status == Statuser.Hendelse.Aktiv)));
            model.Stats.Add(("Venter på verifisering", await db.Ressurser.AsNoTracking().CountAsync(r => r.Status == Statuser.Ressurs.VenterPåVerifisering)));
        }
        var kontakter = await (from u in db.Users.AsNoTracking()
                               join ur in db.UserRoles on u.Id equals ur.UserId
                               join r in db.Roles on ur.RoleId equals r.Id
                               where u.Id != user.Id && r.Name != null && AppRoles.All.Contains(r.Name) && u.Email != "verifisering@gmail.com"
                               orderby u.Fornavn, u.Etternavn
                               select new ProfilKontakt(u.Id, u.Fornavn + " " + u.Etternavn, r.Name!, u.Organisasjon, u.Email ?? u.UserName ?? "", u.PhoneNumber)).ToListAsync();
        model.Kontakter.AddRange(kontakter);
        var logg = await db.Hendelseslogg.AsNoTracking().Where(l => l.UtførtAvID == user.Id)
            .OrderByDescending(l => l.Tidspunkt).Take(5).Select(l => new { l.Handling, l.Begrunnelse, l.Tidspunkt }).ToListAsync();
        model.Aktivitet.AddRange(logg.Select(l => (l.Handling, l.Begrunnelse, l.Tidspunkt)));
        await BuildOversiktAsync(db, model, user.Id, roles);
        return View(model);
    }

    private async Task BuildOversiktAsync(AppDbContext db, ProfileViewModel model, int uid, IReadOnlyCollection<string> roles)
    {
        var ov = model.Ov;
        string U(string c, string a, object? r = null) => Url.Action(a, c, r) ?? "#";
        (string, string) P(string p) => p == Statuser.Prioritet.Akutt ? ("Akutt", "c-red") : ("Planlagt", "c-gray");
        (string, string) B(string s) => s switch
        {
            Statuser.Behov.Ny => ("Ny", "c-blue"), Statuser.Behov.Vurderes => ("Vurderes", "c-amber"),
            Statuser.Behov.Tildelt => ("Tildelt", "c-blue"), _ => ("Løst", "c-green")
        };
        string Kort(string s, int n = 60) => s.Length <= n ? s : s[..n].TrimEnd() + "…";
        var kl = roles.Contains(AppRoles.Kriseledelse);
        var dp = roles.Contains(AppRoles.DispatchTeam);
        var rl = roles.Contains(AppRoles.Ressursleverandor);
        var oa = !kl && !dp && !rl;

        var ulest = await db.Meldinger.AsNoTracking().CountAsync(m => m.MottakerID == uid && m.Lest == null);
        var aktiveH = await db.Hendelser.AsNoTracking().Where(h => h.Status == Statuser.Hendelse.Aktiv).OrderBy(h => h.Starttidspunkt).ToListAsync();
        var aktiveTild = db.Tildelinger.AsNoTracking().Where(t => t.Fullført == null);
        var åpne = db.Behov.AsNoTracking().Where(b => b.Status != Statuser.Behov.Løst);
        var udekkedeAkutte = (await db.Behov.AsNoTracking().Include(b => b.Forespørsler).ThenInclude(f => f.Tildeling)
            .Where(b => b.Status != Statuser.Behov.Løst && b.Prioritet == Statuser.Prioritet.Akutt).ToListAsync())
            .Where(b => b.Mangler > 0).Select(b => b.BehovID).ToList();
        var akutte = åpne.Where(b => udekkedeAkutte.Contains(b.BehovID));
        var hovedToken = await db.UserTokens.AsNoTracking().Where(x => x.UserId == uid && x.LoginProvider == "Profil" && x.Name == "Hovedhendelse").Select(x => x.Value).FirstOrDefaultAsync();
        var festToken = await db.UserTokens.AsNoTracking().Where(x => x.UserId == uid && x.LoginProvider == "Profil" && x.Name == "FestetHendelser").Select(x => x.Value).FirstOrDefaultAsync();
        var festet = (festToken ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var i) ? i : -1).ToList();
        Hendelse? hovedH = aktiveH.Count == 1 ? aktiveH[0] : aktiveH.FirstOrDefault(x => x.HendelseID.ToString() == hovedToken) ?? aktiveH.FirstOrDefault();
        var venterVerif = db.Ressurser.AsNoTracking().Where(r => r.Status == Statuser.Ressurs.VenterPåVerifisering);
        var logQuery = db.Hendelseslogg.AsNoTracking().OrderByDescending(l => l.Tidspunkt);

        OvRad Logg(string h, string d, DateTime t) => new() { Tittel = ShellService.Menneskelig(h), Under = d, Tid = t };
        OvRad HRad(Hendelse h, string knapp, string url) => new()
        {
            Tittel = h.Navn, Under = $"Pågående · Startet {h.Starttidspunkt.ToLocalTime():dd.MM HH:mm}", Tekst = h.Beskrivelse,
            Chip = h.Prioritet == Statuser.HendelsePrioritet.Hoy ? "Høy prioritet" : h.Prioritet == Statuser.HendelsePrioritet.Medium ? "Middels prioritet" : "Lav prioritet",
            ChipK = h.Prioritet == Statuser.HendelsePrioritet.Hoy ? "c-red" : h.Prioritet == Statuser.HendelsePrioritet.Medium ? "c-amber" : "c-gray",
            Knapp = knapp, Url = url, Ikon = h.Navn
        };
        async Task<List<OvRad>> BehovRader(IQueryable<Behov> q, string knapp, int n = 3, string anker = "#behov-") =>
            (await q.Include(b => b.Behovstype).OrderByDescending(b => b.Registrert).Take(n).ToListAsync()).Select(b =>
            {
                var (pc, pk) = P(b.Prioritet); var (sc, sk) = B(b.Status);
                return new OvRad { Tittel = $"{b.Behovstype?.Navn}: {Kort(b.Beskrivelse, 46)}", Under = b.Stedsnavn, Chip = pc, ChipK = pk, Chip2 = sc, Chip2K = sk, Tid = b.Registrert, Knapp = knapp, Url = U("Home", "Dashboard") + (anker.EndsWith("-") ? anker + b.BehovID : anker), Ikon = b.Behovstype?.Navn ?? "" };
            }).ToList();

        if (kl)
        {
            ov.Stats.Add(new("Aktive hendelser", aktiveH.Count, "alarm"));
            ov.Stats.Add(new("Akutte behov", await akutte.CountAsync(), "layers"));
            ov.Stats.Add(new("Beslutninger venter", await åpne.CountAsync(b => b.Status == Statuser.Behov.Ny || b.Status == Statuser.Behov.Vurderes), "hammer"));
            ov.Stats.Add(new("Meldinger", ulest, "chat", ulest > 0 ? $"{ulest} ulest" : null));
            var h = hovedH;
            if (h is not null)
            {
                ov.Hoved = HRad(h, "Åpne arbeidsrom", U("Hendelse", "Arbeidsrom", new { id = h.HendelseID }));
                ov.HovedBehov = await db.Behov.CountAsync(b => b.HendelseID == h.HendelseID);
            }
            ov.A.AddRange(await BehovRader(åpne.Where(b => b.Status == Statuser.Behov.Ny || b.Status == Statuser.Behov.Vurderes).OrderByDescending(b => b.Prioritet == Statuser.Prioritet.Akutt), "Se sak"));
            ov.ResTilgjengelig = await db.Ressurser.CountAsync(r => r.Status == Statuser.Ressurs.Tilgjengelig);
            ov.ResAktive = await aktiveTild.CountAsync();
            ov.ResVenter = await db.Forespørsler.CountAsync(f => f.Svar == Statuser.Svar.Venter);
            var tot = await db.Ressurser.SumAsync(r => (decimal?)r.Antall) ?? 0; var res = await db.Ressurser.SumAsync(r => (decimal?)r.ReservertAntall) ?? 0;
            ov.Utnyttelse = tot > 0 ? (int)Math.Round(res / tot * 100) : 0;
            ov.Aktivitet.AddRange((await logQuery.Take(3).ToListAsync()).Select(l => Logg(l.Handling, l.Begrunnelse, l.Tidspunkt)));
        }
        else if (dp)
        {
            ov.AntA = await akutte.CountAsync(); ov.AntB = await db.Forespørsler.CountAsync(f => f.Svar == Statuser.Svar.Godtatt && f.Tildeling == null); ov.AntC = await venterVerif.CountAsync();
            ov.Stats.Add(new("Åpne behov", await åpne.CountAsync(), "clipboard"));
            ov.Stats.Add(new("Akutte behov", ov.AntA, "warning"));
            ov.Stats.Add(new("Forespørsler", await db.Forespørsler.CountAsync(f => f.Svar == Statuser.Svar.Venter), "mail"));
            ov.Stats.Add(new("Aktive tildelinger", await aktiveTild.CountAsync(), "layers"));
            ov.Stats.Add(new("Venter på verifisering", ov.AntC, "shield"));
            ov.A.AddRange(await BehovRader(akutte, "Håndter"));
            ov.B.AddRange((await db.Forespørsler.AsNoTracking().Include(f => f.Ressurs).ThenInclude(r => r!.Eier).Where(f => f.Svar == Statuser.Svar.Godtatt && f.Tildeling == null)
                .OrderByDescending(f => f.Svart).Take(3).ToListAsync()).Select(f => new OvRad
                {
                    Tittel = f.Ressurs?.Eier?.Organisasjon ?? f.Ressurs?.Navn ?? "Leverandør", Under = $"Svar på forespørsel #F-{f.ForespørselID}: {(f.Svar == Statuser.Svar.Godtatt ? "godtatt" : "avslått")}",
                    Tid = f.Svart, Knapp = "Vurder", Url = U("Home", "Dashboard") + "#foresporsel-" + f.ForespørselID, Ikon = f.Ressurs?.Navn ?? ""
                }));
            ov.C.AddRange((await venterVerif.Include(r => r.Eier).Include(r => r.Ressurstype).OrderBy(r => r.Registrert).Take(3).ToListAsync()).Select(r => new OvRad
            {
                Tittel = $"{r.Navn}{(r.Antall > 1 ? $" ({r.Antall:0.##})" : "")}", Under = r.Eier?.Organisasjon ?? "", Tid = r.Registrert, Knapp = "Verifiser", Url = U("Home", "Dashboard") + "#verifisering-" + r.RessursID, Ikon = r.Ressurstype?.Navn ?? ""
            }));
            foreach (var h in aktiveH.OrderBy(x => x.HendelseID == hovedH?.HendelseID ? 0 : festet.Contains(x.HendelseID) ? 1 : 2).ThenBy(x => x.Starttidspunkt).Take(5)) ov.Hendelser.Add(HRad(h, "Arbeidsrom", U("Hendelse", "Arbeidsrom", new { id = h.HendelseID })));
            var mine = await db.Forespørsler.CountAsync(f => f.SendtAvID == uid);
            ov.Oppgaver.Add(("Verifisere nye ressurser", ov.AntC == 0));
            ov.Oppgaver.Add(("Sende forespørsler og opprette tildelinger", mine > 0));
            ov.Oppgaver.Add(("Følge leveranser til de er fullført", !await aktiveTild.AnyAsync()));
            ov.Oppgaver.Add(("Koordinere akutte behov med relevante aktører", ov.AntA == 0));
            ov.Oppgaver.Add(("Oppdatere hendelser og gi status i arbeidsrom", aktiveH.Count == 0));
            ov.Aktivitet.AddRange((await logQuery.Where(l => l.UtførtAvID == uid).Take(3).ToListAsync()).Select(l => Logg(l.Handling, l.Begrunnelse, l.Tidspunkt)));
        }
        else if (rl)
        {
            var mine = db.Ressurser.AsNoTracking().Where(r => r.EierID == uid);
            var venter = db.Forespørsler.AsNoTracking().Where(f => f.Svar == Statuser.Svar.Venter && f.Ressurs!.EierID == uid);
            var mineTild = aktiveTild.Where(t => t.Forespørsel!.Ressurs!.EierID == uid);
            ov.Stats.Add(new("Mine ressurser", await mine.CountAsync(), "truck"));
            ov.Stats.Add(new("Tilgjengelige", await mine.CountAsync(r => r.Status == Statuser.Ressurs.Tilgjengelig), "check"));
            ov.Stats.Add(new("Forespørsler til meg", await venter.CountAsync(), "doc"));
            ov.Stats.Add(new("Aktive leveranser", await mineTild.CountAsync(), "layers"));
            ov.Stats.Add(new("Venter på verifisering", await mine.CountAsync(r => r.Status == Statuser.Ressurs.VenterPåVerifisering), "shield"));
            ov.A.AddRange((await mine.Include(r => r.Ressurstype).Where(r => r.Status == Statuser.Ressurs.VenterPåVerifisering).Take(3).ToListAsync()).Select(r => new OvRad
            { Tittel = r.Navn, Under = $"Venter på verifisering · Registrert {r.Registrert.ToLocalTime():dd.MM HH:mm}", Knapp = "Se ressurs", Url = U("Home", "Resources") + "#ressurs-" + r.RessursID, Ikon = r.Ressurstype?.Navn ?? "" }));
            ov.A.AddRange((await venter.Include(f => f.Ressurs).Include(f => f.Behov).Take(3).ToListAsync()).Select(f => new OvRad
            { Tittel = $"Forespørsel: {f.Ressurs?.Navn}", Under = $"Venter på svar · {f.Behov?.Stedsnavn}", Knapp = "Svar", Url = U("Home", "Dashboard") + "#innkommende-" + f.ForespørselID, Ikon = "doc" }));
            ov.AntA = ov.A.Count;
            foreach (var g in (await mine.Include(r => r.Ressurstype).ToListAsync()).GroupBy(r => r.Ressurstype?.Navn ?? "Annet"))
                ov.Typer.Add((g.Key, g.Count(), g.Count(r => r.Status == Statuser.Ressurs.Tilgjengelig)));
            ov.AntB = await mineTild.CountAsync();
            ov.Leveranser.AddRange((await mineTild.Include(t => t.Forespørsel!).ThenInclude(f => f.Ressurs).ThenInclude(r => r!.Ressurstype).Include(t => t.Forespørsel!).ThenInclude(f => f.Behov)
                .OrderByDescending(t => t.Tildelt).Take(4).ToListAsync()).Select(t => new OvRad
                {
                    Tittel = t.Forespørsel!.Ressurs!.Navn, Under = t.Forespørsel.Behov?.Stedsnavn ?? "", Tekst = $"{t.Forespørsel.Antall:0.##} {t.Forespørsel.Ressurs.Enhet}",
                    Chip = Statuser.Visning(t.Fremdrift), ChipK = t.Fremdrift == Statuser.Fremdrift.PåVei ? "c-blue" : "c-green",
                    Knapp = t.Fremdrift == Statuser.Fremdrift.Bekreftet ? "Planlegg rute" : t.Fremdrift == Statuser.Fremdrift.PåVei ? "Oppdater status" : "Bekreft levering",
                    Url = U("Home", "Resources") + "#ressurs-" + t.Forespørsel.Ressurs.RessursID, Ikon = t.Forespørsel.Ressurs.Ressurstype?.Navn ?? ""
                }));
            ov.Aktivitet.AddRange((await logQuery.Where(l => l.UtførtAvID == uid).Take(3).ToListAsync()).Select(l => Logg(l.Handling, l.Begrunnelse, l.Tidspunkt)));
        }
        else
        {
            var mine = db.Behov.AsNoTracking().Where(b => b.RegistrertAvID == uid);
            ov.Stats.Add(new("Mine behov", await mine.CountAsync(), "clipboard"));
            ov.Stats.Add(new("Behov under behandling", await mine.CountAsync(b => b.Status == Statuser.Behov.Vurderes || b.Status == Statuser.Behov.Tildelt), "refresh"));
            ov.Stats.Add(new("Akutte behov", await mine.CountAsync(b => b.Prioritet == Statuser.Prioritet.Akutt && b.Status != Statuser.Behov.Løst), "warning"));
            ov.Stats.Add(new("Aktive hendelser", aktiveH.Count, "signal"));
            ov.Stats.Add(new("Meldinger", ulest, "mail"));
            ov.A.AddRange(await BehovRader(mine, "Åpne", 4, "#mine-behov"));
            foreach (var h in aktiveH.Take(2)) ov.Hendelser.Add(HRad(h, "Åpne hendelse", U("Home", "Map")));
            var ids = await mine.Select(b => b.BehovID).ToListAsync();
            ov.Aktivitet.AddRange((await logQuery.Where(l => l.BehovID != null && ids.Contains(l.BehovID.Value)).Take(3).ToListAsync()).Select(l => Logg(l.Handling, l.Begrunnelse, l.Tidspunkt)));
        }

        string D(string anker) => U("Home", "Dashboard") + "#" + anker;
        var mål = new Dictionary<string, string>
        {
            ["Aktive hendelser"] = kl ? U("Hendelse", "Index") : U("Home", "Map"),
            ["Akutte behov"] = kl ? D("behov-prioritet") : dp ? D("akutte-behov") : D("mine-akutte-behov"),
            ["Beslutninger venter"] = D("behov-prioritet"),
            ["Meldinger"] = U("Messages", "Inbox"),
            ["Åpne behov"] = D("apne-behov"),
            ["Forespørsler"] = D("foresporsler"),
            ["Aktive tildelinger"] = D("tildelinger"),
            ["Venter på verifisering"] = dp ? D("verifiseringskoe") : U("Home", "Resources"),
            ["Mine ressurser"] = U("Home", "Resources"),
            ["Tilgjengelige"] = U("Home", "Resources"),
            ["Forespørsler til meg"] = D("mine-foresporsler"),
            ["Aktive leveranser"] = U("Home", "Resources"),
            ["Mine behov"] = D("mine-behov"),
            ["Behov under behandling"] = D("behov-under-behandling")
        };
        for (var i = 0; i < ov.Stats.Count; i++)
            if (mål.TryGetValue(ov.Stats[i].Label, out var url)) ov.Stats[i] = ov.Stats[i] with { Url = url };
    }


    private static string Ansvarsområde(IReadOnlyCollection<string> roles) =>
        roles.Contains(AppRoles.Kriseledelse) ? "Overordnet ledelse og brukeradministrasjon"
        : roles.Contains(AppRoles.DispatchTeam) ? "Koordinering av behov, forespørsler og leveranser"
        : roles.Contains(AppRoles.Ressursleverandor) ? "Ressurser og beholdning"
        : roles.Contains(AppRoles.OffentligAktor) ? "Meldte behov og oppfølging" : "Ikke angitt";

    private static void AddShortcuts(ProfileViewModel model, IReadOnlyCollection<string> roles)
    {
        model.Shortcuts.Add(("Oversikt", "Se status og det som krever handling nå.", "Home", "Dashboard"));
        model.Shortcuts.Add(("Kart", "Ressurser og akutte behov på kartet.", "Home", "Map"));
        if (roles.Contains(AppRoles.OffentligAktor))
            model.Shortcuts.Add(("Meld behov", "Send inn et nytt behov med sted.", "Behov", "Registrer"));
        if (roles.Contains(AppRoles.Ressursleverandor))
            model.Shortcuts.Add(("Mine ressurser", "Se, rediger og følg opp ressursene dine.", "Home", "Mine"));
        if (roles.Contains(AppRoles.DispatchTeam) || roles.Contains(AppRoles.Kriseledelse))
            model.Shortcuts.Add(("Hendelser", "Koordiner hendelser og tildelinger.", "Hendelse", "Index"));
        if (roles.Contains(AppRoles.Kriseledelse))
            model.Shortcuts.Add(("Brukere", "Administrer kontoer og roller.", "Admin", "Index"));
    }

    // «Lagre endringer» på profilsiden: varselvalgene styrer hva bjellen viser (ShellService), og vaktstatus lagres på kontoen.
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreferences(string[]? varsel, string? vakt)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VarselToken, ShellService.SkrivValg(varsel ?? []));
        if (vakt is not null && ShellService.Vaktstatuser.Any(s => s.Verdi == vakt))
            await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.VaktToken, vakt);
        TempData["ProfileMessage"] = "Endringene er lagret.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(ProfileEditModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid)
        {
            TempData["ProfileError"] = "Kontroller opplysningene: navn og et gyldig telefonnummer må oppgis.";
            return RedirectToAction(nameof(Profile));
        }
        user.Fornavn = model.Fornavn.Trim();
        user.Etternavn = model.Etternavn.Trim();
        user.PhoneNumber = model.Telefon.Trim();
        user.Organisasjon = string.IsNullOrWhiteSpace(model.Organisasjon) ? null : model.Organisasjon.Trim();
        var result = await users.UpdateAsync(user);
        if (result.Succeeded) TempData["ProfileMessage"] = "Opplysningene dine er oppdatert.";
        else TempData["ProfileError"] = "Kunne ikke lagre endringene. Prøv igjen.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid)
        {
            TempData["ProfileError"] = "Passordet må være minst 12 tegn, og de to nye passordene må være like.";
            return RedirectToAction(nameof(Profile));
        }
        var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await signIn.RefreshSignInAsync(user);
            TempData["ProfileMessage"] = "Passordet er byttet.";
        }
        else TempData["ProfileError"] = "Kunne ikke bytte passord. Kontroller det nåværende passordet og passordkravene.";
        return RedirectToAction(nameof(Profile));
    }

    // Profilbildet lagres som et lite JPEG-bilde (nettleseren skalerer det ned) i Identity sin tokentabell.
    private const string AvatarProvider = "Profil";
    private const string AvatarToken = "Profilbilde";
    private const string JpegPrefix = "data:image/jpeg;base64,";

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAvatar(string? bilde)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        if (bilde is not null && bilde.StartsWith(JpegPrefix, StringComparison.Ordinal) && bilde.Length < 200_000
            && IsJpeg(bilde[JpegPrefix.Length..]))
        {
            await users.SetAuthenticationTokenAsync(user, AvatarProvider, AvatarToken, bilde);
            TempData["ProfileMessage"] = "Profilbildet er oppdatert.";
        }
        else TempData["ProfileError"] = "Bildet kunne ikke brukes. Velg et vanlig bilde (JPEG, PNG eller WebP).";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAvatar()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        await users.RemoveAuthenticationTokenAsync(user, AvatarProvider, AvatarToken);
        TempData["ProfileMessage"] = "Profilbildet er fjernet.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Avatar()
    {
        var user = await users.GetUserAsync(User);
        var data = user is null ? null : await users.GetAuthenticationTokenAsync(user, AvatarProvider, AvatarToken);
        if (data is null || !data.StartsWith(JpegPrefix, StringComparison.Ordinal)) return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(Convert.FromBase64String(data[JpegPrefix.Length..]), "image/jpeg");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationsSeen()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var modell = await shell.HentAsync(User);
        await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.SettToken, string.Join('\n', modell.Nøkler));
        return NoContent();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationSeen(string key)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var modell = await shell.HentAsync(User);
        if (!modell.Nøkler.Contains(key)) return NoContent();
        var sett = (await users.GetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.SettToken) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        sett.Add(key);
        await users.SetAuthenticationTokenAsync(user, ShellService.Provider, ShellService.SettToken, string.Join('\n', sett.Where(modell.Nøkler.Contains)));
        return NoContent();
    }

    private static bool IsJpeg(string base64)
    {
        var buffer = new byte[base64.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(base64, buffer, out var written) && written > 3
            && buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF;
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => View();
}
