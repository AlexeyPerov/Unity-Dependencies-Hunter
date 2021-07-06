# Dependencies Hunter Unity3D Tool

![stability-wip](https://img.shields.io/badge/stability-work_in_progress-lightgrey.svg)

All code combined into one script for easier portability.
So you can just copy-paste Dependencies Hunter.cs to your project in any Editor folder.

### How it works

This tool finds unreferenced assets by scanning all files in your Unity project.
<br>
At first it calls
```code
AssetDatabase.GetAllAssetPaths()
```
to form a map of all assets.

Then it uses:
```code
AssetDatabase.GetDependencies
```
to find dependencies for each of those assets. As a result dependencies map is formed.

Then it simply finds all assets which are not presented as a dependency anywhere.

### Tools

Dependencies Hunter consists of two editor windows.
Their names are speaking.

- AllProjectAssetsReferencesWindow

It is called from Tools/Dependencies Hunter and lists all assets of your project which it considers as unused.

![plot](./Screenshots/window_result.png)

- SelectedAssetsReferencesWindow

This one called from the context menu by "Find References in Project".
It attempts to find all assets which reference the selected one. 

![plot](./Screenshots/context_menu_result.png)

### TODO

- Proper way of managing the list of assets which are considered as "used" by default. It is hardcoded now.
- General cleanup & UI tuning.
