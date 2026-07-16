# ApexWeaponExporter

Script to export weapon data from Apex Legends for https://desiresaregrey.com/misc/apex-weapon-stats (Site is in the process of being updated for this new format)

The script is run using the command `dotnet run ApexWeaponExporter.cs` and requires .NET 10.

It downloads specific versions of the game's files (using Steam manifest IDs https://steamdb.info/depot/1172471/manifests/) with DepotDownloader, and uses rsx to get the weapon definition files. It uses a season manifest file (explained below) to copy the needed definitions to an Output directory as well as the manifest itself to be uploaded to the site.

## Manifest

This is a snippet of the [Season 29 manifest](https://github.com/DesiresAreGrey/ApexWeaponExporter/blob/main/Manifests/s29.json). Most of the properties are self explanatory.

`SteamManifestID` is the [manifest id](https://steamdb.info/depot/1172471/manifests/) that has the season's files. Omitting it instead prompts you for an ID (or to just use the latest) that DepotDownloader downloads. Only the `paks/Win64/patch_master.rpak` and `paks/Win64/common.rpak` files are downloaded (as well as any of the other common rpaks like common(01).rpak) which amounts to around 600mb of downloaded game files.

WIP

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
