namespace WordTileScorer.Core;

public sealed record Player(Guid Id, string Name)
{
    public static Player Create(string name) => new(Guid.NewGuid(), RequireName(name));

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A player name is required.", nameof(name))
            : name.Trim();
}

public sealed record Team(Guid Id, string Name, IReadOnlyList<Guid> PlayerIds)
{
    public static Team Create(string name, IEnumerable<Guid> playerIds)
    {
        var members = playerIds.Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A team name is required.", nameof(name));
        if (members.Length == 0) throw new ArgumentException("A team must contain at least one player.", nameof(playerIds));
        return new Team(Guid.NewGuid(), name.Trim(), members);
    }
}

public enum CompetitionKind { Casual, League, Knockout }
public enum CompetitionStatus { Draft, Active, Completed }

public sealed record Competition(
    Guid Id,
    string Name,
    CompetitionKind Kind,
    CompetitionStatus Status,
    DateOnly? StartsOn,
    DateOnly? EndsOn);

public sealed record Season(Guid Id, Guid CompetitionId, string Name, DateOnly StartsOn, DateOnly EndsOn);

public sealed record LeagueSettings(Guid CompetitionId, int MeetingsPerOpponent, int WinPoints, int DrawPoints, int LossPoints);

public sealed record LeagueFixture(
    Guid Id,
    Guid CompetitionId,
    Guid HomeEntrantId,
    Guid AwayEntrantId,
    DateTimeOffset? ScheduledAt,
    Guid? GameId);

public sealed record KnockoutMatch(
    Guid Id,
    Guid CompetitionId,
    int Round,
    int Position,
    Guid? EntrantOneId,
    Guid? EntrantTwoId,
    Guid? WinnerId,
    Guid? GameId);

public enum GameMode { Individual, Teams }

public sealed class GameState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GameMode Mode { get; init; }
    public List<Player> Players { get; init; } = [];
    public List<Team> Teams { get; init; } = [];
    public List<Guid> TurnOrder { get; init; } = [];
    public int CurrentTurnIndex { get; set; }
    public List<Turn> Turns { get; init; } = [];
    public bool IsFinished { get; set; }

    public Player CurrentPlayer => Players.Single(p => p.Id == TurnOrder[CurrentTurnIndex]);

    public Team? TeamFor(Guid playerId) => Teams.SingleOrDefault(t => t.PlayerIds.Contains(playerId));

    public int PlayerTotal(Guid playerId) => Turns.Where(t => t.PlayerId == playerId).Sum(t => t.Score);

    public int TeamTotal(Guid teamId)
    {
        var ids = Teams.Single(t => t.Id == teamId).PlayerIds;
        return Turns.Where(t => ids.Contains(t.PlayerId)).Sum(t => t.Score);
    }
}

public sealed record Turn(
    Guid Id,
    Guid PlayerId,
    Guid? TeamId,
    DateTimeOffset PlayedAt,
    string Word,
    IReadOnlyList<LetterPlay> Letters,
    int WordMultiplier,
    int Score,
    bool IsPass);

public sealed record LetterPlay(char Letter, int BaseValue, int LetterMultiplier)
{
    public int Score => BaseValue * LetterMultiplier;
}
