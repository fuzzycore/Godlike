# GODLIKE

Frontline killstreak announcer for Dalamud. Pops escalating arena callouts (KILL, DOUBLE KILL, GODLIKE, ...) with bundled voice lines as your streak climbs.

## Build

```powershell
dotnet build -c Release -p:Platform=x64
```

Output: `bin\x64\Release\Godlike.dll`

## Install (dev)

Point Dalamud's dev plugin loader at `bin\x64\Release\Godlike.dll`, or install from the [fuzzycore/ffxivrepo](https://github.com/fuzzycore/ffxivrepo) custom repository.
