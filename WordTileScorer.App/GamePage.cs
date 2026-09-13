using System.Text.Json;
using WordTileScorer.Core;

namespace WordTileScorer.App;

public sealed class GamePage : ContentPage
{
    private readonly GameState _game;
    private readonly GameEngine _engine = new();
    private readonly Label _turn = new() { FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
    private readonly Label _turnDetail = new() { FontSize = 14, TextColor = Colors.White };
    private readonly Border _activePlayerCard;
    private readonly VerticalStackLayout _totals = new() { Spacing = 4 };
    private readonly VerticalStackLayout _words = new() { Spacing = 14 };
    private readonly Entry _placedTiles = new() { Placeholder = "All tiles currently on rack, e.g. CATERS?", CharacterSpacing = 2 };
    private readonly Label _tileStatus = new() { TextColor = AppPalette.Slate };
    private readonly Label _turnTotal = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly Button _record = new() { Text = "Record turn", BackgroundColor = AppPalette.Blue, TextColor = Colors.White };

    public GamePage(GameState game)
    {
        _game = game;
        Title = "Score game";
        _activePlayerCard = new Border
        {
            BackgroundColor = AppPalette.Navy,
            Stroke = AppPalette.Blue,
            StrokeThickness = 2,
            Padding = 16,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label { Text = "ACTIVE PLAYER", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#F0E442"), CharacterSpacing = 2 },
                    _turn, _turnDetail
                }
            }
        };
        AddWord();

        var addWord = new Button { Text = "Add another word" };
        addWord.Clicked += (_, _) => AddWord();
        _placedTiles.TextChanged += PlacedTilesChanged;
        var tileUsage = new Button { Text = "View tile usage" };
        tileUsage.Clicked += async (_, _) => await DisplayAlert("Tiles used", TileUsageText(), "OK");
        _record.Clicked += RecordTurn;
        var pass = new Button { Text = "Pass (0)" };
        pass.Clicked += async (_, _) => { _engine.Pass(_game); await AfterTurn(); };
        var undo = new Button { Text = "Undo last turn" };
        undo.Clicked += async (_, _) =>
        {
            try { _engine.UndoLastTurn(_game); await Save(); RefreshGame(); }
            catch (Exception ex) { await DisplayAlert("Cannot undo", ex.Message, "OK"); }
        };
        var finish = new Button { Text = "Finish game" };
        finish.Clicked += async (_, _) =>
        {
            if (!await DisplayAlert("Finish game", "Finish this game? No more turns can be recorded.", "Finish", "Cancel")) return;
            _game.IsFinished = true;
            await Save();
            await DisplayAlert("Game finished", WinnerText(), "OK");
            await Navigation.PopAsync();
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20, Spacing = 12,
                Children =
                {
                    _activePlayerCard, _totals,
                    new BoxView { HeightRequest = 1, Color = Colors.Gray },
                    new Label { Text = "Words made by this play", FontSize = 20, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Current rack", FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Enter all available rack tiles in any order. The app determines which ones the word uses." },
                    _placedTiles, _tileStatus, tileUsage,
                    new Label { Text = "Enter a complete word. Select one or more letters, apply one letter premium to them, then choose any word premiums covered." },
                    _words, addWord, _turnTotal, _record, pass, undo, finish
                }
            }
        };
        RefreshGame();
    }

    private void AddWord()
    {
        var editor = new WordEntryView(_words.Children.Count + 1);
        editor.Changed += (_, _) => RefreshPendingTurn();
        editor.RemoveRequested += (_, _) =>
        {
            _words.Children.Remove(editor);
            if (_words.Children.Count == 0) AddWord();
            RenumberWords();
            RefreshPendingTurn();
        };
        _words.Children.Add(editor);
        RefreshPendingTurn();
    }

    private void RenumberWords()
    {
        var number = 1;
        foreach (var editor in Editors()) editor.Number = number++;
    }

    private IEnumerable<WordEntryView> Editors() => _words.Children.Cast<WordEntryView>();

    private async void RecordTurn(object? sender, EventArgs e)
    {
        try
        {
            var editors = Editors().Where(x => x.HasWord).ToArray();
            if (editors.Length == 0)
                throw new InvalidOperationException("Enter at least one word, or use Pass (0).");
            if (editors.Any(x => !x.IsConfirmedValid))
                throw new InvalidOperationException("Check every entered word with Collins and select Accept word before recording the turn.");
            var playerName = _game.CurrentPlayer.Name;
            var tiles = ParsedPlacedTiles();
            if (tiles.Length == 0)
                throw new InvalidOperationException("Enter the tiles currently available on the rack.");
            var shortages = GameEngine.TileShortages(_game, tiles);
            var overRack = tiles.Length > 7;
            var allowExcess = false;
            if (shortages.Count > 0 || overRack)
            {
                var details = new List<string>();
                if (overRack) details.Add($"The rack contains {tiles.Length} tiles; the maximum is 7.");
                details.AddRange(shortages.Select(x => $"{TileName(x.Key)} exceeds the set by {x.Value}."));
                allowExcess = await DisplayAlert("Tile limit exceeded", string.Join("\n", details) + "\n\nAllow this play anyway?", "Allow", "Cancel");
                if (!allowExcess) return;
            }
            var turn = _engine.RecordWords(_game, editors.Select(x => x.Score).ToArray(), tiles, allowExcess);
            await AfterTurn();
            var bonus = turn.BingoBonus > 0 ? $" (includes {turn.BingoBonus}-point seven-tile bonus)" : string.Empty;
            await DisplayAlert("Turn recorded", $"{playerName}: {turn.Score} points{bonus}.\nNext turn: {_game.CurrentPlayer.Name}.", "OK");
        }
        catch (Exception ex) { await DisplayAlert("Cannot record turn", ex.Message, "OK"); }
    }

    private async Task AfterTurn()
    {
        await Save();
        _placedTiles.Text = string.Empty;
        _words.Children.Clear();
        AddWord();
        RefreshGame();
    }

    private void RefreshPendingTurn()
    {
        var editors = Editors().Where(x => x.HasWord).ToArray();
        var tileCount = ParsedPlacedTiles().Length;
        var bonus = tileCount == 7 ? 50 : 0;
        _turnTotal.Text = $"Turn total: {editors.Sum(x => x.Score.Total) + bonus}" + (bonus > 0 ? " (includes 50-point bonus)" : string.Empty);
    }

    private void PlacedTilesChanged(object? sender, TextChangedEventArgs e)
    {
        var normalized = new string((e.NewTextValue ?? string.Empty)
            .Where(c => c == '?' || c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            .Select(char.ToUpperInvariant).ToArray());
        if (_placedTiles.Text != normalized) { _placedTiles.Text = normalized; return; }
        var shortages = GameEngine.TileShortages(_game, normalized);
        _tileStatus.Text = shortages.Count == 0
            ? $"Rack tiles available: {normalized.Length}/7 (order does not matter)"
            : $"CHECK REQUIRED: {string.Join(", ", shortages.Select(x => $"{TileName(x.Key)} over by {x.Value}"))}";
        _tileStatus.TextColor = shortages.Count == 0 ? AppPalette.Slate : AppPalette.Vermillion;
        RefreshPendingTurn();
    }

    private char[] ParsedPlacedTiles() => (_placedTiles.Text ?? string.Empty).ToCharArray();

    private string TileUsageText()
    {
        var used = GameEngine.UsedTiles(_game);
        return string.Join("\n", EnglishTileDistribution.Counts.Select(x =>
            $"{TileName(x.Key)}: {used.GetValueOrDefault(x.Key)} used / {x.Value} available"));
    }

    private static string TileName(char tile) => tile == '?' ? "Blank" : tile.ToString();

    private void RefreshGame()
    {
        var team = _game.TeamFor(_game.CurrentPlayer.Id);
        _turn.Text = _game.CurrentPlayer.Name;
        _turnDetail.Text = team is null
            ? $"Individual play · Turn {_game.Turns.Count + 1}"
            : $"{team.Name} · Turn {_game.Turns.Count + 1}";
        _totals.Children.Clear();
        if (_game.Mode == GameMode.Teams)
            foreach (var item in _game.Teams) _totals.Children.Add(new Label { Text = $"{item.Name}: {_game.TeamTotal(item.Id)}" });
        else
            foreach (var player in _game.Players) _totals.Children.Add(new Label { Text = $"{player.Name}: {_game.PlayerTotal(player.Id)}" });
        RefreshPendingTurn();
    }

    private async Task Save()
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "active-game.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_game, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string WinnerText()
    {
        if (_game.Mode == GameMode.Teams)
        {
            var ranked = _game.Teams.Select(t => (t.Name, Score: _game.TeamTotal(t.Id))).OrderByDescending(x => x.Score).ToArray();
            return $"Winner: {string.Join(" and ", ranked.Where(x => x.Score == ranked[0].Score).Select(x => x.Name))} — {ranked[0].Score} points";
        }
        var players = _game.Players.Select(p => (p.Name, Score: _game.PlayerTotal(p.Id))).OrderByDescending(x => x.Score).ToArray();
        return $"Winner: {string.Join(" and ", players.Where(x => x.Score == players[0].Score).Select(x => x.Name))} — {players[0].Score} points";
    }
}

public sealed class WordEntryView : Border
{
    private readonly Label _heading = new() { FontSize = 18, FontAttributes = FontAttributes.Bold };
    private readonly Entry _word = new() { Placeholder = "Enter complete word", CharacterSpacing = 2 };
    private readonly HorizontalStackLayout _letterButtons = new() { Spacing = 6 };
    private readonly Label _selected = new() { Text = "Select one or more letters above" };
    private readonly VerticalStackLayout _letterPremiumList = new() { Spacing = 3 };
    private readonly Button _doubleWord = new() { Text = "Double Word (0)", BackgroundColor = AppPalette.Blue, TextColor = Colors.White };
    private readonly Button _trebleWord = new() { Text = "Treble Word (0)", BackgroundColor = AppPalette.Amber, TextColor = AppPalette.Navy };
    private readonly Label _wordPremiumStatus = new() { Text = "No word premium applied", TextColor = AppPalette.Slate };
    private int _doubleWordCount;
    private int _trebleWordCount;
    private readonly Label _calculation = new() { FontSize = 17 };
    private readonly Label _validation = new() { Text = "Word not yet checked", TextColor = AppPalette.Amber };
    private readonly HashSet<int> _selectedLetters = [];
    private string? _confirmedWord;
    private int _number;

    public WordScoreBuilder Score { get; } = new();
    public bool HasWord => Score.Letters.Count > 0;
    public bool IsConfirmedValid => string.Equals(_confirmedWord, Score.Word, StringComparison.Ordinal);
    public event EventHandler? Changed;
    public event EventHandler? RemoveRequested;

    public int Number { get => _number; set { _number = value; _heading.Text = $"Word {_number}"; } }

    public WordEntryView(int number)
    {
        Number = number;
        Stroke = Colors.LightGray;
        StrokeThickness = 1;
        Padding = 12;
        _word.TextChanged += WordChanged;
        _doubleWord.Clicked += (_, _) => AddWordPremium(2);
        _trebleWord.Clicked += (_, _) => AddWordPremium(3);
        var clearWordPremiums = new Button { Text = "Clear word premiums" };
        clearWordPremiums.Clicked += (_, _) => ClearWordPremiums();

        var doubleLetter = PremiumButton("Double letter", 2);
        var tripleLetter = PremiumButton("Triple letter", 3);

        var check = new Button { Text = "Check this word with Collins" };
        check.Clicked += CheckWord;
        var remove = new Button { Text = "Remove this word" };
        remove.Clicked += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                _heading, _word,
                new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _letterButtons },
                _selected,
                _letterPremiumList,
                new HorizontalStackLayout { Spacing = 6, Children = { doubleLetter, tripleLetter } },
                _calculation, _validation, check, remove,
                new Label { Text = "Word premiums", FontAttributes = FontAttributes.Bold },
                _wordPremiumStatus,
                new HorizontalStackLayout
                {
                    Spacing = 6, HorizontalOptions = LayoutOptions.Center,
                    Children = { _doubleWord, _trebleWord }
                },
                clearWordPremiums
            }
        };
        RefreshCalculation();
    }

    private void AddWordPremium(int multiplier)
    {
        var individualCount = multiplier == 2 ? _doubleWordCount : _trebleWordCount;
        if (individualCount >= 3)
        {
            _wordPremiumStatus.Text = $"CHECK: {(multiplier == 2 ? "Double" : "Treble")} Word is already at its maximum of 3.";
            _wordPremiumStatus.TextColor = AppPalette.Vermillion;
            return;
        }
        if (_doubleWordCount + _trebleWordCount >= 3)
        {
            _wordPremiumStatus.Text = "CHECK: a word cannot cover more than 3 word-premium squares on the standard board.";
            _wordPremiumStatus.TextColor = AppPalette.Vermillion;
            return;
        }
        if (multiplier == 2) _doubleWordCount++; else _trebleWordCount++;
        ApplyWordPremiumCounts();
    }

    private void ClearWordPremiums()
    {
        _doubleWordCount = 0;
        _trebleWordCount = 0;
        ApplyWordPremiumCounts();
    }

    private void ApplyWordPremiumCounts()
    {
        var slot = 0;
        for (var i = 0; i < _doubleWordCount; i++) Score.SetWordPremium(slot++, 2);
        for (var i = 0; i < _trebleWordCount; i++) Score.SetWordPremium(slot++, 3);
        while (slot < 3) Score.SetWordPremium(slot++, 1);
        _doubleWord.Text = $"Double Word ({_doubleWordCount})";
        _trebleWord.Text = $"Treble Word ({_trebleWordCount})";
        _wordPremiumStatus.Text = _doubleWordCount + _trebleWordCount == 0
            ? "No word premium applied"
            : $"Applied: Double Word ×{_doubleWordCount}; Treble Word ×{_trebleWordCount}";
        _wordPremiumStatus.TextColor = AppPalette.Slate;
        RefreshCalculation();
    }

    private Button PremiumButton(string text, int multiplier)
    {
        var button = new Button { Text = text, FontSize = 13 };
        button.Clicked += (_, _) => ApplyLetterMultiplier(multiplier);
        return button;
    }

    private void WordChanged(object? sender, TextChangedEventArgs e)
    {
        var normalized = new string((e.NewTextValue ?? string.Empty).Where(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z').ToArray()).ToUpperInvariant();
        if (_word.Text != normalized) { _word.Text = normalized; return; }
        _selectedLetters.Clear();
        _letterButtons.Children.Clear();
        if (normalized.Length > 0)
        {
            Score.SetWord(normalized);
            for (var i = 0; i < Score.Letters.Count; i++) AddLetterButton(i);
        }
        else while (Score.Letters.Count > 0) Score.RemoveLastLetter();
        Invalidate(); RefreshLetterSelectionList(); RefreshCalculation();
    }

    private void AddLetterButton(int index)
    {
        var play = Score.Letters[index];
        var button = new Button { Text = $"{play.Letter}\n{play.BaseValue}", WidthRequest = 54, CommandParameter = index };
        button.Clicked += (_, _) => ToggleLetter((int)button.CommandParameter);
        _letterButtons.Children.Add(button);
    }

    private void ToggleLetter(int index)
    {
        if (!_selectedLetters.Add(index)) _selectedLetters.Remove(index);
        _selected.Text = _selectedLetters.Count == 0
            ? "Select one or more letters above"
            : "Selected tiles — choose Double Letter or Triple Letter:";
        RefreshLetterButtons(); RefreshLetterSelectionList();
    }

    private void ApplyLetterMultiplier(int multiplier)
    {
        if (_selectedLetters.Count == 0)
        {
            _selected.Text = "Select at least one tile before applying a letter premium.";
            _selected.TextColor = AppPalette.Vermillion;
            return;
        }
        foreach (var index in _selectedLetters) Score.SetLetterMultiplier(index, multiplier);
        _selectedLetters.Clear();
        _selected.Text = "Select one or more letters above";
        _selected.TextColor = AppPalette.Slate;
        RefreshLetterButtons(); RefreshLetterSelectionList(); RefreshCalculation();
    }

    private void RefreshLetterSelectionList()
    {
        _letterPremiumList.Children.Clear();
        for (var i = 0; i < Score.Letters.Count; i++)
        {
            var letter = Score.Letters[i];
            var status = letter.LetterMultiplier switch
            {
                2 => "Double Letter",
                3 => "Triple Letter",
                _ when _selectedLetters.Contains(i) => "Selected — choose a premium",
                _ => null
            };
            if (status is not null)
                _letterPremiumList.Children.Add(new Label { Text = $"Tile {i + 1}: {letter.Letter} — {status}" });
        }
    }

    private void RefreshLetterButtons()
    {
        for (var i = 0; i < _letterButtons.Children.Count; i++)
        {
            var button = (Button)_letterButtons.Children[i];
            var play = Score.Letters[i];
            var premium = play.LetterMultiplier switch { 2 => " DL", 3 => " TL", _ => string.Empty };
            button.Text = $"{play.Letter}\n{play.BaseValue}{premium}";
            button.BackgroundColor = _selectedLetters.Contains(i) ? Color.FromArgb("#F0E442") : AppPalette.Ivory;
            button.TextColor = AppPalette.Navy;
        }
    }

    private void RefreshCalculation()
    {
        var premiums = Score.WordPremiums.Where(x => x > 1).Select(x => $"×{x}").ToArray();
        var wordPart = premiums.Length == 0 ? "no word premium" : string.Join(" ", premiums);
        _calculation.Text = $"Letter total: {Score.Subtotal}; word: {wordPart}; total = {Score.Total}";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Invalidate()
    {
        _confirmedWord = null;
        _validation.Text = "Word not yet checked";
        _validation.TextColor = AppPalette.Amber;
    }

    private async void CheckWord(object? sender, EventArgs e)
    {
        if (Score.Letters.Count == 0)
        {
            _validation.Text = "Enter a word before checking Collins.";
            _validation.TextColor = AppPalette.Vermillion;
            return;
        }
        Invalidate();
        await Clipboard.Default.SetTextAsync(Score.Word);
        _validation.Text = $"Checking {Score.Word} with Collins…";
        Changed?.Invoke(this, EventArgs.Empty);
        await Navigation.PushModalAsync(new CollinsCheckPage(Score.Word, accepted =>
        {
            if (accepted)
            {
                _confirmedWord = Score.Word;
                _validation.Text = $"Confirmed valid: {_confirmedWord}";
                _validation.TextColor = AppPalette.Blue;
            }
            else
            {
                _confirmedWord = null;
                _validation.Text = $"Rejected: {Score.Word}";
                _validation.TextColor = AppPalette.Vermillion;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }));
    }
}

public sealed class CollinsCheckPage : ContentPage
{
    private bool _completed;

    public CollinsCheckPage(string word, Action<bool> completed)
    {
        Title = "Check with Collins";
        var heading = new Label
        {
            Text = $"Check: {word}\nThe word is copied—paste it into the Collins search box.",
            FontSize = 18,
            Padding = new Thickness(12, 8)
        };
        var web = new WebView { Source = "https://scrabble.collinsdictionary.com/check/" };
        var accept = new Button { Text = "Accept word", BackgroundColor = AppPalette.Blue, TextColor = Colors.White };
        var reject = new Button { Text = "Reject word", BackgroundColor = AppPalette.Vermillion, TextColor = Colors.White };

        async Task Finish(bool accepted)
        {
            if (_completed) return;
            _completed = true;
            completed(accepted);
            await Navigation.PopModalAsync();
        }

        accept.Clicked += async (_, _) => await Finish(true);
        reject.Clicked += async (_, _) => await Finish(false);

        var actions = new HorizontalStackLayout
        {
            Padding = 12, Spacing = 8, HorizontalOptions = LayoutOptions.Center,
            Children = { accept, reject }
        };
        Grid.SetRow(web, 1);
        Grid.SetRow(actions, 2);
        Content = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
            Children = { heading, web, actions }
        };
    }
}
