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
    [HttpGet]
    public async Task<IActionResult> Inbox()
    {
        var actorId = ResourcePermissions.BrukerId(User);
        if (actorId is null) return Forbid();

        var unread = await db.Meldinger.Where(m => m.MottakerID == actorId && m.Lest == null).ToListAsync();
        if (unread.Count > 0)
        {
            var readAt = DateTime.UtcNow;
            unread.ForEach(message => message.Lest = readAt);
            await db.SaveChangesAsync();
        }

        var messageEntities = await db.Meldinger.AsNoTracking().Where(m => m.MottakerID == actorId)
            .Include(m => m.SendtAv).OrderByDescending(m => m.Sendt).Take(100)
            .ToListAsync();
        var messages = messageEntities.Select(m => new InboxMessageEntry(
            m.MeldingID, m.SendtAv?.FulltNavn ?? "Ukjent bruker", "", m.Emne, m.Innhold, m.Sendt, m.Lest == null)).ToList();
        return View(new InboxViewModel { Messages = messages });
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
}
