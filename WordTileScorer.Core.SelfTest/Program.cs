using WordTileScorer.Core;

var tests = new (string Name, Action Run)[]
{
    ("letter and word multipliers", ScoreMultipliers),
    ("eight-player alternating order", EightPlayerOrder),
    ("undo restores current player", UndoRestoresPlayer),
    ("team totals include both partners", TeamTotals),
    ("built-in English tile values", BuiltInTileValues),
    ("multiple words form one turn total", MultipleWordTotal),
    ("letter premium is replaced, not stacked", LetterPremiumIsExclusive),
    ("multiple word premiums compound", MultipleWordPremiums),
    ("English set contains 100 tiles", EnglishSetContains100Tiles),
    ("crossing tile is consumed once", CrossingTileConsumedOnce),
    ("tile shortages are detected", TileShortagesDetected),
    ("seven placed tiles earn bingo bonus", SevenTileBonus),
    ("opening word cannot exceed placed tiles", OpeningWordCannotExceedPlacedTiles),
    ("existing board tiles can extend a word", ExistingBoardTilesCanExtendWord),
    ("rack order does not matter and unused tiles remain unused", RackOrderAndUnusedTiles),
    ("unused rack tiles remain for the player's next turn", UnusedRackTilesRemain),
    ("seven tiles on rack do not earn a bingo", FullRackIsNotBingo),
    ("rejected word scores zero and advances turn", RejectedWordScoresZero),
    ("unavailable rack tile is refused", UnavailableRackTileIsRefused)
};

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Environment.ExitCode = failed == 0 ? 0 : 1;

static void ScoreMultipliers()
{
    var score = new WordScoreBuilder();
    score.AddLetter('C', 3, 2);
    score.AddLetter('A', 1);
    score.AddLetter('T', 1);
    score.SetWordMultiplier(2);
    Equal(16, score.Total);
}

static void EightPlayerOrder()
{
    var players = Enumerable.Range(1, 8).Select(i => Player.Create($"P{i}")).ToArray();
    var teams = Enumerable.Range(0, 4)
        .Select(i => Team.Create($"Team {i + 1}", [players[i].Id, players[i + 4].Id]))
        .ToArray();
    var game = new GameEngine().CreateGame(players, GameMode.Teams, teams);
    var seen = new List<string>();
    var engine = new GameEngine();
    for (var i = 0; i < 8; i++) { seen.Add(game.CurrentPlayer.Name); engine.Pass(game); }
    Equal("P1,P2,P3,P4,P5,P6,P7,P8", string.Join(',', seen));
    Equal("P1", game.CurrentPlayer.Name);
}

static void UndoRestoresPlayer()
{
    var players = new[] { Player.Create("A"), Player.Create("B") };
    var engine = new GameEngine();
    var game = engine.CreateGame(players, GameMode.Individual);
    engine.Pass(game);
    Equal("B", game.CurrentPlayer.Name);
    engine.UndoLastTurn(game);
    Equal("A", game.CurrentPlayer.Name);
}

static void TeamTotals()
{
    var players = new[] { Player.Create("A1"), Player.Create("B1"), Player.Create("A2"), Player.Create("B2") };
    var teamA = Team.Create("A", [players[0].Id, players[2].Id]);
    var teamB = Team.Create("B", [players[1].Id, players[3].Id]);
    var engine = new GameEngine();
    var game = engine.CreateGame(players, GameMode.Teams, [teamA, teamB]);
    foreach (var (letter, value) in new[] { ('A', 3), ('B', 5), ('C', 7), ('D', 11) })
    {
        var score = new WordScoreBuilder();
        score.AddLetter(letter, value);
        engine.RecordWords(game, [score], [letter]);
    }
    Equal(10, game.TeamTotal(teamA.Id));
    Equal(16, game.TeamTotal(teamB.Id));
}

static void BuiltInTileValues()
{
    var score = new WordScoreBuilder();
    score.SetWord("QUIZ");
    Equal(22, score.Total);
    score.SetLetterMultiplier(3, 3);
    Equal(42, score.Total);
    score.SetWordMultiplier(2);
    Equal(84, score.Total);
}

static void MultipleWordTotal()
{
    var players = new[] { Player.Create("A"), Player.Create("B") };
    var game = new GameEngine().CreateGame(players, GameMode.Individual);
    var first = new WordScoreBuilder(); first.SetWord("CAT"); first.SetLetterMultiplier(0, 2); first.SetWordMultiplier(2);
    var second = new WordScoreBuilder(); second.SetWord("AT"); second.SetLetterMultiplier(0, 2);
    var turn = new GameEngine().RecordWords(game, [first, second], "CAT".ToCharArray());
    Equal(19, turn.Score); // CAT: ((3×2)+1+1)×2=16; AT: (1×2)+1=3
}

static void LetterPremiumIsExclusive()
{
    var score = new WordScoreBuilder(); score.SetWord("BOX");
    score.SetLetterMultiplier(0, 2);
    score.SetLetterMultiplier(0, 3);
    Equal(18, score.Total); // B is triple, not double then triple.
}

static void MultipleWordPremiums()
{
    var score = new WordScoreBuilder(); score.SetWord("CAT");
    score.SetLetterMultiplier(0, 2);
    score.SetWordPremium(0, 2);
    score.SetWordPremium(1, 3);
    Equal(48, score.Total); // ((3×2)+1+1)×2×3
}

static void EnglishSetContains100Tiles() => Equal(100, EnglishTileDistribution.Counts.Values.Sum());

static void CrossingTileConsumedOnce()
{
    var players = new[] { Player.Create("A"), Player.Create("B") };
    var game = new GameEngine().CreateGame(players, GameMode.Individual);
    var main = new WordScoreBuilder(); main.SetWord("CAT");
    var crossing = new WordScoreBuilder(); crossing.SetWord("AT");
    new GameEngine().RecordWords(game, [main, crossing], ['C', 'A', 'T']);
    Equal(3, GameEngine.UsedTiles(game).Values.Sum());
}

static void TileShortagesDetected()
{
    var game = CreateGameForTileLimit();
    var word = new WordScoreBuilder(); word.SetWord("BB");
    new GameEngine().RecordWords(game, [word], ['B', 'B']);
    var shortages = GameEngine.TileShortages(game, ['B']);
    Equal(1, shortages['B']);
}

static GameState CreateGameForTileLimit()
{
    var players = new[] { Player.Create("A"), Player.Create("B") };
    return new GameEngine().CreateGame(players, GameMode.Individual);
}

static void SevenTileBonus()
{
    var game = CreateGameForTileLimit();
    var word = new WordScoreBuilder(); word.SetWord("READING");
    var turn = new GameEngine().RecordWords(game, [word], "READING".ToCharArray());
    Equal(word.Total + 50, turn.Score);
    Equal(50, turn.BingoBonus);
}

static void OpeningWordCannotExceedPlacedTiles()
{
    var game = CreateGameForTileLimit();
    var word = new WordScoreBuilder(); word.SetWord("REACTION");
    Throws<InvalidOperationException>(() =>
        new GameEngine().RecordWords(game, [word], "REACTION"[..7].ToCharArray()));
}

static void ExistingBoardTilesCanExtendWord()
{
    var game = CreateGameForTileLimit();
    var first = new WordScoreBuilder(); first.SetWord("CAT");
    new GameEngine().RecordWords(game, [first], "CAT".ToCharArray());
    var extension = new WordScoreBuilder(); extension.SetWord("CATER");
    new GameEngine().RecordWords(game, [extension], "ER".ToCharArray());
    Equal(5, GameEngine.UsedTiles(game).Values.Sum());
}

static void RackOrderAndUnusedTiles()
{
    var game = CreateGameForTileLimit();
    var word = new WordScoreBuilder(); word.SetWord("CAT");
    new GameEngine().RecordWords(game, [word], "ZXTCAYQ".ToCharArray());
    var used = GameEngine.UsedTiles(game);
    Equal(3, used.Values.Sum());
    Equal(1, used['C']);
    Equal(1, used['A']);
    Equal(1, used['T']);
}

static void UnusedRackTilesRemain()
{
    var players = new[] { Player.Create("A"), Player.Create("B") };
    var engine = new GameEngine();
    var game = engine.CreateGame(players, GameMode.Individual);
    var first = new WordScoreBuilder(); first.SetWord("CAT");
    engine.RecordWords(game, [first], "ZXTCAYQ".ToCharArray());
    engine.Pass(game);
    Equal("ZXYQ", new string(GameEngine.CurrentRack(game).ToArray()));
}

static void FullRackIsNotBingo()
{
    var game = CreateGameForTileLimit();
    var word = new WordScoreBuilder(); word.SetWord("CAT");
    var turn = new GameEngine().RecordWords(game, [word], "ZXTCAYQ".ToCharArray());
    Equal(0, turn.BingoBonus);
}

static void RejectedWordScoresZero()
{
    var players = new[] { Player.Create("A"), Player.Create("B") };
    var engine = new GameEngine();
    var game = engine.CreateGame(players, GameMode.Individual);
    var turn = engine.RejectWord(game, "ZZZ", "CATERS?".ToCharArray());
    Equal(0, turn.Score);
    Equal(false, turn.IsPass);
    Equal("ZZZ", turn.RejectedWord);
    Equal("B", game.CurrentPlayer.Name);
    engine.Pass(game);
    Equal("CATERS?", new string(GameEngine.CurrentRack(game).ToArray()));
}

static void UnavailableRackTileIsRefused()
{
    var game = CreateGameForTileLimit();
    var word = new WordScoreBuilder(); word.SetWord("BB");
    new GameEngine().RecordWords(game, [word], "BB".ToCharArray());
    Equal(false, GameEngine.CanAddToCurrentRack(game, [], 'B', out var reason));
    Equal("No B tiles remain available.", reason);
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new Exception($"Expected {typeof(TException).Name}.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; received {actual}.");
}
