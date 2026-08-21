namespace Zettel.Domain.Enums;

public enum MatchStatus
{
    Scheduled,
    InProgress,
    Completed,
    Cancelled,
    WalkoverWin,
    // Both participants lose: mutual disqualification or mutual no-show. Terminal, but
    // unlike Cancelled the fight did happen — score is kept and the bracket cell stays
    // occupied (АР-16).
    DoubleLoss
}
