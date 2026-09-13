namespace WordTileScorer.Core;

public sealed class WordScoreBuilder
{
    private readonly List<LetterPlay> _letters = [];
    private readonly int[] _wordPremiums = [1, 1, 1];

    public IReadOnlyList<LetterPlay> Letters => _letters;
    public IReadOnlyList<int> WordPremiums => _wordPremiums;
    public int WordMultiplier => _wordPremiums.Aggregate(1, checked((total, premium) => total * premium));
    public int Subtotal => _letters.Sum(x => x.Score);
    public int Total => checked(Subtotal * WordMultiplier);
    public string Word => new(_letters.Select(x => x.Letter).ToArray());

    public void SetWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) throw new ArgumentException("A word is required.", nameof(word));
        _letters.Clear();
        foreach (var letter in word.Trim().ToUpperInvariant())
            AddLetter(letter, EnglishTileValues.ValueOf(letter));
    }

    public void SetLetterMultiplier(int letterIndex, int multiplier)
    {
        if (letterIndex < 0 || letterIndex >= _letters.Count) throw new ArgumentOutOfRangeException(nameof(letterIndex));
        if (multiplier is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(multiplier));
        _letters[letterIndex] = _letters[letterIndex] with { LetterMultiplier = multiplier };
    }

    public PlayedWord Build() => new(Word, _letters.ToArray(), WordMultiplier, Total);

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
        _wordPremiums[0] = multiplier;
        _wordPremiums[1] = 1;
        _wordPremiums[2] = 1;
    }

    public void SetWordPremium(int slot, int multiplier)
    {
        if (slot is < 0 or >= 3) throw new ArgumentOutOfRangeException(nameof(slot));
        if (multiplier is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(multiplier));
        _wordPremiums[slot] = multiplier;
    }
}

public static class EnglishTileValues
{
    private static readonly IReadOnlyDictionary<char, int> Values = new Dictionary<char, int>
    {
        ['A'] = 1, ['B'] = 3, ['C'] = 3, ['D'] = 2, ['E'] = 1, ['F'] = 4,
        ['G'] = 2, ['H'] = 4, ['I'] = 1, ['J'] = 8, ['K'] = 5, ['L'] = 1,
        ['M'] = 3, ['N'] = 1, ['O'] = 1, ['P'] = 3, ['Q'] = 10, ['R'] = 1,
        ['S'] = 1, ['T'] = 1, ['U'] = 1, ['V'] = 4, ['W'] = 4, ['X'] = 8,
        ['Y'] = 4, ['Z'] = 10
    };

    public static int ValueOf(char letter)
    {
        var normalized = char.ToUpperInvariant(letter);
        return Values.TryGetValue(normalized, out var value)
            ? value
            : throw new ArgumentException($"'{letter}' is not an English letter.", nameof(letter));
    }
}

public static class EnglishTileDistribution
{
    public static readonly IReadOnlyDictionary<char, int> Counts = new Dictionary<char, int>
    {
        ['A'] = 9, ['B'] = 2, ['C'] = 2, ['D'] = 4, ['E'] = 12, ['F'] = 2,
        ['G'] = 3, ['H'] = 2, ['I'] = 9, ['J'] = 1, ['K'] = 1, ['L'] = 4,
        ['M'] = 2, ['N'] = 6, ['O'] = 8, ['P'] = 2, ['Q'] = 1, ['R'] = 6,
        ['S'] = 4, ['T'] = 6, ['U'] = 4, ['V'] = 2, ['W'] = 2, ['X'] = 1,
        ['Y'] = 2, ['Z'] = 1, ['?'] = 2
    };
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

    public Turn RecordWords(
        GameState game,
        IReadOnlyList<WordScoreBuilder> scores,
        IReadOnlyList<char>? placedTiles = null,
        bool allowExcessTiles = false,
        DateTimeOffset? playedAt = null)
    {
        EnsurePlayable(game);
        if (scores.Count == 0 || scores.Any(s => s.Letters.Count == 0))
            throw new InvalidOperationException("Enter every word or use Pass.");
        var tiles = (placedTiles ?? []).Select(NormalizeTile).ToArray();
        if (tiles.Length > 7 && !allowExcessTiles)
            throw new InvalidOperationException("A player can place no more than seven rack tiles in one turn.");
        ValidateWordsCanBeFormed(game, scores, tiles);
        var shortages = TileShortages(game, tiles);
        if (shortages.Count > 0 && !allowExcessTiles)
            throw new TileLimitException(shortages);
        var words = scores.Select(s => s.Build()).ToArray();
        var bingo = tiles.Length == 7 ? 50 : 0;
        return AddTurn(game, words, checked(words.Sum(w => w.Score) + bingo), false, playedAt, tiles, bingo);
    }

    public static IReadOnlyDictionary<char, int> UsedTiles(GameState game) => game.Turns
        .SelectMany(t => t.PlacedTiles ?? [])
        .GroupBy(NormalizeTile)
        .ToDictionary(group => group.Key, group => group.Count());

    public static IReadOnlyDictionary<char, int> TileShortages(GameState game, IEnumerable<char> proposedTiles)
    {
        var used = UsedTiles(game);
        return proposedTiles.Select(NormalizeTile)
            .GroupBy(tile => tile)
            .Select(group => new
            {
                Tile = group.Key,
                Excess = used.GetValueOrDefault(group.Key) + group.Count() - EnglishTileDistribution.Counts[group.Key]
            })
            .Where(item => item.Excess > 0)
            .ToDictionary(item => item.Tile, item => item.Excess);
    }

    public static void ValidateWordsCanBeFormed(
        GameState game,
        IReadOnlyList<WordScoreBuilder> words,
        IReadOnlyList<char> placedTiles)
    {
        if (words.Count == 0) return;
        var normalizedPlaced = placedTiles.Select(NormalizeTile).ToArray();
        var boardTiles = game.Turns.SelectMany(turn => turn.PlacedTiles ?? []).Select(NormalizeTile).ToArray();

        var mainRemainder = LetterCounts(words[0].Word);
        var placedBlanks = normalizedPlaced.Count(tile => tile == '?');
        foreach (var tile in normalizedPlaced.Where(tile => tile != '?'))
        {
            if (!Take(mainRemainder, tile))
                throw new InvalidOperationException($"The main word {words[0].Word} does not contain every tile entered as placed from the rack.");
        }
        while (placedBlanks-- > 0)
        {
            if (!TakeAny(mainRemainder))
                throw new InvalidOperationException($"The main word {words[0].Word} is shorter than the number of tiles entered as placed.");
        }
        EnsureRemainderExistsOnBoard(words[0].Word, mainRemainder, boardTiles);

        var availableForCrossings = boardTiles.Concat(normalizedPlaced).ToArray();
        foreach (var word in words.Skip(1))
            EnsureWordUsesAvailableTiles(word.Word, availableForCrossings);
    }

    private static void EnsureWordUsesAvailableTiles(string word, IReadOnlyList<char> availableTiles)
    {
        var needed = LetterCounts(word);
        var blanks = availableTiles.Count(tile => tile == '?');
        foreach (var tile in availableTiles.Where(tile => tile != '?')) Take(needed, tile);
        while (blanks-- > 0) TakeAny(needed);
        if (needed.Values.Sum() > 0)
            throw new InvalidOperationException($"The word {word} uses letters that are not among the current rack tiles or tiles already recorded on the board.");
    }

    private static void EnsureRemainderExistsOnBoard(string word, Dictionary<char, int> remainder, IReadOnlyList<char> boardTiles)
    {
        var blanks = boardTiles.Count(tile => tile == '?');
        foreach (var tile in boardTiles.Where(tile => tile != '?')) Take(remainder, tile);
        while (blanks-- > 0) TakeAny(remainder);
        if (remainder.Values.Sum() > 0)
            throw new InvalidOperationException($"The word {word} requires more letters than the placed rack tiles and previously recorded board tiles provide.");
    }

    private static Dictionary<char, int> LetterCounts(string word) => word
        .GroupBy(char.ToUpperInvariant)
        .ToDictionary(group => group.Key, group => group.Count());

    private static bool Take(Dictionary<char, int> counts, char tile)
    {
        if (!counts.TryGetValue(tile, out var count) || count == 0) return false;
        counts[tile] = count - 1;
        return true;
    }

    private static bool TakeAny(Dictionary<char, int> counts)
    {
        var entry = counts.FirstOrDefault(item => item.Value > 0);
        return entry.Value > 0 && Take(counts, entry.Key);
    }

    private static char NormalizeTile(char tile)
    {
        var normalized = char.ToUpperInvariant(tile);
        if (normalized != '?' && !EnglishTileDistribution.Counts.ContainsKey(normalized))
            throw new ArgumentException($"'{tile}' is not a valid English tile.", nameof(tile));
        return normalized;
    }

    public Turn Pass(GameState game, DateTimeOffset? playedAt = null)
    {
        EnsurePlayable(game);
        return AddTurn(game, [], 0, true, playedAt);
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
        IReadOnlyList<PlayedWord> words,
        int score,
        bool isPass,
        DateTimeOffset? playedAt,
        IReadOnlyList<char>? placedTiles = null,
        int bingoBonus = 0)
    {
        var player = game.CurrentPlayer;
        var turn = new Turn(
            Guid.NewGuid(), player.Id, game.TeamFor(player.Id)?.Id,
            playedAt ?? DateTimeOffset.Now, words, score, isPass)
        {
            PlacedTiles = placedTiles ?? [],
            BingoBonus = bingoBonus
        };
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

public sealed class TileLimitException(IReadOnlyDictionary<char, int> shortages)
    : InvalidOperationException("The proposed play exceeds the available tile distribution.")
{
    public IReadOnlyDictionary<char, int> Shortages { get; } = shortages;
}
