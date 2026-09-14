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
                    new Label { Text = "Word Tile Scorer — version 0.3.8", FontAttributes = FontAttributes.Bold, TextColor = AppPalette.Blue },
                    new Label { Text = "Enter 2–8 names. Use the arrows to set the exact turn order." },
                    _players,
                    add,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { new Label { Text = "Teams", VerticalOptions = LayoutOptions.Center }, _teamMode }
                    },
                    new Label { Text = "In team mode, select each team's members from dropdown lists on the next screen. A selected player is removed from the other lists.", FontSize = 12 },
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
                await Navigation.PushAsync(new TeamSetupPage(players));
                return;
            }
            var game = new GameEngine().CreateGame(players, mode, teams);
            await Navigation.PushAsync(new GamePage(game));
        }
        catch (Exception ex) { await DisplayAlert("Cannot start game", ex.Message, "OK"); }
    }
}

public sealed class TeamSetupPage : ContentPage
{
    private readonly IReadOnlyList<Player> _players;
    private readonly List<(string TeamName, Picker First, Picker Second)> _rows = [];
    private bool _refreshing;

    public TeamSetupPage(IReadOnlyList<Player> players)
    {
        _players = players;
        Title = "Select teams";
        var rows = new VerticalStackLayout { Spacing = 12 };
        for (var i = 0; i < players.Count / 2; i++)
        {
            var teamName = $"Team {(char)('A' + i)}";
            var first = PlayerPicker("First player");
            var second = PlayerPicker("Second player");
            first.SelectedIndexChanged += (_, _) => RefreshAvailablePlayers();
            second.SelectedIndexChanged += (_, _) => RefreshAvailablePlayers();
            _rows.Add((teamName, first, second));
            rows.Children.Add(new Border
            {
                Stroke = AppPalette.Blue, StrokeThickness = 1, Padding = 12,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Content = new VerticalStackLayout
                {
                    Children = { new Label { Text = teamName, FontAttributes = FontAttributes.Bold, FontSize = 18 }, first, second }
                }
            });
        }

        var start = new Button { Text = "Start team game", BackgroundColor = AppPalette.Blue, TextColor = Colors.White };
        start.Clicked += StartGame;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20, Spacing = 14,
                Children =
                {
                    new Label { Text = "Assign players to teams", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Choose two players for each team. Only players not already assigned remain available." },
                    rows, start
                }
            }
        };
        RefreshAvailablePlayers();
    }

    private static Picker PlayerPicker(string title) => new()
    {
        Title = title,
        ItemDisplayBinding = new Binding(nameof(Player.Name))
    };

    private IEnumerable<Picker> Pickers() => _rows.SelectMany(row => new[] { row.First, row.Second });

    private void RefreshAvailablePlayers()
    {
        if (_refreshing) return;
        _refreshing = true;
        var pickers = Pickers().ToArray();
        foreach (var picker in pickers)
        {
            var current = picker.SelectedItem as Player;
            var assignedElsewhere = pickers.Where(other => other != picker)
                .Select(other => other.SelectedItem as Player)
                .Where(player => player is not null)
                .Select(player => player!.Id)
                .ToHashSet();
            picker.ItemsSource = _players.Where(player => player.Id == current?.Id || !assignedElsewhere.Contains(player.Id)).ToList();
            picker.SelectedItem = current;
        }
        _refreshing = false;
    }

    private async void StartGame(object? sender, EventArgs e)
    {
        try
        {
            if (Pickers().Any(picker => picker.SelectedItem is not Player))
                throw new InvalidOperationException("Select two players for every team.");
            var teams = _rows.Select(row => Team.Create(row.TeamName,
                [((Player)row.First.SelectedItem).Id, ((Player)row.Second.SelectedItem).Id])).ToArray();
            var game = new GameEngine().CreateGame(_players, GameMode.Teams, teams);
            await Navigation.PushAsync(new GamePage(game));
        }
        catch (Exception ex) { await DisplayAlert("Cannot start team game", ex.Message, "OK"); }
    }
}
