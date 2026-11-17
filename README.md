[![Build & Test Debug](https://github.com/Space-Klovns/Klovnstation14/actions/workflows/build-test-debug.yml/badge.svg)](https://github.com/Space-Klovns/Klovnstation14/actions/workflows/build-test-debug.yml)
[![YAML Linter](https://github.com/Space-Klovns/Klovnstation14/actions/workflows/yaml-linter.yml/badge.svg)](https://github.com/Space-Klovns/Klovnstation14/actions/workflows/yaml-linter.yml)

Klovnstation 14 _(KS14)_ is a fork of [Space Station 14](https://github.com/space-wizards/space-station-14).
[Our discord server.](https://discord.gg/rSjZ48VnMx)
We use the [Robust Toolbox engine](https://github.com/space-wizards/RobustToolbox).

If you want to host or create content for base SS14, go to the [Space Station 14 repository](https://github.com/space-wizards/space-station-14), as it is holds all base code and resources for SS14 forks.

## Documentation/Wiki

The [Wizard's Den docs site](https://docs.spacestation14.com/) has documentation on SS14's (and by extension, also KS14's) content, engine, game design, and more, including code standards.

## Legal Info

All code in this codebase is released under the AGPL-3.0-or-later license. [You can view the text of the license here.](https://github.com/Space-Klovns/Klovnstation14/blob/master/LICENSES/AGPL-3.0-or-later.txt)

Source files in this repository may be dual-licensed (alongside AGPL-3.0-or-later), or only licensed, under one of the three below licenses:
- [MIT](https://github.com/Space-Klovns/Klovnstation14/blob/master/LICENSES/MIT.txt)
- [MPL-2.0](https://github.com/Space-Klovns/Klovnstation14/blob/master/LICENSES/MPL-2.0.txt)
- [CC-BY-NC-SA-3.0](https://github.com/Space-Klovns/Klovnstation14/blob/master/LICENSES/CC-BY-NC-SA-3.0-US.txt)

All code past commit hash f9a52e8fbb8066e56d34ba1908c399699e801c42 on Wed, 29 Oct 2025 16:04:39 +0000 is also automatically subject to [this notice](NOTICE).
Inclusion of additional legal notices is allowed under §7(b) of the (A)GPL assuming they do not infringe on any of the freedoms granted to the user by the license.

Most media assets are licensed under [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) unless stated otherwise. Assets have their license and the copyright in the metadata file. [Example](https://github.com/space-wizards/space-station-14/blob/master/Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).

> [!NOTE]
> Some assets are licensed under the non-commercial [CC-BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/) or similar non-commercial licenses, and will need to be removed if you wish to use this project commercially.

## Development

As the repository contains private submodules, you may need to use this command to update/initialise submodules:
`git submodule update --init --recursive RobustToolbox`
instead of the usual:
`git submodule update --init --recursive`

### Server stuck after `Flushing entities` on first launch

On a fresh clone, the first server launch creates the SQLite database and runs every migration. If that launch is killed before it finishes, EF Core leaves a lock row in the `__EFMigrationsLock` table. Every later launch then waits for that lock forever: the log stops at `Flushing entities. Entity count: 0`, and the main thread sits in `TaskManager.BlockWaitOnTask`.

To fix it, stop the server and delete the half-built database (`bin/Content.Server/data/preferences.db`, `-wal` and `-shm`). The next launch builds a fresh one. Let that launch finish, since it takes longer than usual.
