#!/usr/bin/env python3
"""
create_github_issues.py — create GitHub Issues from github_backlog_input.json.

Usage:
    python create_github_issues.py --input <path> --output <path>

Auth:  reads GH_TOKEN env var first; falls back to `gh auth token`.
Target: GH_OWNER + GH_REPO env vars override owner/repo from the input JSON.
"""
import argparse
import json
import os
import subprocess
import sys

import requests

API = "https://api.github.com"
HEADERS_BASE = {
    "Accept": "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28",
}

LABEL_COLORS = {
    "bug": "d73a4a",
    "enhancement": "a2eeef",
    "task": "e4e669",
    "documentation": "0075ca",
    "tracking": "cccccc",
    "P0": "b60205",
    "P1": "d93f0b",
    "P2": "fbca04",
    "P3": "0e8a16",
    "size:XS": "c5def5",
    "size:S": "bfd4f2",
    "size:M": "d4c5f9",
    "size:L": "e99695",
    "size:XL": "f9d0c4",
}


def get_token():
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if token:
        return token
    try:
        result = subprocess.run(
            ["gh", "auth", "token"],
            capture_output=True, text=True, check=True,
        )
        return result.stdout.strip()
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        sys.exit(f"no token: set GH_TOKEN or run `gh auth login` ({exc})")


def gh(method, path, token, **kwargs):
    resp = requests.request(
        method,
        f"{API}{path}",
        headers={**HEADERS_BASE, "Authorization": f"Bearer {token}"},
        **kwargs,
    )
    if not resp.ok:
        sys.exit(f"GitHub API {method} {path} -> {resp.status_code}: {resp.text}")
    if resp.status_code == 204:
        return {}
    return resp.json()


def ensure_labels(owner, repo, token, needed):
    existing = {l["name"] for l in gh("GET", f"/repos/{owner}/{repo}/labels?per_page=100", token)}
    for name in needed:
        if name not in existing:
            color = LABEL_COLORS.get(name, "ededed")
            gh("POST", f"/repos/{owner}/{repo}/labels", token, json={"name": name, "color": color})
            print(f"  created label: {name}")


def get_or_create_milestone(owner, repo, token, title):
    milestones = gh("GET", f"/repos/{owner}/{repo}/milestones?state=open&per_page=100", token)
    for m in milestones:
        if m["title"] == title:
            return m["number"]
    result = gh("POST", f"/repos/{owner}/{repo}/milestones", token, json={"title": title})
    print(f"  created milestone: {title} (#{result['number']})")
    return result["number"]


def create_issue(owner, repo, token, item, milestone_number):
    payload = {
        "title": item["title"],
        "body": item.get("body", ""),
        "labels": item.get("labels", []),
        "assignees": item.get("assignees", []),
        "milestone": milestone_number,
    }
    result = gh("POST", f"/repos/{owner}/{repo}/issues", token, json=payload)
    return result["number"], result["html_url"]


def create_tracking_issue(owner, repo, token, milestone_title, milestone_number, created_items):
    task_lines = "\n".join(f"- [ ] #{i['number']} {i['title']}" for i in created_items)
    body = f"Tracking issue for **{milestone_title}**.\n\n## Issues\n\n{task_lines}"
    result = gh("POST", f"/repos/{owner}/{repo}/issues", token, json={
        "title": f"[Tracking] {milestone_title}",
        "body": body,
        "labels": ["tracking"],
        "milestone": milestone_number,
    })
    return result["number"], result["html_url"]


def main():
    ap = argparse.ArgumentParser(description="Create GitHub Issues from github_backlog_input.json")
    ap.add_argument("--input", required=True, help="Path to github_backlog_input.json")
    ap.add_argument("--output", required=True, help="Path to write github_backlog_result.json")
    args = ap.parse_args()

    with open(args.input, encoding="utf-8") as f:
        plan = json.load(f)

    owner = os.environ.get("GH_OWNER") or plan.get("owner")
    repo = os.environ.get("GH_REPO") or plan.get("repo")
    if not owner or not repo:
        sys.exit("set GH_OWNER and GH_REPO env vars, or include 'owner'/'repo' in the input JSON")

    token = get_token()
    milestone_title = plan.get("milestone", "Backlog")
    items = plan.get("items", [])

    print(f"repo:      {owner}/{repo}")
    print(f"milestone: {milestone_title}")
    print(f"items:     {len(items)}")

    all_labels = {"tracking"}
    for item in items:
        all_labels.update(item.get("labels", []))

    ensure_labels(owner, repo, token, all_labels)
    milestone_number = get_or_create_milestone(owner, repo, token, milestone_title)

    result_items = []
    created_for_tracking = []
    for item in items:
        number, url = create_issue(owner, repo, token, item, milestone_number)
        print(f"  key {item['key']} -> #{number} {url}")
        result_items.append({
            "key": item["key"],
            "number": number,
            "url": url,
            "status": "created",
            "title": item["title"],
        })
        created_for_tracking.append({"number": number, "title": item["title"]})

    tracking_number, tracking_url = create_tracking_issue(
        owner, repo, token, milestone_title, milestone_number, created_for_tracking
    )
    print(f"  tracking issue -> #{tracking_number} {tracking_url}")

    result = {
        "owner": owner,
        "repo": repo,
        "tracking_issue": {"number": tracking_number, "url": tracking_url},
        "items": result_items,
    }
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)
    print(f"wrote {args.output}")


if __name__ == "__main__":
    main()
