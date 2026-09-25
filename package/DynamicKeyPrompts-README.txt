=====================================================================
  Dynamic Key Prompts  -  Dark Souls II: Scholar of the First Sin
=====================================================================

Shows the keys and mouse buttons YOU bound in every button prompt,
instead of Xbox buttons: menus, the help bar at the bottom, "Rest at
bonfire" style prompts and tutorial messages. Rebind a key in the game's
Key Bindings screen and the prompts follow immediately.


REQUIREMENTS
------------
- Dark Souls II: Scholar of the First Sin, Steam version, latest update
- Windows 10 or 11 (64-bit)


INSTALLATION
------------
With Vortex: install the mod from its Nexus page ("Mod Manager Download")
and deploy - Vortex puts the files into the Game folder by itself.

Manually:
1. Find the game folder:
   Steam > Library > right-click "DARK SOULS II: Scholar of the First Sin"
   > Manage > Browse local files > open the "Game" folder
   (the one that contains DarkSoulsII.exe).

2. Copy into that folder:
     dinput8.dll
     DynamicKeyPrompts   (the whole folder)
   (this readme does not need to be copied)

   It should look like this:
     Game\DarkSoulsII.exe
     Game\dinput8.dll
     Game\DynamicKeyPrompts\DynamicKeyPrompts.dll
     Game\DynamicKeyPrompts\DynamicKeyPrompts.ini

3. Start the game as usual. With Seamless Co-op, start it through
   ds2sc_launcher.exe as before.

The first start takes a moment longer: the mod prepares its icons.


UNINSTALLING
------------
Remove the mod in Vortex, or delete dinput8.dll and the DynamicKeyPrompts
folder from the Game folder. The mod does not change any of the game's own
files. (Vortex may leave the files the mod created itself - the log, icons
and cache - in Game\DynamicKeyPrompts; you can delete that folder.)


UPDATING
--------
New versions: https://www.nexusmods.com/darksouls2/mods/1738
Copy the new dinput8.dll and DynamicKeyPrompts.dll over the old ones.
Keep your DynamicKeyPrompts.ini if you changed settings.


SETTINGS
--------
Open DynamicKeyPrompts\DynamicKeyPrompts.ini with Notepad. Changes apply
the next time you start the game.

  Style=icons        key and mouse icons (default)
  Style=text         key names as text, e.g. [E]

  IconTheme=dark     dark stone keys with a bronze rim (default)
  IconTheme=minimal  only a thin frame around the key name
  IconTheme=silver   light metal keys, like the game's RB/LT icons

  Labels=auto        mouse button where you use one (attack = left mouse
                     button, lock-on = middle button), key otherwise
  Labels=keyboard    always the keyboard key
  Labels=mouse       always the mouse button if the action has one
  Labels=both        both, e.g. H / left mouse button

The file itself describes every option.


YOUR OWN ICONS
--------------
After the first start you will find the icon sheets in
DynamicKeyPrompts\icons\ :

  dark.png    - all icons of the "dark" theme in one picture
  dark.txt    - where each icon is in that picture (name, x, y, width, height)

To change the look, edit the PNG in any image editor (keep transparency)
and start the game - the mod picks the changes up by itself.
To make a separate theme, copy dark.png and dark.txt to e.g. mytheme.png
and mytheme.txt, edit them and set IconTheme=mytheme in the ini.


COMPATIBILITY
-------------
Works together with:
  - Seamless Co-op
  - DS2 Lighting Engine
  - OptiScaler
  - mods that change the game's fonts should work too: the icons are
    added to whatever font you have installed

The mod loads through dinput8.dll. If another mod already uses dinput8.dll,
download the optional "xinput1_3 loader" file and use its xinput1_3.dll
instead of dinput8.dll (see the readme inside it). With Ultimate ASI Loader
you can also rename dinput8.dll to DynamicKeyPrompts.asi (not tested).

Made to work with every game language (tested with Russian).
Key names follow the game (QWERTY, and the game's own German / French
variants).


TROUBLESHOOTING
---------------
The mod writes DynamicKeyPrompts\DynamicKeyPrompts.log every time the
game starts.

- Prompts still show gamepad buttons:
  make sure dinput8.dll is next to DarkSoulsII.exe (not inside the
  DynamicKeyPrompts folder) and look at the log.
- Icons look broken or empty:
  set Style=text in the ini to confirm the mod works, and report it.
- Something else:
  set Diagnostics=1 in the ini, start the game, open the menu where it
  goes wrong and press F9, then quit. Attach DynamicKeyPrompts.log to
  your report.

Report problems on the mod's Nexus page or on GitHub:
  https://www.nexusmods.com/darksouls2/mods/1738?tab=bugs
  https://github.com/HappyEntity/DS2-Dynamic-Key-Prompts/issues


SOURCE CODE
-----------
https://github.com/HappyEntity/DS2-Dynamic-Key-Prompts


CREDITS
-------
- dearxan by tremwil          https://github.com/tremwil/dearxan
- MinHook by Tsuda Kageyu      https://github.com/TsudaKageyu/minhook
- The Souls modding community for documenting the game's file formats

License: MIT, see DynamicKeyPrompts\LICENSE.
Third-party licenses: DynamicKeyPrompts\THIRD-PARTY-NOTICES.md


---------------------------------------------------------------------
  ПО-РУССКИ
---------------------------------------------------------------------
Мод показывает в подсказках клавиши и кнопки мыши, которые вы назначили
в игре, вместо кнопок геймпада, и сразу обновляет их после
переназначения.

Установка: скопируйте dinput8.dll и папку DynamicKeyPrompts в папку
Game игры (там, где DarkSoulsII.exe). С Seamless Co-op запускайте игру
как обычно, через ds2sc_launcher.exe.

Удаление: удалите dinput8.dll и папку DynamicKeyPrompts. Файлы игры мод
не изменяет.

Настройки: DynamicKeyPrompts\DynamicKeyPrompts.ini
  Style      - icons (значки) или text (текст вида [E])
  IconTheme  - dark, minimal, silver или своя тема
  Labels     - auto, keyboard, mouse, both (клавиша или кнопка мыши)

Свои значки: DynamicKeyPrompts\icons\<тема>.png - все значки темы на одной
картинке, <тема>.txt - их расположение. Отредактируйте PNG или скопируйте
тему под новым именем и укажите его в IconTheme.

Если что-то не работает: поставьте Diagnostics=1, запустите игру,
нажмите F9 там, где проблема, и приложите DynamicKeyPrompts.log.
