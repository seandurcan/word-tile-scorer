using System.Text.Json;
using WordTileScorer.Core;

namespace WordTileScorer.App;

public sealed class GamePage : ContentPage
{
    private readonly GameState _game;
    private readonly GameEngine _engine = new();
    private readonly Label _turn = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly VerticalStackLayout _totals = new() { Spacing = 4 };
    private readonly VerticalStackLayout _words = new() { Spacing = 14 };
    private readonly Label _turnTotal = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly Button _record = new() { Text = "Record turn", BackgroundColor = Color.FromArgb("#0B6E4F"), TextColor = Colors.White, IsEnabled = false };

    public GamePage(GameState game)
    {
        _game = game;
        Title = "Score game";
        AddWord();

        var addWord = new Button { Text = "Add another word" };
        addWord.Clicked += (_, _) => AddWord();
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
                    _turn, _totals,
                    new BoxView { HeightRequest = 1, Color = Colors.Gray },
                    new Label { Text = "Words made by this play", FontSize = 20, FontAttributes = FontAttributes.Bold },
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
            if (_words.Children.Count <= 1) return;
            _words.Children.Remove(editor);
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
            var editors = Editors().ToArray();
            if (editors.Any(x => !x.IsConfirmedValid))
                throw new InvalidOperationException("Every word must be checked and confirmed valid before recording the turn.");
            _engine.RecordWords(_game, editors.Select(x => x.Score).ToArray());
            await AfterTurn();
        }
        catch (Exception ex) { await DisplayAlert("Cannot record turn", ex.Message, "OK"); }
    }

    private async Task AfterTurn()
    {
        await Save();
        _words.Children.Clear();
        AddWord();
        RefreshGame();
    }

    private void RefreshPendingTurn()
    {
        var editors = Editors().ToArray();
        _turnTotal.Text = $"Turn total: {editors.Sum(x => x.Score.Total)}";
        _record.IsEnabled = editors.Length > 0 && editors.All(x => x.IsConfirmedValid);
    }

    private void RefreshGame()
    {
        var team = _game.TeamFor(_game.CurrentPlayer.Id);
        _turn.Text = team is null ? $"Current turn: {_game.CurrentPlayer.Name}" : $"Current turn: {_game.CurrentPlayer.Name} — {team.Name}";
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
    private readonly Picker[] _wordPremiums = Enumerable.Range(1, 3).Select(number => new Picker
    {
        Title = $"Word premium {number}",
        ItemsSource = new[] { "No word premium", "Double word", "Triple word" },
        SelectedIndex = 0
    }).ToArray();
    private readonly Label _calculation = new() { FontSize = 17 };
    private readonly Label _validation = new() { Text = "Word not yet checked", TextColor = Colors.DarkOrange };
    private readonly HashSet<int> _selectedLetters = [];
    private bool _updating;
    private string? _confirmedWord;
    private int _number;

    public WordScoreBuilder Score { get; } = new();
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
        for (var slot = 0; slot < _wordPremiums.Length; slot++)
        {
            var premiumSlot = slot;
            _wordPremiums[slot].SelectedIndexChanged += (_, _) =>
            {
                if (_updating) return;
                Score.SetWordPremium(premiumSlot, _wordPremiums[premiumSlot].SelectedIndex + 1);
                Invalidate(); RefreshCalculation();
            };
        }

        var normalLetter = PremiumButton("Normal letter", 1);
        var doubleLetter = PremiumButton("Double letter", 2);
        var tripleLetter = PremiumButton("Triple letter", 3);

        var check = new Button { Text = "Check this word with Collins" };
        check.Clicked += CheckWord;
        var valid = new Button { Text = "Collins says VALID" };
        valid.Clicked += (_, _) =>
        {
            if (Score.Letters.Count == 0) return;
            _confirmedWord = Score.Word;
            _validation.Text = $"Confirmed valid: {_confirmedWord}";
            _validation.TextColor = Colors.Green;
            Changed?.Invoke(this, EventArgs.Empty);
        };
        var invalid = new Button { Text = "NOT VALID" };
        invalid.Clicked += (_, _) => { _word.Text = string.Empty; _word.Focus(); };
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
                new HorizontalStackLayout { Spacing = 6, Children = { normalLetter, doubleLetter, tripleLetter } },
                new Label { Text = "Word premiums (use another row only when the word covers another premium square)" },
                _wordPremiums[0], _wordPremiums[1], _wordPremiums[2], _calculation, _validation,
                check, new HorizontalStackLayout { Spacing = 8, Children = { valid, invalid } }, remove
            }
        };
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
        Invalidate(); RefreshCalculation();
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
            : $"Selected: {string.Join(", ", _selectedLetters.Order().Select(i => $"{Score.Letters[i].Letter}{i + 1}"))}";
        RefreshLetterButtons();
    }

    private void ApplyLetterMultiplier(int multiplier)
    {
        if (_selectedLetters.Count == 0) return;
        foreach (var index in _selectedLetters) Score.SetLetterMultiplier(index, multiplier);
        Invalidate(); RefreshLetterButtons(); RefreshCalculation();
    }

    private void RefreshLetterButtons()
    {
        for (var i = 0; i < _letterButtons.Children.Count; i++)
        {
            var button = (Button)_letterButtons.Children[i];
            var play = Score.Letters[i];
            var premium = play.LetterMultiplier switch { 2 => " DL", 3 => " TL", _ => string.Empty };
            button.Text = $"{play.Letter}\n{play.BaseValue}{premium}";
            button.BackgroundColor = _selectedLetters.Contains(i) ? Color.FromArgb("#F4E3B2") : Colors.Transparent;
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
        _validation.TextColor = Colors.DarkOrange;
    }

    private async void CheckWord(object? sender, EventArgs e)
    {
        if (Score.Letters.Count == 0) return;
        Invalidate();
        await Clipboard.Default.SetTextAsync(Score.Word);
        _validation.Text = $"{Score.Word} copied. Paste it into Collins, then return and select the result.";
        Changed?.Invoke(this, EventArgs.Empty);
        await Launcher.Default.OpenAsync(new Uri("https://scrabble.collinsdictionary.com/check/"));
    }
}
