from os import path, listdir
import mutagen
from mutagen.mp3 import MP3
from mutagen.oggvorbis import OggVorbis

# load music
script_path = path.dirname(__file__)
music_path = path.join(script_path, './Resources/Audio/', 'Lobby')
lobbysongs = [f for f in listdir(music_path)
              if path.isfile(path.join(music_path, f))
              and (".mp3" in f.lower() or ".ogg" in f.lower())]

with open(path.join(script_path,"./Resources/Prototypes/Soundcollections/lobby.yml"), "w") as lobbymusic_cfg:
    lobbymusic_cfg.write("- type: soundCollection\n  id: LobbyMusic\n  files:\n")
    for item in lobbysongs:
        lobbymusic_cfg.write(f"    - /Audio/Lobby/{item}\n")

# load jukebox music
jb_music_path = path.join(script_path, './Resources/Audio/', 'Lobby')
jb_songs = [f for f in listdir(jb_music_path)
            if path.isfile(path.join(jb_music_path, f))
            and (".mp3" in f.lower() or ".ogg" in f.lower())]

with open(path.join(script_path,"./Resources/Prototypes/Catalog/Jukebox/Standard.yml"), "w") as jb_music_cfg:
    for item in jb_songs:
        full_path = path.join(jb_music_path, item)

        # Get metadata title
        try:
            if item.lower().endswith('.mp3'):
                audio = MP3(full_path)
                title = audio.get('TIT2', item.split(".")[0])[0]
            elif item.lower().endswith('.ogg'):
                audio = OggVorbis(full_path)
                title = audio.get('title', [item.split(".")[0]])[0]
            else:
                title = item.split(".")[0]
        except:
            title = item.split(".")[0]  # fallback to filename

        jb_music_cfg.write(f"- type: jukebox\n  id: {item.split('.')[0]}\n")
        jb_music_cfg.write(f"  name: {title}\n")
        jb_music_cfg.write(f"  path:\n    path: /Audio/Lobby/{item}\n\n")
