#!/usr/bin/env python3
"""
Installs git hooks, updates them, updates submodules, that kind of thing.
"""

import os
import shutil
import subprocess
import sys
import time
import configparser # KS14
import threading # KS14
from pathlib import Path
from typing import List, Optional

def get_repo_root() -> Path:
    """
    Dynamically resolves the root of the git repository.
    Falls back to the script's parent directory if not run inside a git repo
    (e.g. a .zip download), which check_for_zip_download() reports properly.
    """
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        text=True
    )

    if result.returncode != 0:
        return Path(__file__).resolve().parent.parent

    return Path(result.stdout.strip())


REPO_ROOT = get_repo_root()
BUILD_CHECKER_DIR = Path(__file__).resolve().parent
SOLUTION_PATH = REPO_ROOT / "SpaceStation14.slnx"
GITMODULES_PATH = REPO_ROOT / ".gitmodules"
DISABLE_SUBMODULE_AUTOUPDATE_PATH = BUILD_CHECKER_DIR / "DISABLE_SUBMODULE_AUTOUPDATE"
INSTALLED_HOOKS_VERSION_PATH = BUILD_CHECKER_DIR / "INSTALLED_HOOKS_VERSION"
HOOKS_SOURCE_DIR = BUILD_CHECKER_DIR / "hooks"
# If this doesn't match the saved version we overwrite them all.
CURRENT_HOOKS_VERSION = "4"
QUIET = len(sys.argv) == 2 and sys.argv[1] == "--quiet"


def run_command(command: List[str], capture: bool = False, cwd: Optional[Path] = None) -> subprocess.CompletedProcess:
    """
    Runs a command with pretty output.
    """
    text = ' '.join(command)
    if not QUIET:
        print("$ {}".format(text))

    sys.stdout.flush()

    if capture:
        completed = subprocess.run(command, stdout=subprocess.PIPE, text=True, cwd=cwd)
    else:
        completed = subprocess.run(command, cwd=cwd)

    if completed.returncode != 0:
        print("Error: command exited with code {}!".format(completed.returncode))

    return completed

# KS14 Start
def check_git_access(repo_url):
    """
    Checks whether the current credentials can reach the given submodule URL.
    Talks to the remote URL directly, so this works even for submodules that
    haven't been initialised/cloned locally yet.
    """
    try:
        # Use git ls-remote to check connectivity and authentication
        # stdout and stderr are redirected to devnull to suppress console output
        result = subprocess.run(
            ["git", "ls-remote", repo_url],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=10, # Prevents hanging on interactive credential prompts
            check=True
        )
        return True
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
        return False

# returns smth like ['RobustToolbox', 'Resources/Prototypes/_KsModule', 'Resources/Audio/_KsModule', 'Resources/_KsModule_ReplacedPrototypes']
def get_all_submodules():
    result = subprocess.run(
        ["git", "config", "--file={}".format(GITMODULES_PATH), "--get-regexp", r"^\^?submodule\..*\.path$"],
        capture_output=True,
        text=True,
        check=True
    )

    return [line.split()[1] for line in result.stdout.strip().split("\n") if line]

def get_submodule_url(target_path):
    if not GITMODULES_PATH.is_file():
        raise FileNotFoundError("{} file not found.".format(GITMODULES_PATH))

    config = configparser.ConfigParser()
    config.read(GITMODULES_PATH)

    # Search through the sections for the matching path
    for section in config.sections():
        if config.has_option(section, "path") and config.get(section, "path") == target_path:
            return config.get(section, "url")

    return None

def thread_doupdate(repo_path):
    # repo_path is relative to REPO_ROOT (as recorded in .gitmodules), which is
    # what get_submodule_url expects to match against. This lookup works even
    # if the submodule hasn't been initialised/cloned yet, since it reads
    # straight from .gitmodules instead of the local submodule git config.
    repo_url = get_submodule_url(repo_path)
    if (repo_url == None or
        not check_git_access(repo_url)):
        return

    run_command(["git", "submodule", "update", "--init", "--recursive", repo_path], cwd=REPO_ROOT)

# KS14 End

def update_submodules():
    """
    Updates all submodules.
    """

    if 'GITHUB_ACTIONS' in os.environ:
        return

    if DISABLE_SUBMODULE_AUTOUPDATE_PATH.is_file():
        return

    if shutil.which("git") is None:
        raise FileNotFoundError("git not found in PATH")

    # KS14 start: replaced all the logic here with manually checking each repo

    for submodule_path in get_all_submodules():
        thread_doupdate(submodule_path)

    # KS14 end

def install_hooks():
    """
    Installs the necessary git hooks into .git/hooks.
    """

    # Read version file.
    if INSTALLED_HOOKS_VERSION_PATH.is_file():
        with open(INSTALLED_HOOKS_VERSION_PATH, "r") as f:
            if f.read() == CURRENT_HOOKS_VERSION:
                if not QUIET:
                    print("No hooks change detected.")
                return

    print("Hooks need updating.")

    hooks_target_dir = Path(run_command(["git", "rev-parse", "--git-path", "hooks"], True, cwd=REPO_ROOT).stdout.strip())
    hooks_source_dir = HOOKS_SOURCE_DIR

    if not hooks_target_dir.is_absolute():
        hooks_target_dir = REPO_ROOT / hooks_target_dir

    # Clear entire tree since we need to kill deleted files too.
    for filename in os.listdir(hooks_target_dir):
        os.remove(hooks_target_dir / filename)

    for filename in os.listdir(hooks_source_dir):
        print("Copying hook {}".format(filename))
        shutil.copy2(hooks_source_dir / filename, hooks_target_dir / filename)

    with open(INSTALLED_HOOKS_VERSION_PATH, "w") as f:
        f.write(CURRENT_HOOKS_VERSION)


def reset_solution():
    """
    Force VS to think the solution has been changed to prompt the user to reload it, thus fixing any load errors.
    """

    with SOLUTION_PATH.open("r") as f:
        content = f.read()

    with SOLUTION_PATH.open("w") as f:
        f.write(content)

def check_for_zip_download():
    # Check if .git exists,
    if run_command(["git", "rev-parse"]).returncode != 0:
        print("It appears that you downloaded this repository directly from GitHub. (Using the .zip download option) \n"
              "When downloading straight from GitHub, it leaves out important information that git needs to function. "
              "Such as information to download the engine or even the ability to even be able to create contributions. \n"
              "Please read and follow https://docs.spacestation14.com/en/general-development/setup/setting-up-a-development-environment.html \n"
              "If you just want a Sandbox Server, you are following the wrong guide! You can download a premade server following the instructions here:"
              "https://docs.spacestation14.com/en/general-development/setup/server-hosting-tutorial.html \n"
              "Closing automatically in 30 seconds.")
        time.sleep(30)
        exit(1)

if __name__ == '__main__':
    check_for_zip_download()
    update_submodules() # KS14: moved before hooks
    install_hooks()
