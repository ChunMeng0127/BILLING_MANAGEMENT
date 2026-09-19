#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import subprocess
from datetime import datetime, timezone

PATTERN = re.compile(r"^billing-(\d{8}T\d{6}Z)\.dump$")


def run(*args: str) -> str:
    return subprocess.check_output(args, text=True)


def parse_backup(name: str):
    match = PATTERN.match(name)
    if not match:
        return None
    return datetime.strptime(match.group(1), "%Y%m%dT%H%M%SZ").replace(tzinfo=timezone.utc)


def select_keep(backups, daily: int, weekly: int, monthly: int) -> set[str]:
    keep: set[str] = set()
    seen_days: set[str] = set()
    seen_weeks: set[str] = set()
    seen_months: set[str] = set()

    for stamp, name in backups:
        day = stamp.strftime("%Y-%m-%d")
        if len(seen_days) < daily and day not in seen_days:
            keep.add(name)
            seen_days.add(day)

    for stamp, name in backups:
        iso_year, iso_week, _ = stamp.isocalendar()
        week = f"{iso_year:04d}-W{iso_week:02d}"
        if len(seen_weeks) < weekly and week not in seen_weeks:
            keep.add(name)
            seen_weeks.add(week)

    for stamp, name in backups:
        month = stamp.strftime("%Y-%m")
        if len(seen_months) < monthly and month not in seen_months:
            keep.add(name)
            seen_months.add(month)

    return keep


def main() -> int:
    parser = argparse.ArgumentParser(description="Apply Billing Control retention to an rclone remote.")
    parser.add_argument("remote")
    parser.add_argument("--config", required=True)
    parser.add_argument("--daily", type=int, default=14)
    parser.add_argument("--weekly", type=int, default=8)
    parser.add_argument("--monthly", type=int, default=12)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    if min(args.daily, args.weekly, args.monthly) < 0:
        parser.error("retention counts cannot be negative")

    listing = run("rclone", "--config", args.config, "lsf", args.remote, "--files-only")
    names = {line.strip().rstrip("/") for line in listing.splitlines() if line.strip()}
    parsed = []
    for name in names:
        stamp = parse_backup(name)
        if stamp is not None:
            parsed.append((stamp, name))
    parsed.sort(key=lambda item: item[0], reverse=True)

    keep = select_keep(parsed, args.daily, args.weekly, args.monthly)
    expired = [name for _, name in parsed if name not in keep]
    print(f"rclone_retention_mode={'apply' if args.apply else 'dry-run'} backups={len(parsed)} keep={len(keep)} expire={len(expired)}")

    base = args.remote.rstrip("/")
    for name in expired:
        print(f"expire={name}")
        if not args.apply:
            continue
        subprocess.check_call(["rclone", "--config", args.config, "deletefile", f"{base}/{name}"])
        sidecar = f"{name}.sha256"
        if sidecar in names:
            subprocess.check_call(["rclone", "--config", args.config, "deletefile", f"{base}/{sidecar}"])

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
