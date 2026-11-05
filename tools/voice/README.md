# Voice models (read aloud)

The engine reads answers aloud with two local voices (`src/QuickPrompt.Voice`, ONNX Runtime). Nothing here runs at
build time: the models are downloaded once by the app (`VoiceModels.cs`, size and SHA-256 checked).

| Voice | Language | Source | Licence | Files |
|---|---|---|---|---|
| EMA Lightning 1.1.2 | Turkish | `canberkkkkkk/ema-lightning` at `e7c96f2` (Hugging Face), our ONNX export | Apache-2.0 | `voice-models-1` release of this repository |
| Kokoro-82M v1.0, fp32 | English | `onnx-community/Kokoro-82M-v1.0-ONNX` at `1939ad2` | Apache-2.0 | Hugging Face, fixed revision |

Text frontends:
- Turkish: `autolex` in the page (English words respelled, `tools/autolex`), then normalizer-tr 0.8 (numbers, dates,
  symbols; Apache-2.0) through `native/normalizer` (a C ABI, built with cargo by `native/macos/build-app.sh`), then
  EMA's own alphabet and chunker (ported in `EmaVoice.cs`).
- English: no eSpeak (GPL). misaki's US lexicons (Apache-2.0) and CMUdict (BSD-2), merged by `build_en_lexicon.py` into
  `src/QuickPrompt.Voice/Data/en-lexicon.txt.gz`; inflections, compounds and letters in `EnglishG2P.cs`.

## Re-exporting EMA

1. Download `ema_lightning` 1.1.2 (sdist) and the weights (`config.json`, `ema.pt`, `decoder.pt`) at a fixed revision.
2. `python export_ema.py <ema_lightning src> <weights dir> <out dir> <folder with onnx installed>` writes
   `ema-text.onnx`, `ema-sound.onnx`, `ema-decoder.onnx` and `ema.json` (opset 17, batch 1). `expm1` is written as
   `exp(x) - 1` (no ONNX op).
3. `python verify_ema.py …` compares them with PyTorch on real sentences (2026-11-06: audio within 2e-4, a 7 s sentence
   in 159 ms on an M-series CPU).
4. Upload the four files to a new `voice-models-N` release and update the URLs, sizes and SHA-256 in `VoiceModels.cs`.

## Rebuilding the English lexicon

`python3 -I build_en_lexicon.py us_gold.json us_silver.json ../autolex/data/cmudict.dict` (misaki's files from
`hexgrad/misaki` `misaki/data`, commit `fba1236`).
