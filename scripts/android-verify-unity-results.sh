#!/usr/bin/env bash
set -Eeuo pipefail

results_directory="${1:?usage: android-verify-unity-results.sh RESULTS_DIRECTORY MODE EXPECTED_TESTS}"
mode="${2:?missing mode}"
expected_tests="${3:?missing expected test count}"

python3 - "$results_directory" "$mode" "$expected_tests" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

root_dir = pathlib.Path(sys.argv[1])
mode = sys.argv[2]
expected = int(sys.argv[3])
xml_files = sorted(root_dir.rglob("*.xml"))
if not xml_files:
    raise SystemExit(f"No NUnit/JUnit XML files found under {root_dir}")

observed = []

def integer_attribute(root, *names):
    for name in names:
        value = root.attrib.get(name)
        if value not in (None, ""):
            return int(value)
    return 0

for xml_file in xml_files:
    try:
        root = ET.parse(xml_file).getroot()
    except ET.ParseError:
        continue
    tag = root.tag.rsplit("}", 1)[-1]
    if tag not in {"test-run", "test-results", "testsuites", "testsuite"}:
        continue
    total_value = root.attrib.get("total") or root.attrib.get("tests") or root.attrib.get("testcasecount")
    if total_value is None:
        continue
    total = int(total_value)
    failed = integer_attribute(root, "failed", "failures")
    errors = integer_attribute(root, "errors")
    skipped = integer_attribute(root, "skipped")
    inconclusive = integer_attribute(root, "inconclusive")
    not_run = integer_attribute(root, "not-run", "notrun", "notRun")
    passed_value = root.attrib.get("passed")
    passed = int(passed_value) if passed_value is not None else (
        total - failed - errors - skipped - inconclusive - not_run
    )
    result = (root.attrib.get("result") or "").strip().lower()
    priority = {"test-run": 4, "test-results": 3, "testsuites": 2, "testsuite": 1}[tag]
    observed.append({
        "priority": priority,
        "tag": tag,
        "total": total,
        "passed": passed,
        "failed": failed,
        "errors": errors,
        "skipped": skipped,
        "inconclusive": inconclusive,
        "not_run": not_run,
        "result": result,
        "source": xml_file,
    })

if not observed:
    raise SystemExit(f"No recognized test-run roots found under {root_dir}")

priority = max(item["priority"] for item in observed)
complete_roots = [item for item in observed if item["priority"] == priority]
totals = {item["total"] for item in complete_roots}
if len(totals) != 1:
    details = ", ".join(f"{item['source']}={item['total']}" for item in complete_roots)
    raise SystemExit(f"{mode} has inconsistent complete test-run roots: {details}")

for item in complete_roots:
    non_passed = (
        item["failed"] + item["errors"] + item["skipped"]
        + item["inconclusive"] + item["not_run"]
    )
    if item["result"] and item["result"] not in {"passed", "success"}:
        raise SystemExit(
            f"{mode} root reports result={item['result']} ({item['source']})"
        )
    if non_passed:
        raise SystemExit(
            f"{mode} is not all-pass: failed={item['failed']}, errors={item['errors']}, "
            f"skipped={item['skipped']}, inconclusive={item['inconclusive']}, "
            f"not-run={item['not_run']} ({item['source']})"
        )
    if item["passed"] != item["total"]:
        raise SystemExit(
            f"{mode} passed/total mismatch: {item['passed']}/{item['total']} ({item['source']})"
        )

representative = complete_roots[0]
print(
    f"{mode}: passed {representative['passed']}/{representative['total']}; "
    f"skipped=0, inconclusive=0, not-run=0 ({representative['source']})"
)
if representative["total"] != expected:
    raise SystemExit(
        f"{mode} ran {representative['total']} tests; expected exactly {expected}"
    )
PY
