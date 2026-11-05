"""Build the English lexicon the Kokoro voice reads (src/QuickPrompt.Voice/Data/en-lexicon.txt.gz).

Lines "word<TAB>phonemes" in Kokoro's (misaki's) phoneme alphabet: misaki's US gold and silver lexicons
(Apache-2.0, hexgrad/misaki), then CMUdict (BSD-2-Clause) converted for the words those lack. No eSpeak.

python3 -I build_en_lexicon.py <us_gold.json> <us_silver.json> <cmudict.dict>
"""
import gzip, json, sys
from pathlib import Path

gold, silver, cmu_path = sys.argv[1:4]
V = {"AA": "ɑ", "AE": "æ", "AH": "ʌ", "AO": "ɔ", "AW": "W", "AY": "I", "EH": "ɛ", "ER": "ɜɹ", "EY": "A", "IH": "ɪ",
     "IY": "i", "OW": "O", "OY": "Y", "UH": "ʊ", "UW": "u"}
REDUCED = {"AH": "ə", "ER": "əɹ"}
C = {"B": "b", "CH": "ʧ", "D": "d", "DH": "ð", "F": "f", "G": "ɡ", "HH": "h", "JH": "ʤ", "K": "k", "L": "l", "M": "m",
     "N": "n", "NG": "ŋ", "P": "p", "R": "ɹ", "S": "s", "SH": "ʃ", "T": "t", "TH": "θ", "V": "v", "W": "w", "Y": "j",
     "Z": "z", "ZH": "ʒ"}


def from_cmu(phones):
    out = []
    for p in phones:
        base, st = p.rstrip("012"), p[len(p.rstrip("012")):]
        if base in V:
            v = REDUCED.get(base, V[base]) if st == "0" else V[base]
            out.append(("ˈ" if st == "1" else "ˌ" if st == "2" else "") + v)
        else:
            out.append(C[base])
    return "".join(out)


lex = {}
for path in (silver, gold):            # gold last: it wins
    for word, e in json.loads(Path(path).read_text(encoding="utf-8")).items():
        e = e.get("DEFAULT") if isinstance(e, dict) else e
        if e:
            lex[word] = e
added = 0
for line in Path(cmu_path).read_text(encoding="latin-1").splitlines():
    word, _, phones = line.partition(" ")
    if "(" in word or not word.isalpha() or word in lex or word.capitalize() in lex:
        continue
    try:
        lex[word] = from_cmu(phones.split("#")[0].split())
        added += 1
    except KeyError:
        pass
out = Path(__file__).resolve().parents[2] / "src" / "QuickPrompt.Voice" / "Data" / "en-lexicon.txt.gz"
body = "\n".join(f"{w}\t{p}" for w, p in sorted(lex.items()) if "\t" not in w and "\n" not in w).encode()
out.write_bytes(gzip.compress(body, 9, mtime=0))
print(f"{len(lex)} words ({added} from CMUdict), {len(body) / 1e6:.1f} MB raw, {out.stat().st_size / 1e6:.2f} MB gzip -> {out}")
