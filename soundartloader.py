from os import path, listdir
#load music
script_path = path.dirname(__file__)
music_path = path.join(script_path, './Resources/Audio/', 'Lobby')
lobbysongs = [f for f in listdir(music_path) if path.isfile(path.join(music_path, f)) and ".ogg" in f]
lobbymusic_cfg = open(path.join(script_path,"./Resources/Prototypes/Soundcollections/lobby.yml"), "w")
lobbymusic_cfg.write("- type: soundCollection\n  id: LobbyMusic\n  files:\n")
for item in lobbysongs:
    lobbymusic_cfg.write(f"    - /Audio/Lobby/{item}\n")

lobbymusic_cfg.close()

#load jukebox music
jb_music_path = path.join(script_path, './Resources/Audio/', 'Lobby')
jb_songs = [f for f in listdir(jb_music_path) if path.isfile(path.join(jb_music_path, f)) and ".ogg" in f]
jb_music_cfg = open(path.join(script_path,"./Resources/Prototypes/Catalog/Jukebox/Standard.yml"), "w")
for item in jb_songs:
    stripped_item = item.split(".")[0]
    jb_music_cfg.write(f"- type: jukebox\n  id: {stripped_item}\n  name:  {stripped_item}\n  path:\n    path: /Audio/Lobby/{item}\n\n")

jb_music_cfg.close()

#load lobby screens
lobby_screen_path = path.join(script_path, './Resources/Textures/', 'LobbyScreens')
lobby_screens = [f for f in listdir(lobby_screen_path) if path.isfile(path.join(lobby_screen_path, f)) and (".webp" in f or ".png" in f)]
lobby_screen_cfg = open(path.join(script_path,"./Resources/Prototypes/lobbyscreens.yml"), "w")
for item in lobby_screens:
    stripped_item = item.split(".")[0]
    lobby_screen_cfg.write(f"- type: lobbyBackground\n  id: {stripped_item}\n  background: /Textures/LobbyScreens/{item}\n\n")

lobby_screen_cfg.close()
