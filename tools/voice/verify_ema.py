"""Check the ONNX graphs against the original PyTorch model on real sentences, and time them on this CPU.

python verify.py <ema src> <weights> <onnx dir> <pylib> <out dir>
"""
import sys, time, json
sys.path.insert(0, sys.argv[1])
sys.path.append(sys.argv[4])
import numpy as np
import torch
torch.expm1 = lambda x: torch.exp(x) - 1
import onnxruntime as ort
from ema_lightning.model import load_acoustic
from ema_lightning.decoder import load_decoder
from ema_lightning.engine import Engine
from ema_lightning.audio import write_wav

W, O, OUT = sys.argv[2], sys.argv[3], sys.argv[5]
model = load_acoustic(W + "/ema.pt", "cpu")
dec = load_decoder(W + "/decoder.pt", "cpu")
eng = Engine(model, dec, "cpu")
opt = ort.SessionOptions()
opt.intra_op_num_threads = 4
text = ort.InferenceSession(O + "/ema-text.onnx", opt)
sound = ort.InferenceSession(O + "/ema-sound.onnx", opt)
decoder = ort.InferenceSession(O + "/ema-decoder.onnx", opt)

SENTENCES = [
    "merhaba, cevapları ben okuyacağım.",
    "kısaca: sörvis leyıra bağlanamamanızın sebebi büyük ihtimalle sertifika.",
    "toplantıyı on iki kasım çarşamba saat on dört otuza aldım; katılımcılar ayşe, mehmet ve es ey pi biznıs van ekibi.",
]
for n, s in enumerate(SENTENCES):
    p = eng.piece(s, 0.0, 7)
    eng.plan([p], 1.0)
    eng.think([p])
    ref = dec(p.latents[None].transpose(1, 2), None)[0].numpy()

    t0 = time.perf_counter()
    ids = p.ids[None].numpy().astype(np.int64)
    mask = np.ones_like(ids, dtype=bool)
    h, dur = text.run(None, {"ids": ids, "mask": mask})
    t1 = time.perf_counter()
    noise = eng.noise(p)[None].numpy()
    feeds = {"h": p.h[None].numpy(), "dur": p.dur[None].numpy(), "mask": mask,
             "cw": p.cw[None].numpy().astype(np.int64), "wstart": p.wstart[None].numpy().astype(np.int64),
             "fw": p.fw[None].numpy().astype(np.int64), "fp": p.fp[None].numpy(),
             "fmask": np.ones((1, p.frames), dtype=bool), "noise": noise}
    (lat,) = sound.run(None, feeds)
    t2 = time.perf_counter()
    (audio,) = decoder.run(None, {"z": lat.transpose(0, 2, 1).copy()})
    t3 = time.perf_counter()
    seconds = audio.shape[1] / 48000
    print(f"{n}: dur diff {np.abs(dur - p.dur[None].numpy()).max():.2e}, latents diff {np.abs(lat[0] - p.latents.numpy()).max():.2e}, "
          f"audio diff {np.abs(audio[0] - ref).max():.2e}; {seconds:.2f} s of audio in "
          f"{1000*(t3-t0):.0f} ms (text {1000*(t1-t0):.0f}, sound {1000*(t2-t1):.0f}, decode {1000*(t3-t2):.0f})")
    write_wav(f"{OUT}/ema-onnx-{n}.wav", audio[0], 48000)
