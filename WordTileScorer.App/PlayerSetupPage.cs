using WordTileScorer.Core;

namespace WordTileScorer.App;

public sealed class PlayerSetupPage : ContentPage
{
    private readonly VerticalStackLayout _players = new() { Spacing = 8 };
    private readonly Switch _teamMode = new();

    public PlayerSetupPage()
    {
        Title = "New game";
        for (var i = 1; i <= 4; i++) AddPlayerRow($"Player {i}");

        var add = new Button { Text = "Add player (maximum 8)" };
        add.Clicked += async (_, _) =>
        {
            if (_players.Children.Count < 8) AddPlayerRow(string.Empty);
            else await DisplayAlert("Maximum players", "A game can contain no more than eight players.", "OK");
        };
        var start = new Button { Text = "Start game", BackgroundColor = AppPalette.Blue, TextColor = Colors.White };
        start.Clicked += StartClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20, Spacing = 14,
                Children =
                {
                    new Label { Text = "Players and playing order", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Enter 2–8 names. Use the arrows to set the exact turn order." },
                    _players,
                    add,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { new Label { Text = "Teams", VerticalOptions = LayoutOptions.Center }, _teamMode }
                    },
                    new Label { Text = "In team mode, players 1 and 5 form Team A; 2 and 6 Team B; 3 and 7 Team C; 4 and 8 Team D. With fewer players, partners are assigned by alternating halves.", FontSize = 12 },
                    start
                }
            }
        };
    }

    private void AddPlayerRow(string name)
    {
        var entry = new Entry { Placeholder = "Player name", Text = name, HorizontalOptions = LayoutOptions.Fill };
        var up = new Button { Text = "↑", WidthRequest = 48 };
        var down = new Button { Text = "↓", WidthRequest = 48 };
        var remove = new Button { Text = "×", WidthRequest = 48 };
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new(48), new(48), new(48) } };
        row.Add(entry, 0); row.Add(up, 1); row.Add(down, 2); row.Add(remove, 3);
        up.Clicked += (_, _) => Move(row, -1);
        down.Clicked += (_, _) => Move(row, 1);
        remove.Clicked += (_, _) => { if (_players.Children.Count > 2) _players.Children.Remove(row); };
        _players.Children.Add(row);
    }

    private void Move(View row, int change)
    {
        var oldIndex = _players.Children.IndexOf(row);
        var newIndex = oldIndex + change;
        if (newIndex < 0 || newIndex >= _players.Children.Count) return;
        _players.Children.RemoveAt(oldIndex);
        _players.Children.Insert(newIndex, row);
    }

    private async void StartClicked(object? sender, EventArgs e)
    {
        try
        {
            var players = _players.Children.Cast<Grid>()
                .Select(row => ((Entry)row.Children[0]).Text?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => Player.Create(name!)).ToArray();
            IReadOnlyList<Team> teams = [];
            var mode = _teamMode.IsToggled ? GameMode.Teams : GameMode.Individual;
            if (mode == GameMode.Teams)
            {
                if (players.Length < 4 || players.Length % 2 != 0)
                    throw new InvalidOperationException("Team mode requires 4, 6 or 8 players.");
                var teamCount = players.Length / 2;
                teams = Enumerable.Range(0, teamCount)
                    .Select(i => Team.Create($"Team {(char)('A' + i)}", [players[i].Id, players[i + teamCount].Id]))
                    .ToArray();
            }
            var game = new GameEngine().CreateGame(players, mode, teams);
            await Navigation.PushAsync(new GamePage(game));
        }
        catch (Exception ex) { await DisplayAlert("Cannot start game", ex.Message, "OK"); }
    }
}
