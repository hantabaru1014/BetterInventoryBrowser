# BetterInventoryBrowser

A [NeosModLoader](https://github.com/zkxs/NeosModLoader) mod for [Neos VR](https://neos.com/) that UI tweaks for InventoryBrowser.

## Current features
![screenshot](https://user-images.githubusercontent.com/16133291/236672368-20c84717-c45f-46e9-85bc-c9442548e7b2.png)

- Added a sidebar to the left of the inventory
    - Pin directory to the sidebar
    - Show recently opened directories in the sidebar
    - The sidebar can be shown/hide and width can be set on settings
- Sort by (Default/Name/Updated/Created)
    - Strip RTF tags on sort by name (Toggleable in settings)
    - Sort folders by name and items by update on "Default" (Toggleable in settings)
- Additional layout
    - NoImgGrid : Default grid layout but no thumbnails
    - Detail : VerticalLayout with extra information
        - You can change the row height, etc. in the settings
- Added a sidebar to the right of the inventory
    - Display thumbnails, update date, etc.
    - The sidebar can be shown/hide and width can be set on settings

## Installation
1. Install [NeosModLoader](https://github.com/zkxs/NeosModLoader).
2. Place [BetterInventoryBrowser.dll](https://github.com/hantabaru1014/BetterInventoryBrowser/releases/latest/download/BetterInventoryBrowser.dll) into your `nml_mods` folder. This folder should be at `C:\Program Files (x86)\Steam\steamapps\common\NeosVR\nml_mods` for a default install. You can create it if it's missing, or if you launch the game once with NeosModLoader installed it will create the folder for you.
3. Start the game. If you want to verify that the mod is working you can check your Neos logs.
