"""Copies the ring page from tools/pip-ring-groups-lab.html into native/macos/App/Ring.swift (ringPageHTML),
leaving out the lab's fake desktop and mocked app. Run after changing the lab: python3 -I tools/ring-page-sync.py"""
import pathlib, re, sys

root = pathlib.Path(__file__).resolve().parent.parent
lab = (root / "tools/pip-ring-groups-lab.html").read_text()
page = re.sub(r"/\* Lab only:.*?(?=\.hintbar \{)", "", lab, flags=re.S)
page = re.sub(r"\n/\* ---- Lab only:.*?(?=</script>)", "\n", page, flags=re.S)
if "Lab only" in page or '"""#' in page:
    sys.exit("could not strip the lab parts")
swift_path = root / "native/macos/App/Ring.swift"
swift = swift_path.read_text()
start = swift.index('let ringPageHTML = #"""\n') + len('let ringPageHTML = #"""\n')
end = swift.index('"""#', start)
swift_path.write_text(swift[:start] + page.rstrip("\n") + "\n" + swift[end:])
print("Ring.swift: page updated,", page.count("\n"), "lines")
