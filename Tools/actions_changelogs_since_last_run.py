#!/usr/bin/env python3

"""
Sends updates to a Discord webhook for new changelog entries since the last GitHub Actions publish run.

Automatically figures out the last run and changelog contents with the GitHub API.
"""

import itertools
import os
from datetime import datetime  # KS14
from pathlib import Path
from typing import Any, Iterable

import requests
import yaml
import time

DEBUG = False
DEBUG_CHANGELOG_FILE_OLD = Path("Resources/Changelog/Old.yml")
GITHUB_API_URL = os.environ.get("GITHUB_API_URL", "https://api.github.com")

# https://discord.com/developers/docs/resources/webhook
DISCORD_SPLIT_LIMIT = 2000
DISCORD_WEBHOOK_URL = os.environ.get("DISCORD_WEBHOOK_URL")

CHANGELOG_FILE = "Resources/Changelog/Klovn.yml"

TYPES_TO_EMOJI = {"Fix": "⚒️", "Add": "✅", "Remove": "❌", "Tweak": "😂"}

ChangelogEntry = dict[str, Any]

# KS14 start: max entries to send in one run
MAX_NEW_ENTRIES = int(os.environ.get("CHANGELOG_MAX_NEW_ENTRIES", "10"))
# KS14 end


def main():
    if not DISCORD_WEBHOOK_URL:
        print("No discord webhook URL found, skipping discord send")
        return

    if DEBUG:
        # to debug this script locally, you can use
        # a separate local file as the old changelog
        last_changelog_stream = DEBUG_CHANGELOG_FILE_OLD.read_text()
    else:
        # when running this normally in a GitHub actions workflow,
        # it will get the old changelog from the GitHub API
        last_changelog_stream = get_last_changelog()
        # KS14 start: skip sending when no baseline exists
        if last_changelog_stream is None:
            return
        # KS14 end

    last_changelog = yaml.safe_load(last_changelog_stream)
    with open(CHANGELOG_FILE, "r") as f:
        cur_changelog = yaml.safe_load(f)

    diff = diff_changelog(last_changelog, cur_changelog)
    # KS14 start: safety backstop against a wrong baseline
    diff = cap_entries(list(diff))
    # KS14 end
    message_lines = changelog_entries_to_message_lines(diff)
    send_message_lines(message_lines)


# KS14 start: cap helper
def cap_entries(entries: list[ChangelogEntry]) -> list[ChangelogEntry]:
    if len(entries) <= MAX_NEW_ENTRIES:
        return entries
    print(
        f"WARNING: {len(entries)} new changelog entries exceeds the limit of {MAX_NEW_ENTRIES}; "
        f"the baseline run is probably wrong. Sending only the newest {MAX_NEW_ENTRIES}."
    )
    entries = sorted(entries, key=lambda e: e["id"])
    return entries[-MAX_NEW_ENTRIES:]
# KS14 end


def get_most_recent_workflow(
    sess: requests.Session, github_repository: str, github_run: str
) -> Any:
    workflow_run = get_current_run(sess, github_repository, github_run)
    past_runs = get_past_runs(sess, workflow_run)
    # KS14 start: do not trust API order; pick the newest run strictly before the current one
    current_created = parse_iso(workflow_run["created_at"])
    candidates = [
        run
        for run in past_runs["workflow_runs"]
        if run["id"] != workflow_run["id"]
        and parse_iso(run["created_at"]) < current_created
    ]
    print(f"Considered {len(past_runs['workflow_runs'])} successful runs, {len(candidates)} predate the current run")
    if not candidates:
        return None
    return max(candidates, key=lambda run: parse_iso(run["created_at"]))
    # KS14 end


# KS14 start: ISO-8601 parse helper
def parse_iso(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00"))
# KS14 end


def get_current_run(
    sess: requests.Session, github_repository: str, github_run: str
) -> Any:
    resp = sess.get(
        f"{GITHUB_API_URL}/repos/{github_repository}/actions/runs/{github_run}"
    )
    resp.raise_for_status()
    return resp.json()


def get_past_runs(sess: requests.Session, current_run: Any) -> Any:
    """
    Get all successful workflow runs before our current one.
    """
    # KS14 start: dropped the created filter (flaky ordering); filter by branch instead
    params = {
        "status": "success",
        "branch": current_run["head_branch"],
        "per_page": 100,
    }
    # KS14 end
    resp = sess.get(f"{current_run['workflow_url']}/runs", params=params)
    resp.raise_for_status()
    return resp.json()


def get_last_changelog() -> str | None:  # KS14: may return None
    github_repository = os.environ["GITHUB_REPOSITORY"]
    github_run = os.environ["GITHUB_RUN_ID"]
    github_token = os.environ["GITHUB_TOKEN"]

    session = requests.Session()
    session.headers["Authorization"] = f"Bearer {github_token}"
    session.headers["Accept"] = "Accept: application/vnd.github+json"
    session.headers["X-GitHub-Api-Version"] = "2022-11-28"

    most_recent = get_most_recent_workflow(session, github_repository, github_run)
    # KS14 start: no predecessor found
    if most_recent is None:
        print("No previous successful run found, skipping")
        return None
    # KS14 end
    last_sha = most_recent["head_commit"]["id"]
    print(f"Last successful publish job was {most_recent['id']}: {last_sha}")
    last_changelog_stream = get_last_changelog_by_sha(
        session, last_sha, github_repository
    )

    return last_changelog_stream


def get_last_changelog_by_sha(
    sess: requests.Session, sha: str, github_repository: str
) -> str:
    """
    Use GitHub API to get the previous version of the changelog YAML (Actions builds are fetched with a shallow clone)
    """
    params = {
        "ref": sha,
    }
    headers = {"Accept": "application/vnd.github.raw"}

    resp = sess.get(
        f"{GITHUB_API_URL}/repos/{github_repository}/contents/{CHANGELOG_FILE}",
        headers=headers,
        params=params,
    )
    resp.raise_for_status()
    return resp.text


def diff_changelog(
    old: dict[str, Any], cur: dict[str, Any]
) -> Iterable[ChangelogEntry]:
    """
    Find all new entries not present in the previous publish.
    """
    old_entry_ids = {e["id"] for e in old["Entries"]}
    return (e for e in cur["Entries"] if e["id"] not in old_entry_ids)


def get_discord_body(content: str):
    return {
        "content": content,
        # Do not allow any mentions.
        "allowed_mentions": {"parse": []},
        # SUPPRESS_EMBEDS
        "flags": 1 << 2,
    }


def send_discord_webhook(lines: list[str]):
    content = "".join(lines)
    body = get_discord_body(content)
    retry_attempt = 0

    try:
        response = requests.post(DISCORD_WEBHOOK_URL, json=body, timeout=10)
        while response.status_code == 429:
            retry_attempt += 1
            if retry_attempt > 20:
                print("Too many retries on a single request despite following retry_after header... giving up")
                exit(1)
            retry_after = response.json().get("retry_after", 5)
            print(f"Rate limited, retrying after {retry_after} seconds")
            time.sleep(retry_after)
            response = requests.post(DISCORD_WEBHOOK_URL, json=body, timeout=10)
        response.raise_for_status()
    except requests.exceptions.RequestException as e:
        print(f"Failed to send message: {e}")
        exit(1)


def changelog_entries_to_message_lines(entries: Iterable[ChangelogEntry]) -> list[str]:
    """Process structured changelog entries into a list of lines making up a formatted message."""
    message_lines = []

    for contributor_name, group in itertools.groupby(entries, lambda x: x["author"]):
        message_lines.append("\n")
        message_lines.append(f"**{contributor_name}** updated:\n")

        for entry in group:
            url = entry.get("url")
            if url and not url.strip():
                url = None

            for change in entry["changes"]:
                emoji = TYPES_TO_EMOJI.get(change["type"], "❓")
                message = change["message"]

                # if a single line is longer than the limit, it needs to be truncated
                if len(message) > DISCORD_SPLIT_LIMIT:
                    message = message[: DISCORD_SPLIT_LIMIT - 100].rstrip() + " [...]"

                if url is not None:
                    pr_number = url.split("/")[-1]
                    line = f"{emoji} - {message} ([#{pr_number}]({url}))\n"
                else:
                    line = f"{emoji} - {message}\n"

                message_lines.append(line)

    return message_lines


def send_message_lines(message_lines: list[str]):
    """Join a list of message lines into chunks that are each below Discord's message length limit, and send them."""
    chunk_lines = []
    chunk_length = 0

    for line in message_lines:
        line_length = len(line)
        new_chunk_length = chunk_length + line_length

        if new_chunk_length > DISCORD_SPLIT_LIMIT:
            print("Split changelog and sending to discord")
            send_discord_webhook(chunk_lines)

            new_chunk_length = line_length
            chunk_lines.clear()

        chunk_lines.append(line)
        chunk_length = new_chunk_length

    if chunk_lines:
        print("Sending final changelog to discord")
        send_discord_webhook(chunk_lines)


if __name__ == "__main__":
    main()
