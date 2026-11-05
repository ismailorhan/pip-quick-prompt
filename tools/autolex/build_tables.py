"""Precompute the tables the popup page needs to run autolex.js, and report the sizes.

Run after fetch_data.sh. Writes src/QuickPrompt.Core/Voice/autolex-tr.json.gz (embedded in
the engine, served to the page) and out/autolex-tr.all.json (for check_js.js). Fields:
  en      {word: respelling}  CMU words, respelled; a leading "!" marks an English spelling cue
  tr      Turkish roots (with softened forms), space-separated
  verbs   Turkish verb stems; inverse: roots with inverse harmony
  names   Turkish proper, person and place names (match only when capitalised)
  exc     exceptions.tr.json entries, resolved (spell modes spelled out)
"""
import gzip
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).parent
sys.path.insert(0, str(HERE))
import autolex  # noqa: E402

lex = autolex.AutoLexicon(exceptions=HERE / "exceptions.tr.json")
plain = re.compile("^[a-z]+$")
en = {}
for word, phones in lex.cmu.items():
    if not plain.match(word):
        continue  # short words and Turkish roots stay: compounds use them (on + boarding)
    try:
        say = autolex.respell(phones, word)
    except KeyError:
        continue
    en[word] = ("!" if autolex.english_cue(word, phones) else "") + say

exc = {}
for key, spoken in lex.words.items():
    exc[key] = None if spoken is autolex.KEEP else spoken
phrases = [[rx.pattern, None if sp is autolex.KEEP else sp] for rx, sp in lex.phrases]
data = {
    "en": en,
    "tr": " ".join(sorted(lex.tr)),
    "verbs": " ".join(sorted(autolex.VERB_STEMS)),
    "inverse": " ".join(sorted(autolex.INVERSE_HARMONY)),
    "names": " ".join(sorted(lex.names)),
    "exc": exc,
    "phrases": phrases,
}
out = HERE / "out"
out.mkdir(exist_ok=True)
raw = json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode()
(out / "autolex-tr.all.json").write_bytes(raw)
ship = HERE.parent.parent / "src" / "QuickPrompt.Core" / "Voice" / "autolex-tr.json.gz"
ship.write_bytes(gzip.compress(raw, 9, mtime=0))  # mtime=0: the same data gives the same file
print(f"entries: en {len(en)}, tr {len(lex.tr)}, names {len(lex.names)}, exc {len(exc)}")
print(f"raw {len(raw) / 1e6:.2f} MB, shipped gzip {ship.stat().st_size / 1e6:.2f} MB -> {ship}")
