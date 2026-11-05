// autolex: finds English words in Turkish text and respells them for a Turkish voice
// ("Service Layer'a" -> "Sörvis Leyıra"). The rules of tools/autolex/autolex.py over the
// tables tools/autolex/build_tables.py writes (Voice/autolex-tr.json.gz, served by UiServer).
// Runs in the page; nothing is sent anywhere. Data: CMUdict (BSD-2-Clause), Zemberek (Apache-2.0).
//   const lex = new AutoLex(await (await fetch("autolex-tr.json")).json());
//   lex.apply("Service layer'ı kurdum")  ->  "Sörvis leyırı kurdum"

const LETTERS = "A-Za-zÇĞİÖŞÜçğıöşü";
const TOKEN = new RegExp(`[${LETTERS}]+(?:['’][${LETTERS}]+)?`, "g");
const TR_ONLY = /[çğıöşüÇĞİÖŞÜ]/;
const FRONT = new Set("eiöü"), BACK = new Set("aıou");
const SUFFIXES = `lar ler m ım im um üm n ın in un ün ı i u ü sı si su sü ları leri
da de ta te dan den tan ten a e ya ye na ne nda nde ndan nden ki nın nin nun nün
yla yle la le ca ce ça çe lı li lu lü lık lik luk lük sız siz suz süz
dır dir dur dür tır tir tur tür mak mek ma me arak erek yarak yerek
ar er ır ir ur ür r dı di du dü tı ti tu tü mış miş muş müş
yor ıyor iyor uyor üyor acak ecek yacak yecek abil ebil yabil yebil
ıl il ul ül t ız iz uz üz sın sin sun sün k ık ik uk ük
mız miz muz müz nız niz nuz nüz sa se ken yken ın ınız iniz unuz ünüz
ilen ılan ulan ülen an en yan yen dik dık duk dük tik tık tuk tük
mış mez maz y ş ış iş uş üş lan len laş leş al el
ıp ip up üp yıp yip madan meden ınca ince unca ünce yınca yince dıkça dikçe`.split(/\s+/);
const SUFFIX_CHAIN = new RegExp(`^(?:${[...new Set(SUFFIXES)].sort((a, b) => b.length - a.length).join("|")})+$`);

const trLower = (s) => s.replace(/I/g, "ı").replace(/İ/g, "i").toLowerCase();
const noCircumflex = (s) => s.replace(/â/g, "a").replace(/î/g, "i").replace(/û/g, "u");
const isUpper = (s) => s === s.toUpperCase() && s !== s.toLowerCase();
const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);

class AutoLex {
  constructor(t) {
    this.en = new Map(Object.entries(t.en));
    this.tr = new Set(t.tr.split(" "));
    this.verbs = new Set(t.verbs.split(" "));
    this.inverse = new Set(t.inverse.split(" "));
    this.names = new Set(t.names.split(" "));
    this.exc = new Map(Object.entries(t.exc));         // null = leave as written
    this.phrases = t.phrases.map(([src, say]) => [new RegExp(src, "gi"), say]);
  }

  cmu(w) { return this.en.has(w); }
  say(w) { const s = this.en.get(w); return s.startsWith("!") ? s.slice(1) : s; }
  cue(w) { return this.en.get(w).startsWith("!"); }

  harmonic(root, rest) {
    const last = [...root].filter((c) => FRONT.has(c) || BACK.has(c)).pop();
    const first = [...rest].find((c) => FRONT.has(c) || BACK.has(c));
    if (!last || !first) return true;
    let front = FRONT.has(last);
    if (this.inverse.has(root)) front = !front;
    return FRONT.has(first) === front;
  }

  // undefined = not listed, null = listed as "leave it", string = listed reading
  exception(base) {
    if (this.exc.has(base)) return this.exc.get(base);
    if (!isUpper(base) || base.length === 1) {
      const k = trLower(base);
      if (this.exc.has(k)) return this.exc.get(k);
    }
    return undefined;
  }

  classify(base) {
    const low = base.toLowerCase();
    const tlow = noCircumflex(trLower(base));
    if (TR_ONLY.test(base)) return null;
    if (base.length < 3) return null;
    if (isUpper(base) && base.length <= 5) return null;
    if (this.tr.has(low) || this.tr.has(tlow)) return null;
    if (isUpper(base[0]) && this.names.has(tlow)) return null;
    if (this.rootPlusVowel(tlow)) return null;
    if (this.cmu(low) && this.cue(low)) return this.say(low);
    if (this.isTurkish(tlow, this.cmu(low) ? 2 : 1)) return null;
    if (this.cmu(low)) return this.say(low);
    return this.compound(low);
  }

  rootPlusVowel(low) {
    const root = low.slice(0, -1), last = low.slice(-1);
    return root.length >= 3 && "aeıiuü".includes(last) && last !== "" && this.tr.has(root) && this.harmonic(root, last);
  }

  isTurkish(low, minSuffix = 1) {
    if (this.tr.has(low)) return true;
    for (let cut = low.length - minSuffix; cut > 1; cut--) {
      const root = low.slice(0, cut), rest = low.slice(cut);
      if ((root.length >= 3 || this.verbs.has(root)) && this.tr.has(root) && SUFFIX_CHAIN.test(rest) && this.harmonic(root, rest)) return true;
    }
    return false;
  }

  compound(low) {
    for (let cut = low.length - 3; cut > 2; cut--) {
      const a = low.slice(0, cut), b = low.slice(cut);
      if (this.cmu(a) && this.cmu(b) && !this.tr.has(a) && b.length >= 3) return this.say(a) + this.say(b);
    }
    return null;
  }

  // -> [text, hit]: hit is false, true (automatic) or "listed"
  convertToken(token) {
    const i = token.search(/['’]/);
    const base = i < 0 ? token : token.slice(0, i);
    const suffix = i < 0 ? "" : token.slice(i + 1);
    const sep = i >= 0;
    let listed = this.exception(base);
    if (listed === null) return [token, false];
    if (listed !== undefined) return [listed + suffix, "listed"];
    const spoken = this.classify(base);
    if (spoken !== null) return [spoken + suffix, true];
    if (!sep && !this.isTurkish(noCircumflex(trLower(base)))) {
      const low = trLower(base);
      for (let cut = base.length - 2; cut > 3; cut--) {
        const stem = base.slice(0, cut), rest = low.slice(cut);
        if (!SUFFIX_CHAIN.test(rest)) continue;
        listed = this.exception(stem);
        if (listed === undefined && !this.cmu(stem.toLowerCase())) continue;
        const s = listed !== undefined ? listed : this.classify(stem);
        if (s !== null && this.harmonic(trLower(s), rest)) return [s + rest, true];
      }
    }
    if (!sep) {
      for (let cut = base.length - 2; cut > 3; cut--) {
        const stem = base.slice(0, cut), rest = base.slice(cut);
        if (["le", "la"].includes(rest.slice(0, 2).toLowerCase()) && !this.isTurkish(base.toLowerCase())) {
          const s = this.classify(stem);
          if (s !== null) return [s + rest, true];
        }
      }
    }
    return [token, false];
  }

  apply(text) {
    const held = [];
    const sentenceStart = (at, source) => {
      const before = source.slice(0, at).trimEnd();
      return before === "" || ".!?:;\n".includes(before.slice(-1));
    };
    for (const [rx, say] of this.phrases) {
      const source = text;
      text = text.replace(rx, (m, suffix, at) => {
        let out = m;
        if (say !== null) {
          out = say + (suffix || "");
          if (sentenceStart(at, source)) out = cap(out);
        }
        held.push(out);
        return `${held.length - 1}`;
      });
    }
    const source = text;
    text = text.replace(TOKEN, (m, at) => {
      let [out, hit] = this.convertToken(m);
      if (hit && (hit === "listed" ? sentenceStart(at, source) : isUpper(m[0]))) out = cap(out);
      return out;
    });
    return text.replace(/(\d+)/g, (_, n) => held[+n]);
  }
}

if (typeof module !== "undefined") module.exports = { AutoLex };
else if (typeof window !== "undefined") window.AutoLex = AutoLex;
