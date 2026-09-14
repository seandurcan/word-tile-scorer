# Word Tile Scorer

Android-first scorekeeper for word-tile games, implemented with a shared C# core so Windows and Apple releases can reuse the rules and data model.

Development APKs use a stable development application ID and signing key, allowing subsequent test versions to install as normal updates. Production will use a separate private signing identity.

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
- Dictionary checking is optional and used only when an opponent challenges a word. An unchallenged word can be recorded immediately. Selecting Reject word records 0 points for that player and immediately advances to the next turn.
- Record turn always responds: it either explains missing input or confirms the player, recorded score and next player.
- The active player is displayed in a prominent turn card with team and turn number.
- The interface uses a high-contrast colour-blind-safe navy, blue, amber and neutral palette; status is never communicated by colour alone.
- Each player has a persistent rack. Newly drawn tiles and tiles actually played are recorded separately; unused tiles remain visible when that player's next turn begins.
- Standard 100-tile English distribution and per-letter usage are tracked from the tiles added to racks and actually used. A crossing tile shared by several new words is counted once.
- Attempts to exceed the rack or set inventory show the exact problem and require an explicit Allow or Cancel decision.
- A letter that is no longer available in the 100-tile set is refused immediately and is not added to the rack.
- Rack tiles are an unordered collection. A word is blocked only if it cannot be formed from any combination of the current rack plus tiles recorded on the board. The opening word may use any subset of the rack; crossing words may reuse shared new tiles without consuming them twice.
- Actually playing all seven rack tiles in one turn adds the official 50-point bonus. Merely having seven tiles in the rack does not.
- Word premiums use bottom-aligned Double Word and Treble Word buttons with visible counts and a checked three-square board maximum.
- Team members are chosen from dropdown lists; assigned players disappear from every other dropdown.

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

The intended dictionary is Collins Scrabble Words (CSW24). If an opponent challenges a word, the application copies it and opens the official Collins checker. Unchallenged words do not require a dictionary check.
