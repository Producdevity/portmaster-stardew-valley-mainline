#!/usr/bin/env python3

import csv
import statistics
import sys
from pathlib import Path


def read_csv(path):
    if not path.exists():
        return []

    with path.open(newline="") as handle:
        return list(csv.DictReader(handle))


def percentile(values, percentile_value):
    if not values:
        return 0.0

    ordered = sorted(values)
    index = round((len(ordered) - 1) * percentile_value)
    return ordered[index]


def summarize_profile(profile_dir):
    profile_dir = Path(profile_dir)
    frame_rows = read_csv(profile_dir / "perf-latest.csv")
    content_rows = read_csv(profile_dir / "content-load-latest.csv")

    fps_values = []
    for row in frame_rows:
        try:
            fps_values.append(float(row["fps"]))
        except (KeyError, TypeError, ValueError):
            continue

    content_loads = []
    content_cache_hits = 0
    for row in content_rows:
        try:
            elapsed = float(row["elapsed_ms"])
        except (KeyError, TypeError, ValueError):
            continue

        content_loads.append((elapsed, row.get("asset_name", "")))
        if row.get("content_cache_hit") == "1":
            content_cache_hits += 1

    top_loads = sorted(content_loads, reverse=True)[:5]

    return {
        "name": profile_dir.name,
        "frame_windows": len(fps_values),
        "avg_fps": statistics.mean(fps_values) if fps_values else 0.0,
        "median_fps": statistics.median(fps_values) if fps_values else 0.0,
        "min_fps": min(fps_values) if fps_values else 0.0,
        "p10_fps": percentile(fps_values, 0.10),
        "windows_below_30": sum(1 for value in fps_values if value < 30.0),
        "windows_below_20": sum(1 for value in fps_values if value < 20.0),
        "content_loads": len(content_loads),
        "content_total_ms": sum(value for value, _ in content_loads),
        "content_over_100": sum(1 for value, _ in content_loads if value > 100.0),
        "content_cache_hits": content_cache_hits,
        "top_loads": top_loads,
    }


def format_float(value):
    return f"{value:.2f}"


def print_summary(summaries):
    print("| Profile | FPS avg | FPS median | FPS p10 | FPS min | <30 FPS windows | <20 FPS windows | Content total ms | Loads >100ms | Cache hits |")
    print("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for item in summaries:
        print(
            "| {name} | {avg} | {median} | {p10} | {minimum} | {below30} | {below20} | {content_total} | {over100} | {cache_hits}/{loads} |".format(
                name=item["name"],
                avg=format_float(item["avg_fps"]),
                median=format_float(item["median_fps"]),
                p10=format_float(item["p10_fps"]),
                minimum=format_float(item["min_fps"]),
                below30=item["windows_below_30"],
                below20=item["windows_below_20"],
                content_total=format_float(item["content_total_ms"]),
                over100=item["content_over_100"],
                cache_hits=item["content_cache_hits"],
                loads=item["content_loads"],
            )
        )

    for item in summaries:
        print()
        print(f"Top content loads for {item['name']}:")
        if not item["top_loads"]:
            print("- none")
            continue

        for elapsed, asset_name in item["top_loads"]:
            print(f"- {elapsed:.2f} ms {asset_name}")


def main(argv):
    if len(argv) < 2:
        print("Usage: scripts/compare-profiles.py PROFILE_DIR [PROFILE_DIR ...]", file=sys.stderr)
        return 2

    summaries = [summarize_profile(path) for path in argv[1:]]
    print_summary(summaries)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
