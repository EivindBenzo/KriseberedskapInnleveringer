namespace HeimevernetInnlevering1.Models;

public record AssignDialogModel(Behov Need, List<Ressurs> Matches, string ReturnUrl, string Icon);
