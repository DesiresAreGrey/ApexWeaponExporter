# ApexWeaponExporter

Script to export weapon data from Apex Legends for https://desiresaregrey.com/misc/apex-weapon-stats (Site is in the process of being updated for this new format)

The script is run using the command `dotnet run ApexWeaponExporter.cs` and requires [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

It downloads specific versions of the game's files (using Steam manifest IDs https://steamdb.info/depot/1172471/manifests/) with DepotDownloader, and uses rsx to get the weapon definition files. It uses a season manifest file (explained below) to copy the needed definitions to an Output directory as well as the manifest itself to be uploaded to the site.

## Manifest

This is a snippet of the [Season 29 manifest](https://github.com/DesiresAreGrey/ApexWeaponExporter/blob/main/Manifests/s29.json). Most of the properties are self explanatory.

`SteamManifestID` is the [manifest id](https://steamdb.info/depot/1172471/manifests/) that has the season's files. Omitting it instead prompts you for an ID (or to just use the latest) that DepotDownloader downloads. Only the `paks/Win64/patch_master.rpak` and `paks/Win64/common.rpak` files are downloaded (as well as any of the other common rpaks like common(01).rpak) which amounts to around 600mb of downloaded game files.

`Weapons` is a dictionary of the weapons in the season. The key is the ID that I use for the site (since the asset name isn't really the best name for
the weapon).

`Name` is the display name of the weapon (suffixed with ` (Mythic)` if its a crate weapon)

`Asset` is the asset name of the weapon in the game files.

`CoreWeapon` is a boolean that indicates if the weapon is a "core" weapon, meaning that it is part of the main BR weapon pool. Omitting it defaults to true.

`Modes` is an array of the weapon's firing modes. The name is the display name, `Mods` are the overrides in the Mods section of the weapon definition
that are applied for said firing mode. Sometimes firing modes use multiple mods which is why its an array. `Type` is the type of firing mode it is,
though its not currently used for anything on the site. The only exception is the "Base" type which is used to apply mods to the default firing mode of the weapon.

```json
{
  "Name": "Season 29",
  "FullName": "Season 29: Overclocked",
  "Season": 29,
  "Split": 1,
  "Title": "Overclocked",
  "ID": "s29",
  "ReleaseDate": "2026-05-05",
  "SteamManifestID": 4683457128691488723,

  "Weapons": {
    "g7scout": { "Name": "G7 Scout", "Asset": "mp_weapon_g2", "CoreWeapon": false },
    "g7scout_mythic": { "Name": "G7 Scout (Mythic)", "Asset": "mp_weapon_g2_crate",
      "Modes": [{"Name": "Double Tap", "Mods": ["altfire_double_tap"], "Type": "Hopup"}]
    },
  }
}
```
