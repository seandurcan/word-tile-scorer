namespace WordTileScorer.Core;

public sealed class WordScoreBuilder
{
    private readonly List<LetterPlay> _letters = [];

    public IReadOnlyList<LetterPlay> Letters => _letters;
    public int WordMultiplier { get; private set; } = 1;
    public int Subtotal => _letters.Sum(x => x.Score);
    public int Total => checked(Subtotal * WordMultiplier);
    public string Word => new(_letters.Select(x => x.Letter).ToArray());

    public void AddLetter(char letter, int baseValue, int letterMultiplier = 1)
    {
        if (!char.IsLetter(letter)) throw new ArgumentException("A letter is required.", nameof(letter));
        if (baseValue < 0) throw new ArgumentOutOfRangeException(nameof(baseValue));
        if (letterMultiplier is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(letterMultiplier));
        _letters.Add(new LetterPlay(char.ToUpperInvariant(letter), baseValue, letterMultiplier));
    }

    public void RemoveLastLetter()
    {
        if (_letters.Count > 0) _letters.RemoveAt(_letters.Count - 1);
    }

    public void SetWordMultiplier(int multiplier)
    {
        if (multiplier is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(multiplier));
        WordMultiplier = multiplier;
    }
}

public sealed class GameEngine
{
    public GameState CreateGame(
        IReadOnlyList<Player> orderedPlayers,
        GameMode mode,
        IReadOnlyList<Team>? teams = null)
    {
        if (orderedPlayers.Count is < 2 or > 8)
            throw new ArgumentOutOfRangeException(nameof(orderedPlayers), "A game requires 2 to 8 players.");
        if (orderedPlayers.Select(p => p.Id).Distinct().Count() != orderedPlayers.Count)
            throw new ArgumentException("Each player may occur only once.", nameof(orderedPlayers));

        var selectedTeams = teams?.ToList() ?? [];
        if (mode == GameMode.Teams)
            ValidateTeams(orderedPlayers, selectedTeams);
        else if (selectedTeams.Count != 0)
            throw new ArgumentException("Individual games cannot contain teams.", nameof(teams));

        return new GameState
        {
            Mode = mode,
            Players = orderedPlayers.ToList(),
            Teams = selectedTeams,
            TurnOrder = orderedPlayers.Select(p => p.Id).ToList()
        };
    }

    public Turn RecordWord(GameState game, WordScoreBuilder score, DateTimeOffset? playedAt = null)
    {
        EnsurePlayable(game);
        if (score.Letters.Count == 0) throw new InvalidOperationException("Enter at least one letter or use Pass.");
        return AddTurn(game, score.Word, score.Letters.ToArray(), score.WordMultiplier, score.Total, false, playedAt);
    }

    public Turn Pass(GameState game, DateTimeOffset? playedAt = null)
    {
        EnsurePlayable(game);
        return AddTurn(game, string.Empty, [], 1, 0, true, playedAt);
    }

    public Turn UndoLastTurn(GameState game)
    {
        if (game.Turns.Count == 0) throw new InvalidOperationException("There is no turn to undo.");
        var removed = game.Turns[^1];
        game.Turns.RemoveAt(game.Turns.Count - 1);
        game.CurrentTurnIndex = (game.CurrentTurnIndex - 1 + game.TurnOrder.Count) % game.TurnOrder.Count;
        return removed;
    }

    private static Turn AddTurn(
        GameState game,
        string word,
        IReadOnlyList<LetterPlay> letters,
        int wordMultiplier,
        int score,
        bool isPass,
        DateTimeOffset? playedAt)
    {
        var player = game.CurrentPlayer;
        var turn = new Turn(
            Guid.NewGuid(), player.Id, game.TeamFor(player.Id)?.Id,
            playedAt ?? DateTimeOffset.Now, word, letters, wordMultiplier, score, isPass);
        game.Turns.Add(turn);
        game.CurrentTurnIndex = (game.CurrentTurnIndex + 1) % game.TurnOrder.Count;
        return turn;
    }

    private static void EnsurePlayable(GameState game)
    {
        if (game.IsFinished) throw new InvalidOperationException("The game is finished.");
        if (game.TurnOrder.Count == 0) throw new InvalidOperationException("The game has no turn order.");
    }

    private static void ValidateTeams(IReadOnlyList<Player> players, IReadOnlyList<Team> teams)
    {
        if (teams.Count < 2) throw new ArgumentException("Team mode requires at least two teams.", nameof(teams));
        var validIds = players.Select(p => p.Id).ToHashSet();
        var assigned = teams.SelectMany(t => t.PlayerIds).ToArray();
        if (assigned.Any(id => !validIds.Contains(id)))
            throw new ArgumentException("A team contains a player who is not in this game.", nameof(teams));
        if (assigned.Distinct().Count() != assigned.Length)
            throw new ArgumentException("A player cannot belong to more than one team.", nameof(teams));
        if (assigned.Length != players.Count)
            throw new ArgumentException("Every player must belong to exactly one team.", nameof(teams));
    }
}
