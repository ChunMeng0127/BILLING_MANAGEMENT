#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
from datetime import datetime, timezone
from pathlib import Path

PATTERN = re.compile(r"^billing-(\d{8}T\d{6}Z)\.dump$")


def parse_backup(path: Path):
    match = PATTERN.match(path.name)
    if not match:
        return None
    return datetime.strptime(match.group(1), "%Y%m%dT%H%M%SZ").replace(tzinfo=timezone.utc)


def select_keep(backups: list[tuple[datetime, Path]], daily: int, weekly: int, monthly: int) -> set[Path]:
    keep: set[Path] = set()
    seen_days: set[str] = set()
    seen_weeks: set[str] = set()
    seen_months: set[str] = set()

    for stamp, path in backups:
        day = stamp.strftime("%Y-%m-%d")
        if len(seen_days) < daily and day not in seen_days:
            keep.add(path)
            seen_days.add(day)

    for stamp, path in backups:
        iso_year, iso_week, _ = stamp.isocalendar()
        week = f"{iso_year:04d}-W{iso_week:02d}"
        if len(seen_weeks) < weekly and week not in seen_weeks:
            keep.add(path)
            seen_weeks.add(week)

    for stamp, path in backups:
        month = stamp.strftime("%Y-%m")
        if len(seen_months) < monthly and month not in seen_months:
            keep.add(path)
            seen_months.add(month)

    return keep


def main() -> int:
    parser = argparse.ArgumentParser(description="Apply Billing Control backup retention.")
    parser.add_argument("directory", type=Path)
    parser.add_argument("--daily", type=int, default=14)
    parser.add_argument("--weekly", type=int, default=8)
    parser.add_argument("--monthly", type=int, default=12)
    parser.add_argument("--apply", action="store_true", help="Delete expired backups; default is dry-run.")
    args = parser.parse_args()

    if min(args.daily, args.weekly, args.monthly) < 0:
        parser.error("retention counts cannot be negative")
    if not args.directory.is_dir():
        parser.error(f"directory does not exist: {args.directory}")

    parsed = []
    for path in args.directory.iterdir():
        stamp = parse_backup(path)
        if stamp is not None:
            parsed.append((stamp, path))
    parsed.sort(key=lambda item: item[0], reverse=True)

    keep = select_keep(parsed, args.daily, args.weekly, args.monthly)
    expired = [path for _, path in parsed if path not in keep]

    print(f"retention_mode={'apply' if args.apply else 'dry-run'} backups={len(parsed)} keep={len(keep)} expire={len(expired)}")
    for path in expired:
        print(f"expire={path.name}")
        if args.apply:
            path.unlink()
            sidecar = Path(f"{path}.sha256")
            if sidecar.exists():
                sidecar.unlink()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
