// Equivalence check: autolex.js over the precomputed tables must give exactly what autolex.py gives.
//   python3 -I autolex.py text < lines.txt > py.txt
//   node check_js.js lines.txt py.txt
const fs = require("fs");
const path = require("path");
const { AutoLex } = require("../../src/QuickPrompt.Core/Voice/autolex.js");
const [lines, expected] = process.argv.slice(2).map((f) => fs.readFileSync(f, "utf8").split("\n"));
let t = process.hrtime.bigint();
const lex = new AutoLex(JSON.parse(fs.readFileSync(path.join(__dirname, "out/autolex-tr.all.json"), "utf8")));
const loadMs = Number(process.hrtime.bigint() - t) / 1e6;
t = process.hrtime.bigint();
const got = lines.map((l) => lex.apply(l));
const runMs = Number(process.hrtime.bigint() - t) / 1e6;
let bad = 0;
got.forEach((g, i) => {
  if (g !== expected[i] && bad++ < 20) console.log(`line ${i + 1}\n  py ${expected[i]}\n  js ${g}`);
});
const words = lines.join(" ").split(/\s+/).filter(Boolean).length;
console.log(`${lines.length - bad}/${lines.length} lines identical; load ${loadMs.toFixed(0)} ms; ${words} words in ${runMs.toFixed(0)} ms`);
process.exit(bad ? 1 : 0);
