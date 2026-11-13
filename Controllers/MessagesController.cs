using HeimevernetInnlevering1.Data;
using HeimevernetInnlevering1.Models;
using HeimevernetInnlevering1.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeimevernetInnlevering1.Controllers;

[Authorize(Roles = AppRoles.Any)]
public class MessagesController(AppDbContext db) : Controller
{
    private const string GroupPrefix = "Gruppechat: ";
    private const string ParticipantMarker = "\n\nDeltakere: ";

    [HttpGet]
    public async Task<IActionResult> Inbox(int? with)
    {
        var actorId = ResourcePermissions.BrukerId(User);
        if (actorId is null) return Forbid();
        var me = actorId.Value;

        var unread = await db.Meldinger.Where(m => m.MottakerID == me && m.Lest == null).ToListAsync();
        var unreadIds = unread.Select(m => m.MeldingID).ToHashSet();
        if (unread.Count > 0)
        {
            var readAt = DateTime.UtcNow;
            unread.ForEach(message => message.Lest = readAt);
            await db.SaveChangesAsync();
        }

        var entities = await db.Meldinger.AsNoTracking().Where(m => m.MottakerID == me || m.SendtAvID == me)
            .Include(m => m.SendtAv).Include(m => m.Mottaker)
            .OrderByDescending(m => m.Sendt).Take(1500).ToListAsync();
        entities.Reverse();

        var users = await db.Users.AsNoTracking().ToListAsync();
        var byId = users.ToDictionary(user => user.Id);
        var byName = users.GroupBy(user => user.FulltNavn).ToDictionary(g => g.Key, g => g.First().Id);
        var roleRows = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name }).ToListAsync();
        var roleOf = roleRows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.First().Name ?? "");
        string RoleClass(string role) => role == AppRoles.Kriseledelse ? "krise" : role == AppRoles.Ressursleverandor ? "leverandor" : role == AppRoles.DispatchTeam ? "dispatch" : "annen";
        string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(d => char.ToUpperInvariant(d[0])));
        var myName = byId.TryGetValue(me, out var self) ? self.FulltNavn : "";

        var buckets = new Dictionary<string, List<(Melding M, string Subject, string Body)>>();
        var groupInfo = new Dictionary<string, (string Title, List<string> Names)>();
        var seen = new HashSet<string>();
        foreach (var m in entities)
        {
            if (m.Emne.StartsWith(GroupPrefix, StringComparison.Ordinal))
            {
                var cut = m.Innhold.LastIndexOf(ParticipantMarker, StringComparison.Ordinal);
                var body = cut >= 0 ? m.Innhold[..cut] : m.Innhold;
                var names = cut >= 0
                    ? m.Innhold[(cut + ParticipantMarker.Length)..].Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList()
                    : [m.SendtAv?.FulltNavn ?? "", m.Mottaker?.FulltNavn ?? ""];
                var title = m.Emne[GroupPrefix.Length..];
                if (!names.Contains(m.SendtAv?.FulltNavn ?? "")) names.Add(m.SendtAv?.FulltNavn ?? "");
                var key = "g|" + title + "|" + string.Join(",", names.OrderBy(n => n, StringComparer.Ordinal));
                if (!seen.Add($"{key}|{m.SendtAvID}|{m.Innhold}|{m.Sendt.Ticks / TimeSpan.TicksPerSecond}")) continue;
                groupInfo[key] = (title, names);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = [];
                list.Add((m, "", body));
            }
            else
            {
                var other = m.SendtAvID == me ? m.MottakerID : m.SendtAvID;
                var key = "d|" + other;
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = [];
                list.Add((m, m.Emne, m.Innhold));
            }
        }

        var threads = new List<MessageThread>();
        foreach (var (key, list) in buckets)
        {
            var msgs = list.Select(x => new ThreadMessage(x.M.SendtAvID, x.M.SendtAv?.FulltNavn ?? "Ukjent bruker", x.M.SendtAvID == me,
                x.Subject, x.Body, x.M.Sendt)).ToList();
            var last = list[^1];
            var unreadCount = list.Count(x => unreadIds.Contains(x.M.MeldingID));
            var preview = (last.M.SendtAvID == me ? "Du: " : "") + last.Body.Replace("\n", " ");
            if (preview.Length > 110) preview = preview[..110] + "…";
            if (key.StartsWith("g|"))
            {
                var info = groupInfo[key];
                var others = info.Names.Where(n => n != myName).ToList();
                var ids = others.Where(byName.ContainsKey).Select(n => byName[n]).ToList();
                threads.Add(new MessageThread(key, true, info.Title, $"Gruppechat · {info.Names.Count} deltakere", "gruppe", "G",
                    ids, info.Names.OrderBy(n => n).ToList(), info.Title, unreadCount, last.M.Sendt, preview, msgs));
            }
            else
            {
                var otherId = int.Parse(key[2..]);
                var name = byId.TryGetValue(otherId, out var o) ? o.FulltNavn : "Ukjent bruker";
                var role = roleOf.GetValueOrDefault(otherId, "");
                var subj = list.Select(x => x.Subject).LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
                while (subj.StartsWith("SV: ", StringComparison.Ordinal)) subj = subj[4..];
                threads.Add(new MessageThread(key, false, name, string.IsNullOrEmpty(role) ? "Kontakt" : AppRoles.Label(role), RoleClass(role),
                    Initials(name), [otherId], [name], string.IsNullOrWhiteSpace(subj) ? "Melding" : subj, unreadCount, last.M.Sendt, preview, msgs));
            }
        }

        if (with is int partner && partner != me && byId.TryGetValue(partner, out var contact) && !threads.Any(t => t.Key == "d|" + partner))
        {
            var role = roleOf.GetValueOrDefault(partner, "");
            threads.Add(new MessageThread("d|" + partner, false, contact.FulltNavn, string.IsNullOrEmpty(role) ? "Kontakt" : AppRoles.Label(role), RoleClass(role),
                Initials(contact.FulltNavn), [partner], [contact.FulltNavn], "Melding", 0, DateTime.UtcNow, "", []));
        }
        var ordered = threads.OrderByDescending(t => t.LastAtUtc).ToList();
        if (with is int w) ViewBag.OpenIndex = ordered.FindIndex(t => t.Key == "d|" + w);
        return View(new InboxViewModel { Threads = ordered });

    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int recipientId, string subject, string body, string? returnUrl)
    {
        var senderId = ResourcePermissions.BrukerId(User);
        if (senderId is null) return Forbid();
        if (recipientId == senderId) return BadRequest("Du kan ikke sende en melding til deg selv.");
        if (!await db.Users.AnyAsync(user => user.Id == recipientId)) return NotFound();
        if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 150 ||
            string.IsNullOrWhiteSpace(body) || body.Trim().Length > 2000)
        {
            TempData["MessageError"] = "Skriv et emne og en melding på maksimalt 2 000 tegn.";
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Contacts", "Home")!);
        }

        db.Meldinger.Add(new Melding
        {
            SendtAvID = senderId.Value,
            MottakerID = recipientId,
            Emne = subject.Trim(),
            Innhold = body.Trim()
        });
        await db.SaveChangesAsync();
        TempData["MessageSuccess"] = "Meldingen er sendt.";
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Contacts", "Home")!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendGroup(int[] recipientIds, string subject, string body, string? returnUrl)
    {
        var senderId = ResourcePermissions.BrukerId(User);
        if (senderId is null) return Forbid();
        var back = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Contacts", "Home")!;
        var ids = recipientIds.Where(id => id != senderId).Distinct().ToList();
        var members = await db.Users.Where(user => ids.Contains(user.Id)).ToListAsync();
        if (members.Count < 2 || string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > 140 ||
            string.IsNullOrWhiteSpace(body) || body.Trim().Length > 1800)
        {
            TempData["MessageError"] = "Velg minst to personer og skriv et emne og en melding.";
            return LocalRedirect(back);
        }

        var senderName = (await db.Users.AsNoTracking().FirstAsync(user => user.Id == senderId)).FulltNavn;
        var deltakere = string.Join(", ", members.Select(member => member.FulltNavn).Append(senderName).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var member in members)
        {
            db.Meldinger.Add(new Melding
            {
                SendtAvID = senderId.Value,
                MottakerID = member.Id,
                Emne = "Gruppechat: " + subject.Trim(),
                Innhold = body.Trim() + ParticipantMarker + deltakere
            });
        }
        await db.SaveChangesAsync();
        TempData["MessageSuccess"] = $"Gruppemeldingen er sendt til {members.Count} personer.";
        return LocalRedirect(back);
    }}
