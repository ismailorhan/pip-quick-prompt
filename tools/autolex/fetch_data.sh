#!/bin/sh
# Downloads the dictionaries autolex.py reads into ./data (not committed).
# CMUdict: BSD-2-Clause. Zemberek: Apache-2.0.
set -e
cd "$(dirname "$0")" && mkdir -p data && cd data
curl -fsSLo cmudict.dict https://raw.githubusercontent.com/cmusphinx/cmudict/master/cmudict.dict
curl -fsSLo cmudict.LICENSE https://raw.githubusercontent.com/cmusphinx/cmudict/master/LICENSE
Z=https://raw.githubusercontent.com/ahmetaa/zemberek-nlp/master
curl -fsSLo zemberek.LICENSE $Z/LICENSE
R=$Z/morphology/src/main/resources/tr
curl -fsSLo zemberek.dict $R/master-dictionary.dict
for f in non-tdk proper person-names locations-tr; do curl -fsSLo z_$f.dict $R/$f.dict; done
