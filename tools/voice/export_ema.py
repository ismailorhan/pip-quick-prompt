"""Export EMA Lightning (original weights) to three ONNX graphs: text, sound, decoder. Batch 1."""
import sys, json
sys.path.append(sys.argv[4])   # onnx (installed into a scratch folder for the export)
import torch
torch.expm1 = lambda x: torch.exp(x) - 1   # no ONNX op for expm1; the same within float error here (x <= 6)
sys.path.insert(0, sys.argv[1])               # the ema_lightning 1.1.2 source (src/)
from ema_lightning.model import load_acoustic
from ema_lightning.decoder import load_decoder
W, OUT = sys.argv[2], sys.argv[3]
model = load_acoustic(W + "/ema.pt", "cpu")
dec = load_decoder(W + "/decoder.pt", "cpu")
json.dump({"vocab": model.vocab, "times": model.times, "latent_dim": model.latent_dim, "hop": dec.hop, "rate": 48000},
          open(OUT + "/ema.json", "w"), ensure_ascii=False)

class Text(torch.nn.Module):
    def __init__(s, m): super().__init__(); s.m = m
    def forward(s, ids, mask): return s.m.text_stage(ids, mask)
class Sound(torch.nn.Module):
    def __init__(s, m): super().__init__(); s.m = m
    def forward(s, h, dur, mask, cw, wstart, fw, fp, fmask, noise):
        return s.m.sound_stage(h, dur, mask, cw, wstart, fw, fp, fmask, noise)
class Dec(torch.nn.Module):
    def __init__(s, d): super().__init__(); s.d = d
    def forward(s, z): return s.d(z, None)

L, T, K = 23, 40, len(model.times)
ids = torch.randint(2, len(model.vocab), (1, L)); mask = torch.ones(1, L, dtype=torch.bool)
torch.onnx.export(Text(model), (ids, mask), OUT + "/ema-text.onnx", input_names=["ids", "mask"], output_names=["h", "dur"],
                  dynamic_axes={"ids": {1: "L"}, "mask": {1: "L"}, "h": {1: "L"}, "dur": {1: "L"}}, opset_version=17, dynamo=False)
h, dur = model.text_stage(ids, mask)
cw = torch.arange(L)[None] // 4; wstart = (cw * 4)
fw = (torch.arange(T)[None] * (int(cw.max()) + 1) // T); fp = torch.rand(1, T)
args = (h, dur, mask, cw, wstart, fw, fp, torch.ones(1, T, dtype=torch.bool), torch.randn(1, K, T, model.latent_dim))
names = ["h", "dur", "mask", "cw", "wstart", "fw", "fp", "fmask", "noise"]
axes = {"h": {1: "L"}, "dur": {1: "L"}, "mask": {1: "L"}, "cw": {1: "L"}, "wstart": {1: "L"},
        "fw": {1: "T"}, "fp": {1: "T"}, "fmask": {1: "T"}, "noise": {2: "T"}, "latents": {1: "T"}}
torch.onnx.export(Sound(model), args, OUT + "/ema-sound.onnx", input_names=names, output_names=["latents"],
                  dynamic_axes=axes, opset_version=17, dynamo=False)
z = torch.randn(1, model.latent_dim, T)
torch.onnx.export(Dec(dec), (z,), OUT + "/ema-decoder.onnx", input_names=["z"], output_names=["audio"],
                  dynamic_axes={"z": {2: "T"}, "audio": {1: "S"}}, opset_version=17, dynamo=False)
print("exported", K, "steps, vocab", len(model.vocab))
