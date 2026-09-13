using System.Text.Json;
using WordTileScorer.Core;

namespace WordTileScorer.App;

public sealed class GamePage : ContentPage
{
    private readonly GameState _game;
    private readonly GameEngine _engine = new();
    private readonly WordScoreBuilder _score = new();
    private readonly Label _turn = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly Label _word = new() { FontSize = 26 };
    private readonly Label _calculation = new() { FontSize = 18 };
    private readonly Label _validation = new() { Text = "Word not yet checked", TextColor = Colors.DarkOrange };
    private readonly VerticalStackLayout _totals = new() { Spacing = 4 };
    private readonly Entry _letter = new() { Placeholder = "Letter", MaxLength = 1, WidthRequest = 80 };
    private readonly Entry _value = new() { Placeholder = "Value", Keyboard = Keyboard.Numeric, WidthRequest = 90 };
    private readonly Picker _letterMultiplier = new() { Title = "Letter multiplier", ItemsSource = new[] { "Normal", "Double letter", "Triple letter" }, SelectedIndex = 0 };
    private readonly Picker _wordMultiplier = new() { Title = "Word multiplier", ItemsSource = new[] { "Normal", "Double word", "Triple word" }, SelectedIndex = 0 };
    private readonly Button _record = new() { Text = "Record word", BackgroundColor = Color.FromArgb("#0B6E4F"), TextColor = Colors.White, IsEnabled = false };
    private string? _confirmedWord;

    public GamePage(GameState game)
    {
        _game = game;
        Title = "Score game";
        var addLetter = new Button { Text = "Add letter" };
        addLetter.Clicked += AddLetter;
        var removeLetter = new Button { Text = "Remove last letter" };
        removeLetter.Clicked += (_, _) => { _score.RemoveLastLetter(); InvalidateWordCheck(); RefreshScore(); };
        var checkWord = new Button { Text = "Check word with Collins" };
        checkWord.Clicked += CheckWord;
        var valid = new Button { Text = "Collins says VALID" };
        valid.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_score.Word))
            {
                await DisplayAlert("No word", "Enter the word before confirming it.", "OK");
                return;
            }
            _confirmedWord = _score.Word;
            _validation.Text = $"Confirmed valid: {_confirmedWord}";
            _validation.TextColor = Colors.Green;
            _record.IsEnabled = true;
        };
        var invalid = new Button { Text = "Collins says NOT VALID" };
        invalid.Clicked += (_, _) =>
        {
            ResetScore();
            _letter.Focus();
        };
        _record.Clicked += RecordWord;
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

        _wordMultiplier.SelectedIndexChanged += (_, _) =>
        {
            _score.SetWordMultiplier(_wordMultiplier.SelectedIndex + 1); InvalidateWordCheck(); RefreshScore();
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
                    new Label { Text = "Current word", FontAttributes = FontAttributes.Bold }, _word, _calculation, _validation,
                    new HorizontalStackLayout { Spacing = 8, Children = { _letter, _value } },
                    _letterMultiplier, addLetter, removeLetter, _wordMultiplier,
                    checkWord,
                    new HorizontalStackLayout { Spacing = 8, Children = { valid, invalid } },
                    _record, pass, undo, finish
                }
            }
        };
        RefreshGame();
    }

    private async void AddLetter(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_letter.Text) || !int.TryParse(_value.Text, out var value))
                throw new InvalidOperationException("Enter one letter and its tile value.");
            _score.AddLetter(_letter.Text[0], value, _letterMultiplier.SelectedIndex + 1);
            InvalidateWordCheck();
            _letter.Text = string.Empty; _value.Text = string.Empty; _letterMultiplier.SelectedIndex = 0;
            _letter.Focus(); RefreshScore();
        }
        catch (Exception ex) { await DisplayAlert("Cannot add letter", ex.Message, "OK"); }
    }

    private async void RecordWord(object? sender, EventArgs e)
    {
        try
        {
            if (!string.Equals(_confirmedWord, _score.Word, StringComparison.Ordinal))
                throw new InvalidOperationException("Check this word with Collins and confirm that it is valid before recording its score.");
            _engine.RecordWord(_game, _score); await AfterTurn();
        }
        catch (Exception ex) { await DisplayAlert("Cannot record word", ex.Message, "OK"); }
    }

    private async void CheckWord(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_score.Word))
        {
            await DisplayAlert("No word", "Enter the word before checking it.", "OK");
            return;
        }
        InvalidateWordCheck();
        await Launcher.Default.OpenAsync(new Uri("https://scrabble.collinsdictionary.com/check/"));
        _validation.Text = $"Check {_score.Word} in Collins, then return and select its result.";
    }

    private async Task AfterTurn()
    {
        await Save();
        ResetScore();
        RefreshGame();
    }

    private void ResetScore()
    {
        while (_score.Letters.Count > 0) _score.RemoveLastLetter();
        _score.SetWordMultiplier(1); _wordMultiplier.SelectedIndex = 0;
        InvalidateWordCheck();
        RefreshScore();
    }

    private void InvalidateWordCheck()
    {
        _confirmedWord = null;
        _record.IsEnabled = false;
        _validation.Text = "Word not yet checked";
        _validation.TextColor = Colors.DarkOrange;
    }

    private void RefreshScore()
    {
        _word.Text = string.IsNullOrEmpty(_score.Word) ? "—" : _score.Word;
        _calculation.Text = $"Letters: {_score.Subtotal}  ×  Word: {_score.WordMultiplier}  =  {_score.Total}";
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
        RefreshScore();
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
            var winners = ranked.Where(x => x.Score == ranked[0].Score).Select(x => x.Name);
            return $"Winner: {string.Join(" and ", winners)} — {ranked[0].Score} points";
        }
        var players = _game.Players.Select(p => (p.Name, Score: _game.PlayerTotal(p.Id))).OrderByDescending(x => x.Score).ToArray();
        var playerWinners = players.Where(x => x.Score == players[0].Score).Select(x => x.Name);
        return $"Winner: {string.Join(" and ", playerWinners)} — {players[0].Score} points";
    }
}
