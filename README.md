# Dependencies Hunter Unity3D Tool ![unity](https://img.shields.io/badge/Unity-100000?style=for-the-badge&logo=unity&logoColor=white)

![stability-stable](https://img.shields.io/badge/stability-stable-green.svg)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg)](https://GitHub.com/Naereen/StrapDown.js/graphs/commit-activity)

##
This tool finds unreferenced assets by scanning all files in your Unity project.

All code combined into one script for easier portability.
So you can just copy-paste [DependenciesHunter.cs](./Packages/DependenciesHunter/Editor/DependenciesHunter.cs) to your project in any Editor folder.

# How it works

At first, it calls
```code
AssetDatabase.GetAllAssetPaths()
```
to form a map of all assets.

Then it uses:
```code
AssetDatabase.GetDependencies
```
to find dependencies for each of those assets. As a result dependencies map is formed.

Then it simply finds all assets which are not presented as a dependency within this map.
Such assets are considered as unused if they aren't marked as to be ignored in this analysis (by a list of RegExp patterns).

# Ways of usage

The tool has two ways to use it. Each has a menu option and an editor window.

## To list all unused assets in your project..
..click on "Tools/Dependencies Hunter" option which will open the "AllProjectAssetsReferencesWindow" window.

![plot](./Screenshots/project_analysis.png)

## To list all references towards selected assets..
..select the assets and use a context menu option "Find References in Project".
It will open the "SelectedAssetsReferencesWindow" window with the results.

![plot](./Screenshots/context_menu.png) | ![plot](./Screenshots/context_menu_result.png)

## Installation

 1. Through Unity's Package Manager. Use this as git url: `https://github.com/AlexeyPerov/Unity-Dependencies-Hunter.git#upm`. UPM support added via [template](https://github.com/STARasGAMES/Unity-package-repo-setup-template).
 2. Or you can just copy and paste file [DependenciesHunter.cs](./Packages/DependenciesHunter/Editor/DependenciesHunter.cs) inside Editor folder 

## Contributions

Feel free to [report bugs, request new features](https://github.com/AlexeyPerov/Unity-Dependencies-Hunter/issues) 
or to [contribute](https://github.com/AlexeyPerov/Unity-Dependencies-Hunter/pulls) to this project! 