"""Fail-closed coverage gate for merged Cobertura reports; no third-party packages.

Line hits are unioned across every report, so an assembly exercised by several test projects
is measured once. Thresholds are per assembly (``--minimum ClearCase.Domain=80``) plus an
optional overall minimum (``--overall 60``).
"""
import argparse
import glob
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def merge(reports: list[Path]) -> dict[str, tuple[int, int]]:
    """Return {assembly: (covered_lines, total_lines)} unioned across all reports."""
    hits: dict[tuple[str, str, str], int] = {}
    for report in reports:
        for package in ET.parse(report).getroot().iter("package"):
            for cls in package.iter("class"):
                for line in cls.iter("line"):
                    key = (package.get("name", ""), cls.get("filename", ""), line.get("number", ""))
                    hits[key] = hits.get(key, 0) + int(line.get("hits", "0"))
    totals: dict[str, list[int]] = {}
    for (assembly, _, _), count in hits.items():
        covered_total = totals.setdefault(assembly, [0, 0])
        covered_total[1] += 1
        covered_total[0] += 1 if count > 0 else 0
    return {name: (covered, total) for name, (covered, total) in totals.items()}


def percent(covered: int, total: int) -> float:
    return 100.0 * covered / total if total else 0.0


def verify(coverage: dict[str, tuple[int, int]], minimums: dict[str, float], overall: float) -> list[str]:
    errors = []
    for assembly, minimum in minimums.items():
        if assembly not in coverage:
            errors.append(f"No coverage data for {assembly}")
            continue
        actual = percent(*coverage[assembly])
        if actual < minimum:
            errors.append(f"{assembly} line coverage {actual:.1f}% is below the {minimum:.1f}% minimum")
    covered = sum(value[0] for value in coverage.values())
    total = sum(value[1] for value in coverage.values())
    if total == 0:
        errors.append("Coverage reports contain no lines")
    elif percent(covered, total) < overall:
        errors.append(f"Overall line coverage {percent(covered, total):.1f}% is below the {overall:.1f}% minimum")
    return errors


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", default="TestResults", help="Directory searched for coverage.cobertura.xml")
    parser.add_argument("--minimum", action="append", default=[], metavar="ASSEMBLY=PERCENT")
    parser.add_argument("--overall", type=float, default=0.0)
    args = parser.parse_args(argv)

    reports = [Path(path) for path in glob.glob(f"{args.results}/**/coverage.cobertura.xml", recursive=True)]
    if not reports:
        print("ERROR: no coverage.cobertura.xml reports found")
        return 1
    minimums = {}
    for item in args.minimum:
        name, _, value = item.partition("=")
        minimums[name] = float(value)

    coverage = merge(reports)
    for assembly, (covered, total) in sorted(coverage.items()):
        print(f"{assembly}: {percent(covered, total):.1f}% ({covered}/{total} lines)")
    errors = verify(coverage, minimums, args.overall)
    for error in errors:
        print(f"ERROR: {error}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
