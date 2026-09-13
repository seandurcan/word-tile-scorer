# Word Tile Scorer

Android-first scorekeeper for word-tile games, implemented with a shared C# core so Windows and Apple releases can reuse the rules and data model.

## Confirmed first implementation slice

- 2–8 named players.
- Player order can be moved up or down before a game.
- Individual play or teams.
- Four teams of two are supported; the turn order remains the arranged player order, allowing `A1, B1, C1, D1, A2, B2, C2, D2`.
- Enter a complete word; standard English tile values are supplied automatically.
- All letters start at their ordinary value. Select one or several letters and apply one exclusive double-letter or triple-letter premium to all selected letters.
- Selected tiles and their individual premiums are listed by tile position, so repeated letters remain distinguishable.
- Apply the word multiplier only after all letter multipliers have been included.
- Add and score further words created by the same play; the turn total is the sum of all words.
- Normal, double and triple letter multipliers.
- Up to three normal, double or triple word-premium squares can be recorded; they compound after all letter premiums.
- Pass for zero points.
- Undo the most recent turn.
- Running player contributions and team totals.
- Offline game-state persistence on the device.
- Mandatory temporary word-validation gate: Collins opens inside the app with explicit Accept word and Reject word controls before the score can be recorded.
- Changing scoring premiums does not cancel an accepted dictionary result; only changing the spelling requires another check.
- Record turn always responds: it either explains missing input or confirms the player, recorded score and next player.
- The active player is displayed in a prominent turn card with team and turn number.
- The interface uses a high-contrast colour-blind-safe navy, blue, amber and neutral palette; status is never communicated by colour alone.

League and knockout entities are included in the shared model, but their user interfaces are deliberately not part of this first slice.

## Projects

- `WordTileScorer.Core`: platform-neutral models and scoring/game rules.
- `WordTileScorer.App`: .NET MAUI Android application.
- `WordTileScorer.Core.SelfTest`: dependency-free executable checks for scoring and turn order.

## Build prerequisites

.NET 10 SDK with the MAUI Android workload:

```powershell
dotnet workload install maui-android
dotnet build .\WordTileScorer.App\WordTileScorer.App.csproj -f net10.0-android
```

Run the rule checks with:

```powershell
dotnet run --project .\WordTileScorer.Core.SelfTest
```

## Scoring boundary

The intended dictionary is Collins Scrabble Words (CSW24). Until licensed programmatic access is available, the application copies each entered word, opens the official Collins checker and requires the players to confirm its result. Every word created by a play must be confirmed. A changed word must be checked again; an unconfirmed word cannot be scored.
