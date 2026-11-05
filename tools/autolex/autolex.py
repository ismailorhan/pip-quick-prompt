"""Prototype: detect English words in Turkish text and respell them for a Turkish TTS.

Data: CMUdict (BSD-2-Clause) for English pronunciations, Zemberek master
dictionary (Apache-2.0) for Turkish roots.
"""

import json
import re
import sys
from collections import Counter
from pathlib import Path

DATA = Path(__file__).parent / "data"
TR_ONLY = set("çğıöşüÇĞİÖŞÜ")
LETTERS = "A-Za-zÇĞİÖŞÜçğıöşü"
TOKEN = re.compile(f"[{LETTERS}]+(?:['’][{LETTERS}]+)?")

VOWELS = {
    "AA": "o", "AE": "e", "AO": "o", "AW": "au", "AY": "ay", "EH": "e", "EY": "ey",
    "IH": "i", "IY": "i", "OW": "o", "OY": "oy", "UH": "u", "UW": "u",
}
CONSONANTS = {
    "B": "b", "CH": "ç", "D": "d", "DH": "d", "F": "f", "G": "g", "HH": "h", "JH": "c",
    "K": "k", "L": "l", "M": "m", "N": "n", "NG": "ng", "P": "p", "R": "r", "S": "s",
    "SH": "ş", "T": "t", "TH": "t", "V": "v", "W": "v", "Y": "y", "Z": "z", "ZH": "j",
}


def load_cmu():
    pron = {}
    for line in (DATA / "cmudict.dict").read_text(encoding="latin-1").splitlines():
        word, _, phones = line.partition(" ")
        if "(" in word:  # alternative pronunciations: keep the first
            continue
        pron[word] = phones.split("#")[0].split()
    return pron


SOFTEN = {"p": "b", "ç": "c", "t": "d", "k": "ğ"}


VERB_STEMS = set()
INVERSE_HARMONY = set()
CIRCUMFLEX = str.maketrans("âîû", "aiu")
TR_LOWER = str.maketrans("Iİ", "ıi")


def tr_lower(text):
    return text.translate(TR_LOWER).lower()


def load_turkish_roots():
    roots = set()
    for name in ("zemberek.dict", "z_non-tdk.dict"):
        for line in (DATA / name).read_text(encoding="utf-8").splitlines():
            if not line or line.startswith("#"):
                continue
            word = line.split(" [")[0].strip().lower().translate(CIRCUMFLEX)
            if " " in word:
                continue
            forms = [word]
            if word.endswith(("mak", "mek")) and len(word) > 4:
                forms.append(word[:-3])  # verbs are listed as infinitives: silmek -> sil
                VERB_STEMS.add(word[:-3])
            for form in list(forms):
                if form.endswith("nk"):
                    forms.append(form[:-1] + "g")  # renk -> reng(i), denk -> deng(i)
                if form[-1:] in SOFTEN:
                    soft = form[:-1] + SOFTEN[form[-1]]  # kitap -> kitab(ı), git -> gid(er)
                    forms.append(soft)
                    if form in VERB_STEMS:
                        VERB_STEMS.add(soft)  # et -> ed(en)
            roots.update(forms)
            if "InverseHarmony" in line:
                INVERSE_HARMONY.update(forms)  # rol -> roller, saat -> saatler
    roots.update("bana sana ona buna şuna kendine nere".split())  # irregular forms
    roots.update(_SUFFIXES)  # stray suffixes split off by markup: {x}'lar
    return roots


def load_turkish_names():
    # Proper nouns, person and place names: leave them to the Turkish reading,
    # but only when written capitalized ("Merge" is also a village name).
    roots = set()
    for name in ("z_proper.dict", "z_person-names.dict", "z_locations-tr.dict"):
        for line in (DATA / name).read_text(encoding="utf-8").splitlines():
            word = line.split(" [")[0].strip()
            if word and " " not in word:
                roots.add(tr_lower(word).translate(CIRCUMFLEX))
    return roots


# Common Turkish suffix morphs (all harmony variants). A word is Turkish if it
# is a known root followed by a chain of these.
_SUFFIXES = """
lar ler m ım im um üm n ın in un ün ı i u ü sı si su sü ları leri
da de ta te dan den tan ten a e ya ye na ne nda nde ndan nden ki nın nin nun nün
yla yle la le ca ce ça çe lı li lu lü lık lik luk lük sız siz suz süz
dır dir dur dür tır tir tur tür mak mek ma me arak erek yarak yerek
ar er ır ir ur ür r dı di du dü tı ti tu tü mış miş muş müş
yor ıyor iyor uyor üyor acak ecek yacak yecek abil ebil yabil yebil
ıl il ul ül t ız iz uz üz sın sin sun sün k ık ik uk ük
mız miz muz müz nız niz nuz nüz sa se ken yken ın ınız iniz unuz ünüz
ilen ılan ulan ülen an en yan yen dik dık duk dük tik tık tuk tük
mış mez maz y ş ış iş uş üş lan len laş leş al el
ıp ip up üp yıp yip madan meden ınca ince unca ünce yınca yince dıkça dikçe
""".split()
SUFFIX_CHAIN = re.compile("(?:" + "|".join(sorted(set(_SUFFIXES), key=len, reverse=True)) + ")+")


ORTHO_VOWELS = re.compile("[aeiouy]+")
SPELLING_VOWEL = {"a": "a", "e": "e", "i": "i", "o": "o", "u": "u", "y": "i"}


def respell(phones, word=""):
    # Unstressed reduced vowels (AH0, IH0) follow the spelling, as Turkish
    # speakers say them when the letter is o or i: commit -> komit, service ->
    # sörvis. Other reduced vowels stay ı: token -> tokın, google -> gugıl.
    w = word.lower()
    if re.search("[^aeiouy]e$", w):
        w = w[:-1]  # silent final e: service, release
    groups = ORTHO_VOWELS.findall(w)
    vowel_phones = [p for p in phones if p[-1] in "012"]
    aligned = len(groups) == len(vowel_phones)
    out, v = [], 0
    for i, p in enumerate(phones):
        base, stress = p.rstrip("012"), p[len(p.rstrip("012")):]
        prev = phones[i - 1].rstrip("012") if i else ""
        if stress:
            letter = SPELLING_VOWEL.get(groups[v]) if aligned else None  # single letters only: -tion stays ın
            v += 1
            if base == "ER":
                out.append("ör" if stress == "1" else "ır")
            elif base == "AH" and stress in "12":
                out.append("a")
            elif base == "AH" and stress == "0" and letter in ("o", "i"):
                out.append(letter)
            elif base == "AH":
                out.append("ı")
            else:
                out.append(VOWELS[base])
        elif base == "W" and prev in CONSONANTS:
            out.append("u")  # request -> rikuest
        else:
            out.append(CONSONANTS[base])
    text = "".join(out)
    if text.endswith("c"):
        text = text[:-1] + "ç"  # final devoicing: merge -> mörç
    return text


FRONT, BACK = set("eiöü"), set("aıou")
ENGLISH_SPELLING = re.compile("[wxq]|th|sh|ph|ck|ee|oo|ea|ou|ai|ay")


def harmonic(root, rest):
    """The first suffix vowel agrees in frontness with the root's last vowel.

    Only the first: later morphs such as -ki, -yor, -bil do not harmonize.
    """
    last = [c for c in root if c in FRONT | BACK][-1:]
    first = [c for c in rest if c in FRONT | BACK][:1]
    if not last or not first:
        return True
    front = last[0] in FRONT
    if root in INVERSE_HARMONY:
        front = not front
    return (first[0] in FRONT) == front


def english_cue(low, phones):
    """Spellings Turkish words almost never have, or a silent final e."""
    if ENGLISH_SPELLING.search(low):
        return True
    return low.endswith("e") and phones[-1].rstrip("012") in CONSONANTS


EN_LETTERS = dict(zip("abcdefghijklmnopqrstuvwxyz",
    "ey bi si di i ef ci eyç ay cey key el em en o pi kyu ar es ti yu vi dabılyu eks vay zi".split()))
TR_LETTERS = dict(zip("abcçdefgğhıijklmnoöprsştuüvyz",
    "a be ce çe de e fe ge yumuşakge he ı i je ke le me ne o ö pe re se şe te u ü ve ye ze".split()))
KEEP = object()  # an "asis" exception: leave the word as written


def spell(term, letters):
    low = term.lower() if letters is EN_LETTERS else tr_lower(term)  # English I is i, Turkish I is ı
    return " ".join(letters[c] for c in low if c in letters)


def load_exceptions(path):
    """exceptions.tr.json -> (words, phrases).

    words: {key: spoken or KEEP}; an all-capital term is keyed as written (matches
    only in capitals), any other term in lower case. phrases: [(regex, spoken)]
    for terms with a space, dot or hyphen, longest first.
    """
    words, phrases = {}, []
    for e in json.loads(Path(path).read_text(encoding="utf-8"))["entries"]:
        term, mode = e["term"], e.get("mode", "say")
        spoken = {"say": e.get("say"), "asis": KEEP, "spell": spell(term, EN_LETTERS),
                  "spell-tr": spell(term, TR_LETTERS)}[mode]
        if re.search(f"[^{LETTERS}]", term):
            body = re.escape(term)
            phrases.append((len(term), re.compile(
                f"(?<![{LETTERS}]){body}(?:['’]([{LETTERS}]+))?(?![{LETTERS}])", re.IGNORECASE), spoken))
        else:
            words[term if term.isupper() and len(term) > 1 else tr_lower(term)] = spoken
    phrases.sort(key=lambda x: -x[0])
    return words, [(rx, sp) for _, rx, sp in phrases]


class AutoLexicon:
    def __init__(self, overrides=None, exceptions=None):
        self.cmu = load_cmu()
        self.tr = load_turkish_roots()
        self.names = load_turkish_names()
        self.overrides = {k.lower(): v for k, v in (overrides or {}).items()}
        self.words, self.phrases = load_exceptions(exceptions) if exceptions else ({}, [])

    def exception(self, base):
        if base in self.words:
            return self.words[base]
        if not base.isupper() or len(base) == 1:
            return self.words.get(tr_lower(base))
        return None

    def classify(self, base):
        """Return the spoken form if `base` is an English word, else None."""
        low = base.lower()
        tlow = tr_lower(base).translate(CIRCUMFLEX)
        if low in self.overrides:
            return self.overrides[low]
        if any(c in TR_ONLY for c in base):
            return None
        if len(base) < 3:
            return None
        if base.isupper() and len(base) <= 5:
            return None  # acronym: Turkish or English is ambiguous, leave to overrides
        if low in self.tr or tlow in self.tr:
            return None
        if base[0].isupper() and tlow in self.names:
            return None
        if self.root_plus_vowel(tlow):
            return None  # siz+e, kim+e, link+e, set+i
        if low in self.cmu and english_cue(low, self.cmu[low]):
            return respell(self.cmu[low], low)
        # A one-letter "suffix" is too weak to beat an English entry: user = use + r?
        if self.is_turkish(tlow, min_suffix=2 if low in self.cmu else 1):
            return None
        if low in self.cmu:
            return respell(self.cmu[low], low)
        return self.compound(low)

    def root_plus_vowel(self, low):
        root, last = low[:-1], low[-1:]
        return len(root) >= 3 and last in "aeıiuü" and root in self.tr and harmonic(root, last)

    def is_turkish(self, low, min_suffix=1):
        if low in self.tr:
            return True
        for cut in range(len(low) - min_suffix, 1, -1):  # 3+ letter roots, or 2-letter verbs (ol-arak)
            root, rest = low[:cut], low[cut:]
            if (len(root) >= 3 or root in VERB_STEMS) and root in self.tr and SUFFIX_CHAIN.fullmatch(rest) and harmonic(root, rest):
                return True
        return False

    def compound(self, low):
        # frontend = front + end, onboarding = on + boarding
        for cut in range(len(low) - 3, 2, -1):
            a, b = low[:cut], low[cut:]
            if a in self.cmu and b in self.cmu and a not in self.tr and len(b) >= 3:
                return respell(self.cmu[a], a) + respell(self.cmu[b], b)
        return None

    def convert_token(self, token):
        parts = re.split("['’]", token, maxsplit=1)
        base, suffix = parts[0], (parts[1] if len(parts) > 1 else "")
        sep = len(parts) > 1
        listed = self.exception(base)
        if listed is KEEP:
            return token, False
        if listed is not None:
            return listed + suffix, "listed"
        spoken = self.classify(base)
        if spoken is not None:
            return spoken + suffix, True
        if not sep and not self.is_turkish(tr_lower(base).translate(CIRCUMFLEX)):
            # English word + Turkish suffix written without an apostrophe:
            # workspaceteki, emojiyi. The suffix must agree with the spoken form.
            low = tr_lower(base)
            # Only a whole dictionary or listed word as the stem, and a 2+ letter suffix:
            # a one-letter one or a compound split finds English in Turkish (ornek, logları).
            for cut in range(len(base) - 2, 3, -1):
                stem, rest = base[:cut], low[cut:]
                if not SUFFIX_CHAIN.fullmatch(rest):
                    continue
                listed = self.exception(stem)
                if listed is None and stem.lower() not in self.cmu:
                    continue
                s = (None if listed is KEEP else listed) if listed is not None else self.classify(stem)
                if s is not None and harmonic(tr_lower(s), rest):
                    return s + rest, True
        if not sep:
            # Verb made from an English stem: commitledim, deploylamak, pushladım.
            for cut in range(len(base) - 2, 3, -1):
                stem, rest = base[:cut], base[cut:]
                if rest[:2].lower() in ("le", "la") and not self.is_turkish(base.lower()):
                    s = self.classify(stem)
                    if s is not None:
                        return s + rest, True
        return token, False

    def apply(self, text):
        changed, held = [], []

        def sentence_start(start, source):
            return not source[:start].strip() or source[:start].rstrip()[-1] in ".!?:;\n"

        # Phrases first (Next.js, stand-up), held aside so the word pass leaves them alone.
        for rx, spoken in self.phrases:
            def hold(m, spoken=spoken, source=text):
                if spoken is KEEP:
                    new = m.group(0)
                else:
                    new = spoken + (m.group(1) or "")
                    if sentence_start(m.start(), source):
                        new = new[0].upper() + new[1:]
                    changed.append((m.group(0), new))
                held.append(new)
                return f"\uE000{len(held) - 1}\uE001"
            text = rx.sub(hold, text)

        def repl(m):
            new, hit = self.convert_token(m.group(0))
            if hit:
                # Listed spellings keep their own case, except at a sentence start;
                # automatic ones keep the word's capital.
                cap = sentence_start(m.start(), text) if hit == "listed" else m.group(0)[0].isupper()
                if cap:
                    new = new[0].upper() + new[1:]
                changed.append((m.group(0), new))
            return new

        text = TOKEN.sub(repl, text)
        text = re.sub("\uE000(\\d+)\uE001", lambda m: held[int(m.group(1))], text)
        return text, changed


if __name__ == "__main__":
    EXC = Path(__file__).parent / "exceptions.tr.json"
    lex = AutoLexicon(exceptions=EXC)
    mode = sys.argv[1]
    if mode == "words":
        for w in sys.argv[2:]:
            print(w, "->", lex.convert_token(w)[0])
    elif mode == "text":
        for line in sys.stdin.read().splitlines():
            print(lex.apply(line)[0])
    elif mode == "corpus":
        hits = Counter()
        for path in sys.argv[2:]:
            for line in Path(path).read_text(encoding="utf-8").splitlines():
                # Drop ICU message syntax: {count, plural, one {...} other {...}}
                line = re.sub(r"\{\w+(?:,\s*\w+,?)?", " ", line)
                line = re.sub(r"\b(?:zero|one|two|few|many|other)\s*\{", " ", line)
                line = re.sub(r"<[^>]+>", " ", line)
                for old, _ in lex.apply(line)[1]:
                    hits[old] += 1
        for word, n in hits.most_common():
            print(n, word, "->", lex.convert_token(word)[0])
